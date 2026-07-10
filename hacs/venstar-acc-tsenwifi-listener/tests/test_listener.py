"""DeviceManager + protocol behavior: discovery, dedup, capabilities, rename,
and packet counters."""
from __future__ import annotations

from homeassistant.core import HomeAssistant
from homeassistant.helpers import device_registry as dr
from homeassistant.helpers.dispatcher import async_dispatcher_connect

from custom_components.venstar_acc_tsenwifi_listener.const import (
    DOMAIN,
    SIGNAL_NEW_DEVICE,
    SIGNAL_UPDATE,
)

from .helpers import feed, setup_listener
from .packet_factory import build_packet

MAC = "428e0486d800"


async def test_new_device_discovered(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    events = []
    entry.async_on_unload(
        async_dispatcher_connect(hass, SIGNAL_NEW_DEVICE, lambda r: events.append(r))
    )

    await feed(hass, entry, build_packet(mac=MAC, temperature=124, name="Living Room"))

    roster = entry.runtime_data.device_manager.roster
    assert MAC in roster
    assert roster[MAC].name == "Living Room"
    assert roster[MAC].temp_c == 22.0
    assert len(events) == 1


async def test_five_repeats_dedup_to_one(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    updates = []
    entry.async_on_unload(
        async_dispatcher_connect(
            hass, SIGNAL_UPDATE.format(MAC), lambda r: updates.append(r)
        )
    )

    packet = build_packet(mac=MAC, temperature=124, sequence=42)
    for _ in range(5):
        await feed(hass, entry, packet)

    counters = entry.runtime_data.counters
    assert counters.parsed == 5
    assert counters.deduped == 4  # first is discovery; the next four are dups
    # last_seen still refreshed on dups (keeps the sensor from going stale)
    assert entry.runtime_data.device_manager.roster[MAC].last_sequence == 42
    # a brand-new device dispatches SIGNAL_NEW_DEVICE, not SIGNAL_UPDATE
    assert updates == []


async def test_resend_same_sequence_refreshes_last_seen(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, sequence=7))
    device = entry.runtime_data.device_manager.roster[MAC]
    first_seen = device.last_seen

    # C#-app "resend last packet" reuses the same sequence by design.
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, sequence=7))
    assert device.last_seen >= first_seen
    assert entry.runtime_data.counters.deduped == 1


async def test_pairing_then_data_sequence_one_collision(hass: HomeAssistant) -> None:
    """Pairing (seq 1) then data (seq 1) → the data packet is deduped; the next
    data packet (seq 2) is processed. Known, accepted loss (§4)."""
    entry = await setup_listener(hass)
    from custom_components.venstar_acc_tsenwifi_listener.protobuf import (
        sensor_message_pb2 as pb,
    )

    await feed(
        hass,
        entry,
        build_packet(
            mac=MAC, temperature=120, sequence=1, command=pb.SensorMessage.SENSORPAIR
        ),
    )
    device = entry.runtime_data.device_manager.roster[MAC]
    assert device.temp_c == 20.0  # pairing packet delivered the reading

    # Data packet also at seq 1 → deduped (not reprocessed).
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, sequence=1))
    assert device.temp_c == 20.0
    assert entry.runtime_data.counters.deduped == 1

    # The stream self-heals at seq 2.
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, sequence=2))
    assert device.temp_c == 22.0


async def test_capability_appears_later(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    new_device_events = []
    entry.async_on_unload(
        async_dispatcher_connect(
            hass, SIGNAL_NEW_DEVICE, lambda r: new_device_events.append(r)
        )
    )

    await feed(hass, entry, build_packet(mac=MAC, temperature=120, sequence=1))
    device = entry.runtime_data.device_manager.roster[MAC]
    assert device.has_humidity is False
    assert len(new_device_events) == 1  # initial discovery

    # A later packet carrying humidity flips the flag and re-fires discovery.
    await feed(hass, entry, build_packet(mac=MAC, temperature=120, humidity=55, sequence=2))
    assert device.has_humidity is True
    assert device.humidity == 55
    assert len(new_device_events) == 2  # capability re-fire


async def test_wire_rename_updates_device(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, name="Old Name", sequence=1))

    registry = dr.async_get(hass)
    device = registry.async_get_device(identifiers={(DOMAIN, MAC)})
    assert device.name == "Old Name"

    await feed(hass, entry, build_packet(mac=MAC, name="New Name", sequence=2))
    device = registry.async_get_device(identifiers={(DOMAIN, MAC)})
    assert device.name == "New Name"


async def test_user_rename_wins_over_wire(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, name="Wire Name", sequence=1))

    registry = dr.async_get(hass)
    device = registry.async_get_device(identifiers={(DOMAIN, MAC)})
    registry.async_update_device(device.id, name_by_user="My Bedroom")

    # A subsequent wire rename updates `name` but must preserve name_by_user.
    await feed(hass, entry, build_packet(mac=MAC, name="Wire Name 2", sequence=2))
    device = registry.async_get_device(identifiers={(DOMAIN, MAC)})
    assert device.name == "Wire Name 2"
    assert device.name_by_user == "My Bedroom"


async def test_counters_track_drops(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)

    await feed(hass, entry, build_packet(mac=MAC, temperature=120))  # parsed
    await feed(hass, entry, b"\xde\xad\xbe\xef garbage")  # unparseable
    await feed(hass, entry, build_packet(with_sensor_data=False))  # invalid (no data)
    await feed(hass, entry, build_packet(mac="not-a-mac", temperature=120))  # invalid mac

    counters = entry.runtime_data.counters
    assert counters.parsed == 1
    assert counters.dropped_unparseable == 1
    assert counters.dropped_invalid == 2
