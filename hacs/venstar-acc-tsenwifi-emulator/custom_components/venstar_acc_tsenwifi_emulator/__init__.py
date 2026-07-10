"""The Venstar ACC-TSENWIFI Emulator integration."""
from __future__ import annotations

from dataclasses import dataclass, field
import logging

import voluptuous as vol

from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant, ServiceCall

from .const import DOMAIN, MAX_SENSORS
from .coordinator import VenstarSensorCoordinator, extract_temperature
from .storage import VenstarEmulatorStorage
from .venstar_sensor import VenstarSensor, broadcast_udp_packet

_LOGGER = logging.getLogger(__name__)

SERVICE_PAIR_SENSOR = "pair_sensor"
SERVICE_RESEND_LAST_PACKET = "resend_last_packet"

SERVICE_SCHEMA = vol.Schema(
    {
        vol.Required("sensor_id"): vol.All(
            vol.Coerce(int), vol.Range(min=0, max=MAX_SENSORS - 1)
        ),
    }
)


@dataclass
class VenstarRuntimeData:
    """Runtime data stored on the Venstar ACC-TSENWIFI Emulator config entry."""

    storage: VenstarEmulatorStorage
    coordinators: dict[int, VenstarSensorCoordinator] = field(default_factory=dict)


type VenstarConfigEntry = ConfigEntry[VenstarRuntimeData]


async def async_setup_entry(hass: HomeAssistant, entry: VenstarConfigEntry) -> bool:
    """Set up Venstar ACC-TSENWIFI Emulator from a config entry."""
    _LOGGER.info("Setting up Venstar ACC-TSENWIFI Emulator integration")

    # Initialize storage, passing MAC prefix from config entry for first-load sync
    storage = VenstarEmulatorStorage(hass)
    await storage.async_load(mac_prefix=entry.data.get("mac_prefix"))

    entry.runtime_data = VenstarRuntimeData(storage=storage)

    # Initialize coordinators for each enabled sensor
    for sensor_id_str, sensor_config in storage.sensors.items():
        if sensor_config.get("enabled", True):
            sensor_id = int(sensor_id_str)
            coordinator = VenstarSensorCoordinator(hass, storage, sensor_id)
            await coordinator.start()
            entry.runtime_data.coordinators[sensor_id] = coordinator

    # Register pair_sensor service
    async def handle_pair_sensor(call: ServiceCall) -> None:
        """Handle the pair_sensor service call."""
        sensor_id = call.data["sensor_id"]

        if str(sensor_id) not in storage.sensors:
            _LOGGER.error(f"Sensor {sensor_id} not configured")
            return

        sensor_config = storage.sensors[str(sensor_id)]

        # Get current temperature
        temperature = extract_temperature(hass.states.get(sensor_config["entity_id"]))
        if temperature is None:
            _LOGGER.error(
                f"Cannot pair sensor {sensor_id}: temperature unavailable from "
                f"entity {sensor_config['entity_id']}"
            )
            return

        try:
            # Create sensor instance
            sensor = VenstarSensor(
                sensor_id=sensor_id,
                mac_prefix=storage.mac_prefix,
                name=sensor_config["name"],
                purpose=sensor_config["purpose"],
                scale=sensor_config.get("scale", "F"),
                sequence=1,
            )

            # Build and broadcast pairing packet
            packet = sensor.build_pairing_packet(temperature)
            await hass.async_add_executor_job(broadcast_udp_packet, packet)

            # Reset stored sequence to 1 after pairing (matches C# behavior)
            storage.update_sequence(sensor_id, 1)
            await storage.async_save()

            _LOGGER.info(
                f"Pairing packet sent for sensor {sensor_id} ({sensor_config['name']})"
            )

        except Exception as e:
            _LOGGER.error(f"Failed to send pairing packet for sensor {sensor_id}: {e}")

    hass.services.async_register(
        DOMAIN, SERVICE_PAIR_SENSOR, handle_pair_sensor, schema=SERVICE_SCHEMA
    )

    # Register resend_last_packet service
    async def handle_resend_last_packet(call: ServiceCall) -> None:
        """Handle the resend_last_packet service call."""
        sensor_id = call.data["sensor_id"]

        if str(sensor_id) not in storage.sensors:
            _LOGGER.error(f"Sensor {sensor_id} not configured")
            return

        packet = storage.get_last_packet(sensor_id)
        if packet is None:
            _LOGGER.error(
                f"Sensor {sensor_id}: no cached packet to resend "
                f"(sensor has never broadcast)"
            )
            return

        try:
            await hass.async_add_executor_job(broadcast_udp_packet, packet)
            _LOGGER.info(
                f"Resent last packet for sensor {sensor_id} "
                f"({storage.sensors[str(sensor_id)]['name']}), "
                f"{len(packet)} bytes"
            )
        except Exception as e:
            _LOGGER.error(f"Failed to resend packet for sensor {sensor_id}: {e}")

    hass.services.async_register(
        DOMAIN, SERVICE_RESEND_LAST_PACKET, handle_resend_last_packet, schema=SERVICE_SCHEMA
    )

    return True


async def async_unload_entry(hass: HomeAssistant, entry: VenstarConfigEntry) -> bool:
    """Unload a config entry."""
    _LOGGER.info("Unloading Venstar ACC-TSENWIFI Emulator integration")

    # Stop all coordinators
    for coordinator in entry.runtime_data.coordinators.values():
        await coordinator.stop()
    entry.runtime_data.coordinators.clear()

    # Remove services so stale handlers can't touch unloaded storage
    hass.services.async_remove(DOMAIN, SERVICE_PAIR_SENSOR)
    hass.services.async_remove(DOMAIN, SERVICE_RESEND_LAST_PACKET)

    return True
