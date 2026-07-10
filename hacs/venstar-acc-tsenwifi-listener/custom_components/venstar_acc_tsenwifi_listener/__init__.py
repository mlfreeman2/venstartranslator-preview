"""The Venstar ACC-TSENWIFI Listener integration."""
from __future__ import annotations

import asyncio
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import timedelta
import logging

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant, callback
from homeassistant.exceptions import ConfigEntryNotReady
from homeassistant.helpers import device_registry as dr
from homeassistant.helpers.dispatcher import async_dispatcher_send
from homeassistant.helpers.event import async_track_time_interval

from .const import (
    AVAILABILITY_SCAN_INTERVAL,
    CONF_IGNORE_LOCAL_EMULATED,
    CONF_PORT,
    DOMAIN,
    SIGNAL_AVAILABILITY,
    UDP_PORT,
)
from .listener import (
    DeviceManager,
    ListenerCounters,
    VenstarListenerProtocol,
    async_create_listener,
    get_ignore_prefixes,
)
from .storage import VenstarListenerStorage

_LOGGER = logging.getLogger(__name__)

PLATFORMS = [Platform.SENSOR]


@dataclass
class ListenerRuntimeData:
    """Runtime data stored on the config entry."""

    device_manager: DeviceManager
    counters: ListenerCounters
    transport: asyncio.DatagramTransport | None = None
    protocol: VenstarListenerProtocol | None = None
    unsub_callbacks: list[Callable[[], None]] = field(default_factory=list)


type VenstarListenerConfigEntry = ConfigEntry[ListenerRuntimeData]


async def async_setup_entry(
    hass: HomeAssistant, entry: VenstarListenerConfigEntry
) -> bool:
    """Set up Venstar ACC-TSENWIFI Listener from a config entry."""
    storage = VenstarListenerStorage(hass)
    roster = await storage.async_load()
    counters = ListenerCounters()
    device_manager = DeviceManager(hass, storage, roster, counters)
    entry.runtime_data = ListenerRuntimeData(
        device_manager=device_manager, counters=counters
    )

    # Platforms are forwarded BEFORE the socket opens: a packet arriving before
    # sensor.py subscribes would fire discovery into the void and leave that
    # device permanently entity-less until reload (§6h).
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)

    port = entry.options.get(CONF_PORT, UDP_PORT)
    ignore_local_emulated = entry.options.get(CONF_IGNORE_LOCAL_EMULATED, False)

    def _ignore_prefixes_provider() -> frozenset[str]:
        return get_ignore_prefixes(hass, ignore_local_emulated)

    def _protocol_factory() -> VenstarListenerProtocol:
        return VenstarListenerProtocol(
            device_manager, counters, _ignore_prefixes_provider
        )

    try:
        transport, protocol = await async_create_listener(hass, port, _protocol_factory)
    except OSError as err:
        # Port already held without SO_REUSEADDR → retry with backoff instead of
        # hard-failing. Undo the platform forward so the retry starts clean.
        await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
        raise ConfigEntryNotReady(f"Unable to bind UDP port {port}: {err}") from err

    entry.runtime_data.transport = transport
    entry.runtime_data.protocol = protocol

    # Staleness sweep: dispatch a payload-less availability tick so every entity
    # re-evaluates availability without this timer holding entity references.
    @callback
    def _availability_tick(_now) -> None:
        async_dispatcher_send(hass, SIGNAL_AVAILABILITY)

    entry.runtime_data.unsub_callbacks.append(
        async_track_time_interval(
            hass, _availability_tick, timedelta(seconds=AVAILABILITY_SCAN_INTERVAL)
        )
    )

    # An options change (port / ignore_local_emulated) reloads the entry to
    # rebind and re-apply the filter.
    entry.async_on_unload(entry.add_update_listener(_async_update_listener))

    return True


async def async_unload_entry(
    hass: HomeAssistant, entry: VenstarListenerConfigEntry
) -> bool:
    """Unload a config entry."""
    data = entry.runtime_data

    # Cancel the timer → close the socket (stop packets) → unload platform →
    # final immediate roster save (§6h).
    for unsub in data.unsub_callbacks:
        unsub()
    data.unsub_callbacks.clear()

    if data.transport is not None:
        data.transport.close()

    unloaded = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)

    await data.device_manager.async_save_final()

    return unloaded


async def _async_update_listener(
    hass: HomeAssistant, entry: VenstarListenerConfigEntry
) -> None:
    """Reload on options change to rebind the socket and re-apply the filter."""
    await hass.config_entries.async_reload(entry.entry_id)


async def async_remove_config_entry_device(
    hass: HomeAssistant,
    entry: VenstarListenerConfigEntry,
    device_entry: dr.DeviceEntry,
) -> bool:
    """Allow deleting a discovered device; purge its mac from the roster.

    A device that is still transmitting will simply be rediscovered on its next
    packet — stop the source (or enable ignore_local_emulated) to make deletion
    stick (§2.7).
    """
    manager = entry.runtime_data.device_manager
    for domain, identifier in device_entry.identifiers:
        if domain == DOMAIN:
            await manager.async_remove(identifier)
    return True
