"""Config-entry diagnostics for the Venstar ACC-TSENWIFI Listener.

The first thing to ask for in a "why isn't my sensor showing up" issue: the full
roster, the packet counters, and the churn-prone last-reading fields that are
deliberately kept off entity attributes (§6d / §6j).
"""
from __future__ import annotations

from typing import TYPE_CHECKING, Any

from homeassistant.core import HomeAssistant

if TYPE_CHECKING:
    from . import VenstarListenerConfigEntry


async def async_get_config_entry_diagnostics(
    hass: HomeAssistant, entry: VenstarListenerConfigEntry
) -> dict[str, Any]:
    """Return diagnostics for a config entry."""
    data = entry.runtime_data
    manager = data.device_manager

    roster: dict[str, Any] = {}
    for mac, device in manager.roster.items():
        roster[mac] = {
            "name": device.name,
            "purpose": device.purpose,
            "sensor_id": device.sensor_id,
            "has_battery": device.has_battery,
            "has_humidity": device.has_humidity,
            "firmware": f"{device.fw_major}.{device.fw_minor}",
            "last_seen": device.last_seen.isoformat() if device.last_seen else None,
            # In-memory only — empty after a restart until the next packet (§6j).
            "last_sequence": device.last_sequence,
            "last_reading": {
                "temp_c": device.temp_c,
                "fault": device.fault,
                "battery": device.battery,
                "humidity": device.humidity,
                "raw_index": device.raw_index,
            },
            "source_ip": device.source_ip,
        }

    return {
        "options": dict(entry.options),
        "counters": data.counters.as_dict(),
        "roster": roster,
    }
