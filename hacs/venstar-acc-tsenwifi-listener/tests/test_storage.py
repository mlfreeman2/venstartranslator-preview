"""Roster persistence tests: what is written, what is deliberately not."""
from __future__ import annotations

from homeassistant.core import HomeAssistant

from custom_components.venstar_acc_tsenwifi_listener.const import STORAGE_KEY

from .helpers import feed, setup_listener
from .packet_factory import build_packet

MAC = "428e0486d800"


async def test_roster_persisted_on_discovery_and_flushed_on_unload(
    hass: HomeAssistant, hass_storage
) -> None:
    entry = await setup_listener(hass)
    await feed(
        hass,
        entry,
        build_packet(mac=MAC, temperature=124, battery=100, name="Persisted"),
    )

    # New-device discovery persists immediately.
    stored = hass_storage[STORAGE_KEY]["data"]["devices"]
    assert MAC in stored
    assert stored[MAC]["name"] == "Persisted"
    assert stored[MAC]["has_battery"] is True
    assert stored[MAC]["last_seen"] is not None

    # Only the persisted subset is written — transient fields stay off disk.
    assert "temp_c" not in stored[MAC]
    assert "last_sequence" not in stored[MAC]
    assert "source_ip" not in stored[MAC]

    # Unload flushes a final immediate save.
    await hass.config_entries.async_unload(entry.entry_id)
    await hass.async_block_till_done()
    assert MAC in hass_storage[STORAGE_KEY]["data"]["devices"]


async def test_capability_flag_persists(hass: HomeAssistant, hass_storage) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=120, sequence=1))
    assert hass_storage[STORAGE_KEY]["data"]["devices"][MAC]["has_humidity"] is False

    # Humidity appearing flips and immediately persists the flag.
    await feed(hass, entry, build_packet(mac=MAC, temperature=120, humidity=50, sequence=2))
    assert hass_storage[STORAGE_KEY]["data"]["devices"][MAC]["has_humidity"] is True


async def test_deletion_persists(hass: HomeAssistant, hass_storage) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=120))
    assert MAC in hass_storage[STORAGE_KEY]["data"]["devices"]

    await entry.runtime_data.device_manager.async_remove(MAC)
    assert MAC not in hass_storage[STORAGE_KEY]["data"]["devices"]
