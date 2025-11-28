"""Storage management for Venstar Translator."""
from __future__ import annotations

import logging
import secrets
from typing import Any

from homeassistant.core import HomeAssistant
from homeassistant.helpers.storage import Store

from .const import (
    MAX_SENSORS,
    STORAGE_KEY,
    STORAGE_VERSION,
)

_LOGGER = logging.getLogger(__name__)


class VenstarTranslatorStorage:
    """Manage persistent storage for Venstar Translator."""

    def __init__(self, hass: HomeAssistant) -> None:
        """Initialize storage manager.

        Args:
            hass: Home Assistant instance
        """
        self.hass = hass
        self._store = Store(hass, STORAGE_VERSION, STORAGE_KEY)
        self.mac_prefix: str | None = None
        self.sensors: dict[str, dict[str, Any]] = {}

    async def async_load(self) -> None:
        """Load data from storage."""
        data = await self._store.async_load()

        if data is None:
            _LOGGER.info("No existing storage found, initializing new storage")
            # Generate random MAC prefix on first load
            self.mac_prefix = self._generate_mac_prefix()
            self.sensors = {}
            await self.async_save()
        else:
            self.mac_prefix = data.get("mac_prefix")
            self.sensors = data.get("sensors", {})
            _LOGGER.info(
                f"Loaded storage: MAC prefix={self.mac_prefix}, "
                f"{len(self.sensors)} sensors configured"
            )

    async def async_save(self) -> None:
        """Save data to storage."""
        data = {
            "mac_prefix": self.mac_prefix,
            "sensors": self.sensors,
        }
        await self._store.async_save(data)
        _LOGGER.debug(f"Saved storage: {len(self.sensors)} sensors")

    def get_next_sensor_id(self) -> int | None:
        """Find the next available sensor ID (0-19).

        Returns:
            Next available sensor ID, or None if all 20 slots are full
        """
        used_ids = {int(sensor_id) for sensor_id in self.sensors.keys()}
        for sensor_id in range(MAX_SENSORS):
            if sensor_id not in used_ids:
                return sensor_id
        return None

    def add_sensor(
        self,
        entity_id: str,
        name: str,
        purpose: str,
        scale: str = "F",
        enabled: bool = True,
    ) -> int:
        """Add a new sensor configuration.

        Args:
            entity_id: Home Assistant entity ID to monitor
            name: Sensor name (max 14 characters)
            purpose: Sensor purpose (Outdoor, Remote, Return, Supply)
            scale: Temperature scale (F or C)
            enabled: Whether sensor broadcasts are enabled

        Returns:
            Assigned sensor ID

        Raises:
            ValueError: If no sensor IDs available or name already exists
        """
        # Check if name already exists
        for sensor_config in self.sensors.values():
            if sensor_config["name"] == name:
                raise ValueError(f"Sensor name '{name}' already exists")

        # Get next available ID
        sensor_id = self.get_next_sensor_id()
        if sensor_id is None:
            raise ValueError(f"Cannot add sensor: maximum {MAX_SENSORS} sensors reached")

        # Add sensor configuration
        self.sensors[str(sensor_id)] = {
            "entity_id": entity_id,
            "name": name,
            "purpose": purpose,
            "scale": scale,
            "enabled": enabled,
            "sequence": 1,  # Initial sequence number
        }

        _LOGGER.info(f"Added sensor {sensor_id}: {name} ({purpose})")
        return sensor_id

    def update_sensor(
        self,
        sensor_id: int,
        entity_id: str | None = None,
        name: str | None = None,
        purpose: str | None = None,
        scale: str | None = None,
        enabled: bool | None = None,
    ) -> None:
        """Update an existing sensor configuration.

        Args:
            sensor_id: Sensor ID to update
            entity_id: New entity ID (optional)
            name: New name (optional)
            purpose: New purpose (optional)
            scale: New scale (optional)
            enabled: New enabled state (optional)

        Raises:
            ValueError: If sensor ID doesn't exist or new name conflicts
        """
        sensor_id_str = str(sensor_id)
        if sensor_id_str not in self.sensors:
            raise ValueError(f"Sensor {sensor_id} does not exist")

        # Check for name conflicts
        if name is not None:
            for sid, config in self.sensors.items():
                if sid != sensor_id_str and config["name"] == name:
                    raise ValueError(f"Sensor name '{name}' already exists")

        # Update fields
        sensor_config = self.sensors[sensor_id_str]
        if entity_id is not None:
            sensor_config["entity_id"] = entity_id
        if name is not None:
            sensor_config["name"] = name
        if purpose is not None:
            sensor_config["purpose"] = purpose
        if scale is not None:
            sensor_config["scale"] = scale
        if enabled is not None:
            sensor_config["enabled"] = enabled

        _LOGGER.info(f"Updated sensor {sensor_id}: {sensor_config['name']}")

    def delete_sensor(self, sensor_id: int) -> None:
        """Delete a sensor configuration.

        Args:
            sensor_id: Sensor ID to delete

        Raises:
            ValueError: If sensor ID doesn't exist
        """
        sensor_id_str = str(sensor_id)
        if sensor_id_str not in self.sensors:
            raise ValueError(f"Sensor {sensor_id} does not exist")

        name = self.sensors[sensor_id_str]["name"]
        del self.sensors[sensor_id_str]
        _LOGGER.info(f"Deleted sensor {sensor_id}: {name}")

    def get_sensor(self, sensor_id: int) -> dict[str, Any] | None:
        """Get sensor configuration by ID.

        Args:
            sensor_id: Sensor ID

        Returns:
            Sensor configuration dict, or None if not found
        """
        return self.sensors.get(str(sensor_id))

    def update_sequence(self, sensor_id: int, sequence: int) -> None:
        """Update sequence number for a sensor.

        Args:
            sensor_id: Sensor ID
            sequence: New sequence number

        Raises:
            ValueError: If sensor ID doesn't exist
        """
        sensor_id_str = str(sensor_id)
        if sensor_id_str not in self.sensors:
            raise ValueError(f"Sensor {sensor_id} does not exist")

        self.sensors[sensor_id_str]["sequence"] = sequence

    @staticmethod
    def _generate_mac_prefix() -> str:
        """Generate random 10-character hex MAC prefix.

        Returns:
            10-character hex string (e.g., "428e0486d8")
        """
        return secrets.token_hex(5)  # 5 bytes = 10 hex chars
