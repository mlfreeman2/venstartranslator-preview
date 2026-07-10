"""Config-entry diagnostics tests."""
from __future__ import annotations

from homeassistant.core import HomeAssistant

from custom_components.venstar_acc_tsenwifi_listener.diagnostics import (
    async_get_config_entry_diagnostics,
)

from .helpers import feed, setup_listener
from .packet_factory import build_packet

MAC = "428e0486d800"


async def test_diagnostics_report(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac=MAC, temperature=124, battery=100, name="Den"))
    await feed(hass, entry, b"\x00 not a packet")

    diag = await async_get_config_entry_diagnostics(hass, entry)

    assert diag["counters"]["parsed"] == 1
    assert diag["counters"]["dropped_unparseable"] == 1

    assert MAC in diag["roster"]
    device = diag["roster"][MAC]
    assert device["name"] == "Den"
    assert device["has_battery"] is True
    assert device["last_reading"]["temp_c"] == 22.0
    assert device["last_reading"]["raw_index"] == 124
    assert device["source_ip"] == "192.0.2.1"
    assert "options" in diag
