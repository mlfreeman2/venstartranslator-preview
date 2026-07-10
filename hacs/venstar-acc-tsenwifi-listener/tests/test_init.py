"""Setup / unload / lifecycle tests."""
from __future__ import annotations

from unittest.mock import patch

from homeassistant.config_entries import ConfigEntryState
from homeassistant.core import HomeAssistant
from homeassistant.helpers import device_registry as dr
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.venstar_acc_tsenwifi_listener import (
    async_remove_config_entry_device,
)
from custom_components.venstar_acc_tsenwifi_listener.const import (
    CONF_PORT,
    DOMAIN,
    UDP_PORT,
)

from .helpers import feed, setup_listener
from .packet_factory import build_packet


async def test_setup_and_unload(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    assert entry.state is ConfigEntryState.LOADED
    assert entry.runtime_data.transport is not None
    assert entry.runtime_data.protocol is not None

    assert await hass.config_entries.async_unload(entry.entry_id)
    await hass.async_block_till_done()
    assert entry.state is ConfigEntryState.NOT_LOADED


async def test_default_port_used(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    assert entry.runtime_data.transport.get_extra_info("sockname")[1] == UDP_PORT


async def test_bind_conflict_raises_config_entry_not_ready(hass: HomeAssistant) -> None:
    """A bind failure must retry (ConfigEntryNotReady), not hard-fail the entry."""
    entry = MockConfigEntry(domain=DOMAIN, data={}, options={CONF_PORT: 15321})
    entry.add_to_hass(hass)

    # Simulate the port being held: async_create_listener raises OSError.
    with patch(
        "custom_components.venstar_acc_tsenwifi_listener.async_create_listener",
        side_effect=OSError("address already in use"),
    ):
        assert not await hass.config_entries.async_setup(entry.entry_id)
        await hass.async_block_till_done()
        assert entry.state is ConfigEntryState.SETUP_RETRY

    # Port freed (autouse mock restored) → a reload succeeds.
    await hass.config_entries.async_reload(entry.entry_id)
    await hass.async_block_till_done()
    assert entry.state is ConfigEntryState.LOADED


async def test_options_change_reloads_and_rebinds(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    old_transport = entry.runtime_data.transport

    hass.config_entries.async_update_entry(entry, options={CONF_PORT: 15322})
    await hass.async_block_till_done()

    assert entry.state is ConfigEntryState.LOADED
    assert entry.runtime_data.transport is not old_transport
    assert entry.runtime_data.transport.get_extra_info("sockname")[1] == 15322


async def test_device_removal_and_rediscovery(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    await feed(hass, entry, build_packet(mac="428e0486d800", temperature=120))

    registry = dr.async_get(hass)
    device = registry.async_get_device(identifiers={(DOMAIN, "428e0486d800")})
    assert device is not None

    assert await async_remove_config_entry_device(hass, entry, device)
    assert "428e0486d800" not in entry.runtime_data.device_manager.roster

    # Still transmitting → rediscovered on the next packet (§2.7).
    await feed(
        hass, entry, build_packet(mac="428e0486d800", temperature=122, sequence=11)
    )
    assert "428e0486d800" in entry.runtime_data.device_manager.roster


async def test_real_socket_bind(hass: HomeAssistant, socket_enabled: None) -> None:
    """Exercise the real socket path (bypasses the autouse endpoint mock by
    calling into the listener module directly)."""
    from custom_components.venstar_acc_tsenwifi_listener.listener import (
        DeviceManager,
        ListenerCounters,
        VenstarListenerProtocol,
        async_create_listener,
    )
    from custom_components.venstar_acc_tsenwifi_listener.storage import (
        VenstarListenerStorage,
    )

    counters = ListenerCounters()
    manager = DeviceManager(hass, VenstarListenerStorage(hass), {}, counters)

    def factory() -> VenstarListenerProtocol:
        return VenstarListenerProtocol(manager, counters, frozenset)

    transport, protocol = await async_create_listener(hass, 15488, factory)
    try:
        assert transport.get_extra_info("sockname")[1] == 15488
        assert isinstance(protocol, VenstarListenerProtocol)
    finally:
        transport.close()
