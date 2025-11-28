"""The Venstar Translator integration."""
from __future__ import annotations

import logging

from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant

from .const import DOMAIN
from .coordinator import VenstarSensorCoordinator
from .storage import VenstarTranslatorStorage
from .venstar_sensor import VenstarSensor, broadcast_udp_packet

_LOGGER = logging.getLogger(__name__)


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Set up Venstar Translator from a config entry."""
    _LOGGER.info("Setting up Venstar Translator integration")

    # Initialize storage
    storage = VenstarTranslatorStorage(hass)
    await storage.async_load()

    # Store in hass.data
    hass.data.setdefault(DOMAIN, {})
    hass.data[DOMAIN][entry.entry_id] = {
        "storage": storage,
        "coordinators": {},
    }

    # Initialize coordinators for each enabled sensor
    coordinators = {}
    for sensor_id_str, sensor_config in storage.sensors.items():
        if sensor_config.get("enabled", True):
            sensor_id = int(sensor_id_str)
            coordinator = VenstarSensorCoordinator(hass, entry.entry_id, sensor_id)
            await coordinator.start()
            coordinators[sensor_id] = coordinator

    hass.data[DOMAIN][entry.entry_id]["coordinators"] = coordinators

    # Register pair_sensor service
    async def handle_pair_sensor(call):
        """Handle the pair_sensor service call."""
        sensor_id = call.data.get("sensor_id")

        if str(sensor_id) not in storage.sensors:
            _LOGGER.error(f"Sensor {sensor_id} not configured")
            return

        sensor_config = storage.sensors[str(sensor_id)]

        # Get current temperature
        state = hass.states.get(sensor_config["entity_id"])
        if not state or state.state in ("unknown", "unavailable"):
            _LOGGER.error(
                f"Cannot pair sensor {sensor_id}: temperature unavailable from "
                f"entity {sensor_config['entity_id']}"
            )
            return

        try:
            temperature = float(state.state)

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

            _LOGGER.info(
                f"Pairing packet sent for sensor {sensor_id} ({sensor_config['name']})"
            )

        except Exception as e:
            _LOGGER.error(f"Failed to send pairing packet for sensor {sensor_id}: {e}")

    hass.services.async_register(DOMAIN, "pair_sensor", handle_pair_sensor)

    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload a config entry."""
    _LOGGER.info("Unloading Venstar Translator integration")

    # Stop all coordinators
    data = hass.data[DOMAIN][entry.entry_id]
    for coordinator in data.get("coordinators", {}).values():
        await coordinator.stop()

    # Clean up
    hass.data[DOMAIN].pop(entry.entry_id)

    return True
