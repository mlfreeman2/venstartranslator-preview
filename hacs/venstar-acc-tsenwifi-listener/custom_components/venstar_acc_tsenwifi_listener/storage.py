"""Roster persistence for the Venstar ACC-TSENWIFI Listener.

The roster is the single source of truth for each discovered device, including
``last_seen`` (which drives availability). A subset of fields is persisted via
the HA Store API so devices — and their availability — survive a restart. The
transient fields (last sequence, last reading, source IP) are deliberately not
persisted (see §6f / §6j of the implementation plan) and read empty after a
restart until the next packet arrives.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import logging
from typing import TYPE_CHECKING, Any

from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.storage import Store
from homeassistant.util import dt as dt_util

from .const import (
    DEFAULT_PURPOSE,
    PURPOSE_OUTDOOR,
    STALE_DEFAULT,
    STALE_OUTDOOR,
    STORAGE_KEY,
    STORAGE_VERSION,
)

if TYPE_CHECKING:
    from .listener import DecodedReading

_LOGGER = logging.getLogger(__name__)

# Debounce window for routine last_seen writes. Immediate writes on every packet
# would hit the disk once per sensor per minute forever — real wear on SD-card
# installs (mirrors the emulator's storage rationale).
SAVE_DELAY = 10.0


@dataclass
class DiscoveredDevice:
    """A sensor heard on the wire: the live roster entry and its persisted form.

    Fields above ``last_seen`` (inclusive) are persisted; everything below is
    transient — populated from live packets and empty after a restart until the
    next packet for this mac arrives.
    """

    mac: str
    name: str
    purpose: str
    sensor_id: int
    fw_major: int = 0
    fw_minor: int = 0
    has_battery: bool = False
    has_humidity: bool = False
    last_seen: datetime | None = None

    # transient (never persisted)
    last_sequence: int | None = None
    temp_c: float | None = None
    fault: str | None = None
    battery: int | None = None
    humidity: int | None = None
    power: str | None = None
    source_ip: str | None = None
    raw_index: int | None = None

    @classmethod
    def from_reading(cls, reading: DecodedReading) -> DiscoveredDevice:
        """Create a brand-new roster entry from the first packet for a mac."""
        device = cls(
            mac=reading.mac,
            name=reading.name,
            purpose=reading.purpose,
            sensor_id=reading.sensor_id,
        )
        device.apply_reading(reading)
        return device

    def apply_reading(self, reading: DecodedReading) -> bool:
        """Fold a decoded reading into this device's live state.

        Returns True when a battery/humidity capability appears for the first
        time, which drives dynamic entity creation (§6d).
        """
        new_capability = False
        if reading.battery is not None and not self.has_battery:
            self.has_battery = True
            new_capability = True
        if reading.humidity is not None and not self.has_humidity:
            self.has_humidity = True
            new_capability = True

        self.name = reading.name
        self.purpose = reading.purpose
        self.sensor_id = reading.sensor_id
        self.fw_major = reading.fw_major
        self.fw_minor = reading.fw_minor
        self.last_seen = reading.received_at
        self.last_sequence = reading.sequence
        self.temp_c = reading.temp_c
        self.fault = reading.fault
        self.battery = reading.battery
        self.humidity = reading.humidity
        self.power = reading.power
        self.source_ip = reading.source_ip
        self.raw_index = reading.raw_index
        return new_capability

    @property
    def has_live_reading(self) -> bool:
        """True once a packet has been processed this session (not just restored)."""
        return self.last_sequence is not None

    @property
    def staleness_threshold(self) -> int:
        """Seconds without a packet before the sensor counts as stale (§6e)."""
        return STALE_OUTDOOR if self.purpose == PURPOSE_OUTDOOR else STALE_DEFAULT

    def is_stale(self, now: datetime) -> bool:
        """Whether the sensor has stopped transmitting (drives unavailable)."""
        if self.last_seen is None:
            return True
        return (now - self.last_seen).total_seconds() >= self.staleness_threshold

    def to_storage(self) -> dict[str, Any]:
        """Serialize the persisted subset (the mac is the roster key)."""
        return {
            "name": self.name,
            "purpose": self.purpose,
            "sensor_id": self.sensor_id,
            "fw_major": self.fw_major,
            "fw_minor": self.fw_minor,
            "has_battery": self.has_battery,
            "has_humidity": self.has_humidity,
            "last_seen": self.last_seen.isoformat() if self.last_seen else None,
        }

    @classmethod
    def from_storage(cls, mac: str, data: dict[str, Any]) -> DiscoveredDevice:
        """Rebuild a roster entry from persisted data on startup."""
        last_seen_raw = data.get("last_seen")
        return cls(
            mac=mac,
            name=data.get("name") or f"Venstar {mac[-4:]}",
            purpose=data.get("purpose") or DEFAULT_PURPOSE,
            sensor_id=int(data.get("sensor_id", 0)),
            fw_major=int(data.get("fw_major", 0)),
            fw_minor=int(data.get("fw_minor", 0)),
            has_battery=bool(data.get("has_battery", False)),
            has_humidity=bool(data.get("has_humidity", False)),
            last_seen=dt_util.parse_datetime(last_seen_raw) if last_seen_raw else None,
        )


def _serialize(roster: dict[str, DiscoveredDevice]) -> dict[str, Any]:
    return {"devices": {mac: device.to_storage() for mac, device in roster.items()}}


class VenstarListenerStorage:
    """Thin wrapper over the HA Store for the discovered-device roster."""

    def __init__(self, hass: HomeAssistant) -> None:
        self._store = Store[dict[str, Any]](hass, STORAGE_VERSION, STORAGE_KEY)

    async def async_load(self) -> dict[str, DiscoveredDevice]:
        """Load the roster from disk (empty dict on first run)."""
        data = await self._store.async_load()
        if not data:
            return {}
        roster: dict[str, DiscoveredDevice] = {}
        for mac, entry in data.get("devices", {}).items():
            try:
                roster[mac] = DiscoveredDevice.from_storage(mac, entry)
            except (TypeError, ValueError) as err:
                _LOGGER.warning("Skipping unreadable roster entry %s: %s", mac, err)
        _LOGGER.debug("Loaded roster with %d device(s)", len(roster))
        return roster

    async def async_save(self, roster: dict[str, DiscoveredDevice]) -> None:
        """Write the roster to disk immediately (new device, capability, rename,
        deletion, and the final flush on unload)."""
        await self._store.async_save(_serialize(roster))

    @callback
    def async_delay_save(self, roster: dict[str, DiscoveredDevice]) -> None:
        """Schedule a debounced roster write for routine last_seen updates.

        The callable is evaluated at flush time, so it always captures the latest
        state of the (in-place mutated) roster.
        """
        self._store.async_delay_save(lambda: _serialize(roster), SAVE_DELAY)
