"""Sensor platform: push-discovered temperature/battery/humidity entities.

Devices and per-device capabilities are discovered from packets — see
``DeviceManager`` in ``listener.py``. This module subscribes to the discovery
signals, creates one HA device per mac, and keeps each entity in sync.
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from homeassistant.components.sensor import (
    RestoreSensor,
    SensorDeviceClass,
    SensorStateClass,
)
from homeassistant.const import PERCENTAGE, EntityCategory, UnitOfTemperature
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.device_registry import DeviceInfo
from homeassistant.helpers.dispatcher import async_dispatcher_connect
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.util import dt as dt_util

from .const import (
    DOMAIN,
    MANUFACTURER,
    MODEL,
    SIGNAL_AVAILABILITY,
    SIGNAL_NEW_DEVICE,
    SIGNAL_UPDATE,
)

if TYPE_CHECKING:
    from . import VenstarListenerConfigEntry
    from .listener import DecodedReading, DeviceManager


async def async_setup_entry(
    hass: HomeAssistant,
    entry: VenstarListenerConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up the sensor platform and wire up dynamic discovery."""
    manager = entry.runtime_data.device_manager
    # (mac, kind) tuples already materialized — dedupes roster-restore vs. live
    # discovery, makes capability re-fires idempotent, and lets a late-appearing
    # humidity entity be added without touching the others (§6d).
    created: set[tuple[str, str]] = set()

    @callback
    def _add_entities_for(mac: str) -> None:
        device = manager.roster.get(mac)
        if device is None:
            return
        new_entities: list[VenstarBaseSensor] = []
        if (mac, "temperature") not in created:
            created.add((mac, "temperature"))
            new_entities.append(VenstarTemperatureSensor(manager, mac))
        if device.has_battery and (mac, "battery") not in created:
            created.add((mac, "battery"))
            new_entities.append(VenstarBatterySensor(manager, mac))
        if device.has_humidity and (mac, "humidity") not in created:
            created.add((mac, "humidity"))
            new_entities.append(VenstarHumiditySensor(manager, mac))
        if new_entities:
            async_add_entities(new_entities)

    @callback
    def _on_new_device(reading: DecodedReading) -> None:
        _add_entities_for(reading.mac)

    # Subscribe BEFORE scanning the restored roster so no discovery falls between
    # the cracks (combined with starting the socket after platform setup, §6h).
    entry.async_on_unload(
        async_dispatcher_connect(hass, SIGNAL_NEW_DEVICE, _on_new_device)
    )
    for mac in list(manager.roster):
        _add_entities_for(mac)


class VenstarBaseSensor(RestoreSensor):
    """Shared behavior: device binding, restore, availability, update handling."""

    _attr_has_entity_name = True
    _attr_should_poll = False
    _attr_state_class = SensorStateClass.MEASUREMENT
    _kind: str

    def __init__(self, manager: DeviceManager, mac: str) -> None:
        self._manager = manager
        self._mac = mac
        self._attr_unique_id = f"{mac}_{self._kind}"
        self._restored_native_value: float | int | None = None
        self._last_available_written: bool | None = None

    @property
    def _device(self):
        return self._manager.roster.get(self._mac)

    def _live_value(self) -> float | int | None:
        """Current value from the device's live state (overridden per kind)."""
        raise NotImplementedError

    @property
    def native_value(self) -> float | int | None:
        device = self._device
        # Before the first packet this session (e.g. straight after a restart),
        # show the RestoreSensor value; once a packet lands, trust live state —
        # including None on a fault sentinel (§6c).
        if device is None or not device.has_live_reading:
            return self._restored_native_value
        return self._live_value()

    @property
    def device_info(self) -> DeviceInfo:
        device = self._device
        name = device.name if device else f"Venstar {self._mac[-4:]}"
        return DeviceInfo(
            identifiers={(DOMAIN, self._mac)},
            name=name,
            manufacturer=MANUFACTURER,
            model=MODEL,
        )

    @property
    def available(self) -> bool:
        device = self._device
        if device is None:
            return False
        return not device.is_stale(dt_util.utcnow())

    async def async_added_to_hass(self) -> None:
        await super().async_added_to_hass()
        # RestoreSensor gives back the native value + native unit (survives unit
        # conversion, unlike RestoreEntity's display-state string).
        last = await self.async_get_last_sensor_data()
        if last is not None:
            self._restored_native_value = last.native_value
        self._last_available_written = self.available
        self.async_on_remove(
            async_dispatcher_connect(
                self.hass, SIGNAL_UPDATE.format(self._mac), self._handle_update
            )
        )
        self.async_on_remove(
            async_dispatcher_connect(
                self.hass, SIGNAL_AVAILABILITY, self._handle_availability_tick
            )
        )

    @callback
    def _handle_update(self, reading: DecodedReading) -> None:
        # A fresh packet just landed, so availability is (re)established here.
        self._last_available_written = self.available
        self.async_write_ha_state()

    @callback
    def _handle_availability_tick(self) -> None:
        # The time-based `available` property never re-renders on its own; this
        # sweep is what flips a stale sensor to unavailable. Only write on a
        # change so a steady sensor isn't re-rendered every minute (§6e).
        current = self.available
        if current != self._last_available_written:
            self._last_available_written = current
            self.async_write_ha_state()


class VenstarTemperatureSensor(VenstarBaseSensor):
    """Always present; native °C — exactly what the thermostat sees."""

    _kind = "temperature"
    _attr_device_class = SensorDeviceClass.TEMPERATURE
    _attr_native_unit_of_measurement = UnitOfTemperature.CELSIUS

    def _live_value(self) -> float | None:
        device = self._device
        return device.temp_c if device else None

    @property
    def extra_state_attributes(self) -> dict[str, object] | None:
        device = self._device
        if device is None:
            return None
        # Only stable fields — sequence/last_seen are excluded on purpose so a
        # steady temperature never forces a recorder row (§6d); they live in
        # diagnostics instead.
        attrs: dict[str, object] = {
            "sensor_id": device.sensor_id,
            "purpose": device.purpose,
            "power_source": device.power,
            "firmware": f"{device.fw_major}.{device.fw_minor}",
            "source_ip": device.source_ip,
            "raw_index": device.raw_index,
        }
        if device.fault is not None:
            attrs["fault"] = device.fault
        return attrs


class VenstarBatterySensor(VenstarBaseSensor):
    """Created on the first packet that carries a Battery field (§2.3)."""

    _kind = "battery"
    _attr_device_class = SensorDeviceClass.BATTERY
    _attr_native_unit_of_measurement = PERCENTAGE
    _attr_entity_category = EntityCategory.DIAGNOSTIC

    def _live_value(self) -> int | None:
        device = self._device
        return device.battery if device else None


class VenstarHumiditySensor(VenstarBaseSensor):
    """Created on the first packet that carries a Humidity field (§2.3)."""

    _kind = "humidity"
    _attr_device_class = SensorDeviceClass.HUMIDITY
    _attr_native_unit_of_measurement = PERCENTAGE

    def _live_value(self) -> int | None:
        device = self._device
        return device.humidity if device else None
