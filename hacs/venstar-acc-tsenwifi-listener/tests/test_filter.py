"""Local-emulator filter tests (§2.10).

A co-installed emulator config entry stores its 10-hex ``mac_prefix``; when the
option is on, packets from that prefix are dropped at the validation gate.
"""
from __future__ import annotations

from homeassistant.core import HomeAssistant
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.venstar_acc_tsenwifi_listener.const import (
    CONF_IGNORE_LOCAL_EMULATED,
    EMITTER_DOMAIN,
)

from .helpers import feed, setup_listener
from .packet_factory import build_packet

EMULATED_MAC = "428e0486d800"  # prefix 428e0486d8 + sensor id 00
PHYSICAL_MAC = "aabbccddee00"


def _add_emulator_entry(hass: HomeAssistant, prefix: str = "428e0486d8") -> None:
    MockConfigEntry(
        domain=EMITTER_DOMAIN, data={"mac_prefix": prefix}, title="Emulator"
    ).add_to_hass(hass)


async def test_filter_off_by_default_shows_emulated(hass: HomeAssistant) -> None:
    _add_emulator_entry(hass)
    entry = await setup_listener(hass)  # option defaults off

    await feed(hass, entry, build_packet(mac=EMULATED_MAC, temperature=124))
    assert EMULATED_MAC in entry.runtime_data.device_manager.roster
    assert entry.runtime_data.counters.dropped_filtered == 0


async def test_filter_on_drops_emulated(hass: HomeAssistant) -> None:
    _add_emulator_entry(hass)
    entry = await setup_listener(hass, options={CONF_IGNORE_LOCAL_EMULATED: True})

    await feed(hass, entry, build_packet(mac=EMULATED_MAC, temperature=124))
    assert EMULATED_MAC not in entry.runtime_data.device_manager.roster
    assert entry.runtime_data.counters.dropped_filtered == 1
    # no unique_id was ever minted
    from homeassistant.helpers import entity_registry as er

    assert not [
        e for e in er.async_get(hass).entities.values() if EMULATED_MAC in e.unique_id
    ]


async def test_filter_on_leaves_physical(hass: HomeAssistant) -> None:
    _add_emulator_entry(hass)
    entry = await setup_listener(hass, options={CONF_IGNORE_LOCAL_EMULATED: True})

    await feed(hass, entry, build_packet(mac=PHYSICAL_MAC, temperature=124))
    assert PHYSICAL_MAC in entry.runtime_data.device_manager.roster
    assert entry.runtime_data.counters.dropped_filtered == 0


async def test_deleted_emulated_device_stays_gone_when_filtered(
    hass: HomeAssistant,
) -> None:
    """With the filter on, a filtered mac never re-enters the roster — so a
    deletion sticks (the §2.7 rediscovery caveat doesn't apply)."""
    _add_emulator_entry(hass)
    entry = await setup_listener(hass, options={CONF_IGNORE_LOCAL_EMULATED: True})

    for seq in range(1, 4):
        await feed(hass, entry, build_packet(mac=EMULATED_MAC, temperature=124, sequence=seq))
    assert EMULATED_MAC not in entry.runtime_data.device_manager.roster
