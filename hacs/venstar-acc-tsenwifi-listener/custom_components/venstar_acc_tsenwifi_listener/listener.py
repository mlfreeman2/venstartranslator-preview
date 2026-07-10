"""UDP endpoint, packet decode, and the device roster manager.

The socket binds ``0.0.0.0:5001`` and every datagram is parsed, validated, and
decoded inline on the event loop — a ~98-byte packet decodes in microseconds,
so no executor hop is needed. ``decode_packet`` is pure and side-effect free so
it can be exercised directly against the golden fixtures.
"""
from __future__ import annotations

import asyncio
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
import logging
import socket

from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers import device_registry as dr
from homeassistant.helpers.dispatcher import async_dispatcher_send
from homeassistant.util import dt as dt_util

from .const import (
    DEFAULT_BIND_ADDRESS,
    DEFAULT_PURPOSE,
    DOMAIN,
    EMITTER_DOMAIN,
    FAULT_OPEN,
    FAULT_SHORTED,
    PURPOSE_OUTDOOR,
    PURPOSE_REMOTE,
    PURPOSE_RETURN,
    PURPOSE_SUPPLY,
    SIGNAL_NEW_DEVICE,
    SIGNAL_UPDATE,
)

# Imported at module top so the (slow) protobuf descriptor build happens during
# HA's executor-run integration import, not on the event loop. Importing lazily
# would trigger HA's "blocking call to import_module inside the event loop"
# warning on the first packet.
from .protobuf import sensor_message_pb2 as pb
from .storage import DiscoveredDevice, VenstarListenerStorage

_LOGGER = logging.getLogger(__name__)

# proto2 enum → label maps, built once at import.
_PURPOSE_BY_TYPE = {
    pb.INFO.OUTDOOR: PURPOSE_OUTDOOR,
    pb.INFO.RETURN: PURPOSE_RETURN,
    pb.INFO.REMOTE: PURPOSE_REMOTE,
    pb.INFO.SUPPLY: PURPOSE_SUPPLY,
}
_POWER_BY_ENUM = {
    pb.INFO.BATTERY: "battery",
    pb.INFO.WIRED: "wired",
}
_VALID_COMMANDS = frozenset({pb.SensorMessage.SENSORDATA, pb.SensorMessage.SENSORPAIR})
_HEX_DIGITS = frozenset("0123456789abcdef")


class DropReason(Enum):
    """Why a datagram was discarded. The value is the counter attribute name."""

    UNPARSEABLE = "dropped_unparseable"
    INVALID = "dropped_invalid"
    FILTERED = "dropped_filtered"


@dataclass
class ListenerCounters:
    """Packet accounting surfaced through diagnostics (§6j)."""

    parsed: int = 0
    dropped_unparseable: int = 0
    dropped_invalid: int = 0
    dropped_filtered: int = 0
    deduped: int = 0

    def record_drop(self, reason: DropReason) -> None:
        setattr(self, reason.value, getattr(self, reason.value) + 1)

    def as_dict(self) -> dict[str, int]:
        return {
            "parsed": self.parsed,
            "dropped_unparseable": self.dropped_unparseable,
            "dropped_invalid": self.dropped_invalid,
            "dropped_filtered": self.dropped_filtered,
            "deduped": self.deduped,
        }


@dataclass
class DecodedReading:
    """One validated sensor reading decoded from a packet."""

    mac: str
    command: int
    sensor_id: int
    name: str
    purpose: str
    temp_c: float | None
    fault: str | None
    battery: int | None
    humidity: int | None
    sequence: int
    power: str | None
    fw_major: int
    fw_minor: int
    raw_index: int
    source_ip: str
    received_at: datetime


def normalize_mac(raw: str) -> str | None:
    """Lowercase, strip ``:``/``-`` separators, require exactly 12 hex chars.

    The normalized mac seeds the permanent unique_id, so anything that isn't a
    clean 12-hex mac is rejected here (real-hardware mac formatting is
    unconfirmed, hence normalize rather than exact-match — §12).
    """
    mac = raw.strip().lower().replace(":", "").replace("-", "")
    if len(mac) == 12 and _HEX_DIGITS.issuperset(mac):
        return mac
    return None


def _decode_temperature(raw_index: int) -> tuple[float | None, str | None]:
    """Map a temperature index to (°C, fault). Sentinels report no value."""
    if raw_index == FAULT_SHORTED:
        return None, "shorted"
    if raw_index == FAULT_OPEN:
        return None, "open"
    # Exact inverse of the emulator's get_temperature_index; index/2 is always
    # a whole/half degree, so the result is exact in float (no rounding needed).
    return raw_index / 2 - 40, None


def _decode_purpose(info: pb.INFO) -> str:
    """Absent/unrecognized Type → Remote (§2.6); never proto2's OUTDOOR default."""
    if info.HasField("Type"):
        return _PURPOSE_BY_TYPE.get(info.Type, DEFAULT_PURPOSE)
    return DEFAULT_PURPOSE


def _decode_name(info: pb.INFO, mac: str) -> str:
    if info.HasField("Name") and info.Name:
        return info.Name
    return f"Venstar {mac[-4:]}"


def decode_packet(
    data: bytes,
    source_ip: str,
    ignore_prefixes: frozenset[str] = frozenset(),
) -> tuple[DecodedReading | None, DropReason | None]:
    """Parse, validate, and decode one datagram.

    Returns ``(reading, None)`` on success or ``(None, reason)`` when the packet
    must be dropped. The gates here are the last line of defense before a packet
    earns a permanent unique_id, so anything unvalidated is discarded.
    """
    msg = pb.SensorMessage()
    try:
        msg.ParseFromString(data)
    except Exception:  # noqa: BLE001 - protobuf raises varied types on junk bytes
        # The port is a firehose of arbitrary LAN traffic; never log-spam/raise.
        return None, DropReason.UNPARSEABLE

    # A message carrying only a Command still parses; without SensorData, reading
    # Info would yield a default INFO with Mac="" — an empty-mac ghost device.
    if not msg.HasField("SensorData"):
        return None, DropReason.INVALID
    if msg.Command not in _VALID_COMMANDS:
        return None, DropReason.INVALID

    info = msg.SensorData.Info

    mac = normalize_mac(info.Mac)
    if mac is None:
        return None, DropReason.INVALID

    # Local-emulator filter (§2.10): drop packets whose prefix matches a
    # co-installed emulator entry when the option is on.
    if ignore_prefixes and mac[:10] in ignore_prefixes:
        return None, DropReason.FILTERED

    # Temperature is uint32 on the wire. Absent field → index 0 (= -40.0 °C) for
    # emulator parity (deliberately asymmetric with battery/humidity). 254/255
    # are the fault sentinels; anything above 255 is garbage → drop.
    raw_index = info.Temperature if info.HasField("Temperature") else 0
    if raw_index > FAULT_OPEN:
        return None, DropReason.INVALID

    temp_c, fault = _decode_temperature(raw_index)
    reading = DecodedReading(
        mac=mac,
        command=msg.Command,
        sensor_id=info.SensorId,
        name=_decode_name(info, mac),
        purpose=_decode_purpose(info),
        temp_c=temp_c,
        fault=fault,
        battery=info.Battery if info.HasField("Battery") else None,
        humidity=info.Humidity if info.HasField("Humidity") else None,
        sequence=info.Sequence,
        power=_POWER_BY_ENUM.get(info.Power),
        fw_major=info.FwMajor,
        fw_minor=info.FwMinor,
        raw_index=raw_index,
        source_ip=source_ip,
        received_at=dt_util.utcnow(),
    )
    return reading, None


def get_ignore_prefixes(hass: HomeAssistant, enabled: bool) -> frozenset[str]:
    """Set of co-installed emulator mac prefixes to filter (§2.10).

    Recomputed per packet: it reads one domain's entry list and the emulator
    cannot broadcast before its config entry (and thus its prefix) exists, so
    detection is race-free by causality. No caching / update listeners.
    """
    if not enabled:
        return frozenset()
    return frozenset(
        entry.data["mac_prefix"].lower()
        for entry in hass.config_entries.async_entries(EMITTER_DOMAIN)
        if entry.data.get("mac_prefix")
    )


class DeviceManager:
    """Owns the roster and turns readings into discovery/update dispatches."""

    def __init__(
        self,
        hass: HomeAssistant,
        storage: VenstarListenerStorage,
        roster: dict[str, DiscoveredDevice],
        counters: ListenerCounters,
    ) -> None:
        self._hass = hass
        self._storage = storage
        self.roster = roster
        self.counters = counters

    @callback
    def handle(self, reading: DecodedReading) -> None:
        """Process one decoded reading (called synchronously from the socket)."""
        device = self.roster.get(reading.mac)

        if device is None:
            self.roster[reading.mac] = DiscoveredDevice.from_reading(reading)
            self._save_immediate()
            _LOGGER.debug("Discovered new device %s (%s)", reading.mac, reading.name)
            async_dispatcher_send(self._hass, SIGNAL_NEW_DEVICE, reading)
            return

        # Dedup per mac on sequence: the 5x repeats and the C#-app "resend" reuse
        # the same sequence. Refresh last_seen so the sensor doesn't go stale, but
        # do not dispatch or save (§4).
        if reading.sequence == device.last_sequence:
            device.last_seen = reading.received_at
            self.counters.deduped += 1
            return

        old_name = device.name
        new_capability = device.apply_reading(reading)

        if new_capability:
            self._save_immediate()
            _LOGGER.debug(
                "New capability on %s (battery=%s humidity=%s)",
                reading.mac,
                device.has_battery,
                device.has_humidity,
            )
            # The platform's created-set makes this re-fire idempotent (§6d).
            async_dispatcher_send(self._hass, SIGNAL_NEW_DEVICE, reading)

        if reading.name != old_name:
            self._update_device_name(reading.mac, reading.name)

        async_dispatcher_send(self._hass, SIGNAL_UPDATE.format(reading.mac), reading)
        self._storage.async_delay_save(self.roster)

    @callback
    def _update_device_name(self, mac: str, name: str) -> None:
        """Follow a wire rename. async_update_device preserves name_by_user, so a
        user's manual rename in HA always wins (§2.8)."""
        registry = dr.async_get(self._hass)
        device_entry = registry.async_get_device(identifiers={(DOMAIN, mac)})
        if device_entry is not None:
            registry.async_update_device(device_entry.id, name=name)

    async def async_remove(self, mac: str) -> None:
        """Purge a mac from the roster and persist (device deletion, §6h)."""
        if self.roster.pop(mac, None) is not None:
            await self._storage.async_save(self.roster)

    async def async_save_final(self) -> None:
        """Immediate flush on unload so a pending debounced write can't race a
        freshly reloaded entry (§6f)."""
        await self._storage.async_save(self.roster)

    @callback
    def _save_immediate(self) -> None:
        # handle() runs in a sync socket callback, so schedule the coroutine.
        self._hass.async_create_task(self._storage.async_save(self.roster))


class VenstarListenerProtocol(asyncio.DatagramProtocol):
    """asyncio datagram protocol: decode → count → hand to the DeviceManager."""

    def __init__(
        self,
        device_manager: DeviceManager,
        counters: ListenerCounters,
        ignore_prefixes_provider: Callable[[], frozenset[str]],
    ) -> None:
        self._dm = device_manager
        self._counters = counters
        self._ignore_prefixes_provider = ignore_prefixes_provider

    def datagram_received(self, data: bytes, addr: tuple[str, int]) -> None:
        reading, drop = decode_packet(data, addr[0], self._ignore_prefixes_provider())
        if drop is not None:
            self._counters.record_drop(drop)
            return
        self._counters.parsed += 1
        _LOGGER.debug(
            "Decoded %s seq=%s temp=%s°C fault=%s purpose=%s from %s",
            reading.mac,
            reading.sequence,
            reading.temp_c,
            reading.fault,
            reading.purpose,
            reading.source_ip,
        )
        self._dm.handle(reading)

    def error_received(self, exc: Exception) -> None:
        # e.g. ICMP port-unreachable; nothing actionable for a broadcast listener.
        _LOGGER.debug("UDP error_received: %s", exc)


async def async_create_listener(
    hass: HomeAssistant,
    port: int,
    protocol_factory: Callable[[], VenstarListenerProtocol],
) -> tuple[asyncio.DatagramTransport, VenstarListenerProtocol]:
    """Bind the UDP socket and attach it to the event loop.

    Binding ``0.0.0.0`` receives both unicast and 255.255.255.255 broadcast to
    the port. SO_REUSEADDR lets a co-bound listener coexist (mitigation only).
    Raises OSError if the port is already held without SO_REUSEADDR — the caller
    turns that into ConfigEntryNotReady for retry-with-backoff.
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.setblocking(False)  # required when handing a pre-built socket to asyncio
        sock.bind((DEFAULT_BIND_ADDRESS, port))
    except OSError:
        sock.close()
        raise

    transport, protocol = await hass.loop.create_datagram_endpoint(
        protocol_factory, sock=sock
    )
    _LOGGER.debug("Listening for Venstar packets on %s:%d", DEFAULT_BIND_ADDRESS, port)
    return transport, protocol
