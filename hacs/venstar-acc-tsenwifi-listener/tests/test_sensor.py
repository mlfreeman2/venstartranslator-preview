"""Entity-level tests: creation, dynamic capabilities, attributes, faults,
staleness/availability, and restart restore."""
from __future__ import annotations

from datetime import timedelta

from homeassistant.const import STATE_UNAVAILABLE, STATE_UNKNOWN
from homeassistant.core import HomeAssistant, State
from homeassistant.helpers import entity_registry as er
from pytest_homeassistant_custom_component.common import (
    async_fire_time_changed,
    mock_restore_cache_with_extra_data,
)

from custom_components.venstar_acc_tsenwifi_listener.const import STORAGE_KEY

from .helpers import entity_id_for, feed, setup_listener
from .packet_factory import build_packet

MAC = "428e0486d800"


async def test_temperature_entity_created(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, name="Living Room"))

    entity_id = entity_id_for(hass, MAC, "temperature")
    assert entity_id is not None
    state = hass.states.get(entity_id)
    assert float(state.state) == 22.0
    assert state.attributes["device_class"] == "temperature"
    assert state.attributes["unit_of_measurement"] == "°C"


async def test_no_duplicate_entity_on_repeat(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, sequence=1))
    await feed(hass, entry, build_packet(mac=MAC, temperature=126, sequence=2))

    ent_reg = er.async_get(hass)
    temp_entities = [
        e
        for e in ent_reg.entities.values()
        if e.unique_id == f"{MAC}_temperature"
    ]
    assert len(temp_entities) == 1
    state = hass.states.get(entity_id_for(hass, MAC, "temperature"))
    assert float(state.state) == 23.0  # updated to the second reading


async def test_battery_entity_created_when_present(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=120, battery=100))

    entity_id = entity_id_for(hass, MAC, "battery")
    assert entity_id is not None
    assert hass.states.get(entity_id).state == "100"
    # battery is a diagnostic entity
    assert er.async_get(hass).async_get(entity_id).entity_category == "diagnostic"


async def test_humidity_entity_is_dynamic(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    # No Humidity field → no humidity entity.
    await feed(hass, entry, build_packet(mac=MAC, temperature=120, sequence=1))
    assert entity_id_for(hass, MAC, "humidity") is None

    # A packet carrying Humidity creates the entity.
    await feed(hass, entry, build_packet(mac=MAC, temperature=120, humidity=48, sequence=2))
    entity_id = entity_id_for(hass, MAC, "humidity")
    assert entity_id is not None
    assert hass.states.get(entity_id).state == "48"


async def test_temperature_attributes(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(
        hass,
        entry,
        build_packet(mac=MAC, sensor_id=0, temperature=124, set_type=False),
        source_ip="10.0.0.9",
    )

    attrs = hass.states.get(entity_id_for(hass, MAC, "temperature")).attributes
    assert attrs["sensor_id"] == 0
    assert attrs["purpose"] == "Remote"
    assert attrs["power_source"] == "battery"
    assert attrs["firmware"] == "4.2"
    assert attrs["source_ip"] == "10.0.0.9"
    assert attrs["raw_index"] == 124
    # churn-prone fields must NOT be entity attributes (§6d)
    assert "sequence" not in attrs
    assert "last_seen" not in attrs


async def test_fault_sentinel_unknown_but_available(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=254))

    state = hass.states.get(entity_id_for(hass, MAC, "temperature"))
    assert state.state == STATE_UNKNOWN  # no value...
    assert state.state != STATE_UNAVAILABLE  # ...but still transmitting
    assert state.attributes["fault"] == "shorted"


async def test_staleness_and_recovery(hass: HomeAssistant, freezer) -> None:
    freezer.move_to("2026-01-01 12:00:00+00:00")
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, sequence=1))

    entity_id = entity_id_for(hass, MAC, "temperature")
    assert hass.states.get(entity_id).state != STATE_UNAVAILABLE

    # Remote sensor: unavailable after 5 minutes without a packet.
    freezer.tick(timedelta(minutes=6))
    async_fire_time_changed(hass)
    await hass.async_block_till_done()
    assert hass.states.get(entity_id).state == STATE_UNAVAILABLE

    # A fresh packet brings it back.
    await feed(hass, entry, build_packet(mac=MAC, temperature=126, sequence=2))
    assert hass.states.get(entity_id).state == "23.0"


async def test_outdoor_uses_longer_staleness(hass: HomeAssistant, freezer) -> None:
    from custom_components.venstar_acc_tsenwifi_listener.protobuf import (
        sensor_message_pb2 as pb,
    )

    freezer.move_to("2026-01-01 12:00:00+00:00")
    entry = await setup_listener(hass)
    await feed(
        hass, entry, build_packet(mac=MAC, temperature=124, sensor_type=pb.INFO.OUTDOOR)
    )
    entity_id = entity_id_for(hass, MAC, "temperature")

    # Still available at 6 minutes (Outdoor threshold is 20).
    freezer.tick(timedelta(minutes=6))
    async_fire_time_changed(hass)
    await hass.async_block_till_done()
    assert hass.states.get(entity_id).state != STATE_UNAVAILABLE

    # Unavailable past 20 minutes.
    freezer.tick(timedelta(minutes=15))
    async_fire_time_changed(hass)
    await hass.async_block_till_done()
    assert hass.states.get(entity_id).state == STATE_UNAVAILABLE


def _seed_roster(hass_storage, *, last_seen: str, has_battery: bool = True) -> None:
    hass_storage[STORAGE_KEY] = {
        "version": 1,
        "data": {
            "devices": {
                MAC: {
                    "name": "Restored Sensor",
                    "purpose": "Remote",
                    "sensor_id": 0,
                    "fw_major": 4,
                    "fw_minor": 2,
                    "has_battery": has_battery,
                    "has_humidity": False,
                    "last_seen": last_seen,
                }
            }
        },
    }


async def test_restart_restores_value_and_availability(
    hass: HomeAssistant, hass_storage, freezer
) -> None:
    freezer.move_to("2026-01-01 12:00:00+00:00")
    # Roster persisted with a recent last_seen, plus a restored native value.
    _seed_roster(hass_storage, last_seen="2026-01-01T11:59:00+00:00")
    from homeassistant.components.sensor import SensorExtraStoredData

    mock_restore_cache_with_extra_data(
        hass,
        (
            (
                # The state object is unused by RestoreSensor; the extra data
                # carries the native value + unit that gets restored.
                State("sensor.restored_sensor_temperature", "21.0"),
                SensorExtraStoredData(21.0, "°C").as_dict(),
            ),
        ),
    )

    entry = await setup_listener(hass)

    entity_id = entity_id_for(hass, MAC, "temperature")
    assert entity_id is not None
    state = hass.states.get(entity_id)
    # Value comes back via RestoreSensor; sensor is available (last_seen recent).
    assert float(state.state) == 21.0
    assert state.state != STATE_UNAVAILABLE
    # Persisted capability → battery entity exists again without any packet.
    assert entity_id_for(hass, MAC, "battery") is not None
    # No live packet yet this session.
    assert entry.runtime_data.device_manager.roster[MAC].has_live_reading is False


async def test_restart_past_threshold_starts_unavailable(
    hass: HomeAssistant, hass_storage, freezer
) -> None:
    freezer.move_to("2026-01-01 12:00:00+00:00")
    # last_seen is well beyond the 5-minute Remote threshold.
    _seed_roster(hass_storage, last_seen="2026-01-01T11:00:00+00:00")

    await setup_listener(hass)
    entity_id = entity_id_for(hass, MAC, "temperature")
    assert hass.states.get(entity_id).state == STATE_UNAVAILABLE
