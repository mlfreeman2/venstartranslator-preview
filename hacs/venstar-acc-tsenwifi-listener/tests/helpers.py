"""Shared helpers for the integration tests."""
from __future__ import annotations

from homeassistant.core import HomeAssistant
from homeassistant.helpers import entity_registry as er
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.venstar_acc_tsenwifi_listener.const import DOMAIN


async def setup_listener(
    hass: HomeAssistant,
    *,
    options: dict | None = None,
) -> MockConfigEntry:
    """Create and set up a listener config entry."""
    entry = MockConfigEntry(domain=DOMAIN, data={}, options=dict(options or {}))
    entry.add_to_hass(hass)
    assert await hass.config_entries.async_setup(entry.entry_id)
    await hass.async_block_till_done()
    return entry


async def feed(
    hass: HomeAssistant,
    entry: MockConfigEntry,
    packet: bytes,
    source_ip: str = "192.0.2.1",
    port: int = 5001,
) -> None:
    """Feed a raw packet through the live protocol (decode + counters + dispatch)."""
    entry.runtime_data.protocol.datagram_received(packet, (source_ip, port))
    await hass.async_block_till_done()


def entity_id_for(hass: HomeAssistant, mac: str, kind: str) -> str | None:
    """Resolve the entity_id for a given mac/kind via its unique_id."""
    return er.async_get(hass).async_get_entity_id("sensor", DOMAIN, f"{mac}_{kind}")
