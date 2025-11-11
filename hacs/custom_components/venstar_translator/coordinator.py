"""Broadcast coordinator for Venstar Translator sensors."""
from __future__ import annotations

import asyncio
import logging
from datetime import datetime
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from homeassistant.core import HomeAssistant

from .const import (
    DEFAULT_INTERVAL,
    DOMAIN,
    OUTDOOR_INTERVAL,
    PURPOSE_OUTDOOR,
)
from .venstar_sensor import VenstarSensor, broadcast_udp_packet

_LOGGER = logging.getLogger(__name__)


class VenstarSensorCoordinator:
    """Manages broadcast scheduling and execution for a single sensor."""

    def __init__(
        self,
        hass: HomeAssistant,
        entry_id: str,
        sensor_id: int,
    ) -> None:
        """Initialize the coordinator.

        Args:
            hass: Home Assistant instance
            entry_id: Config entry ID
            sensor_id: Sensor ID (0-19)
        """
        self.hass = hass
        self.entry_id = entry_id
        self.sensor_id = sensor_id
        self._task: asyncio.Task | None = None
        self._stop_event = asyncio.Event()

    @property
    def _storage(self):
        """Get storage instance from hass.data."""
        return self.hass.data[DOMAIN][self.entry_id]["storage"]

    @property
    def _sensor_config(self) -> dict:
        """Get sensor configuration from storage."""
        return self._storage.get_sensor(self.sensor_id)

    async def start(self) -> None:
        """Start the broadcast scheduler."""
        if self._task is not None:
            _LOGGER.warning(f"Coordinator for sensor {self.sensor_id} already running")
            return

        sensor_config = self._sensor_config
        if not sensor_config:
            _LOGGER.error(f"Cannot start coordinator: sensor {self.sensor_id} not found")
            return

        if not sensor_config.get("enabled", True):
            _LOGGER.debug(f"Sensor {self.sensor_id} is disabled, not starting coordinator")
            return

        # Determine broadcast interval based on sensor purpose
        interval = (
            OUTDOOR_INTERVAL
            if sensor_config["purpose"] == PURPOSE_OUTDOOR
            else DEFAULT_INTERVAL
        )

        _LOGGER.info(
            f"Starting coordinator for sensor {self.sensor_id} "
            f"({sensor_config['name']}), interval={interval}s"
        )

        self._stop_event.clear()
        self._task = asyncio.create_task(self._broadcast_loop(interval))

    async def stop(self) -> None:
        """Stop the broadcast scheduler."""
        if self._task is None:
            return

        _LOGGER.info(f"Stopping coordinator for sensor {self.sensor_id}")

        self._stop_event.set()
        self._task.cancel()
        try:
            await self._task
        except asyncio.CancelledError:
            pass
        self._task = None

    async def _broadcast_loop(self, interval: int) -> None:
        """Main broadcast loop.

        Args:
            interval: Broadcast interval in seconds
        """
        sensor_config = self._sensor_config

        while not self._stop_event.is_set():
            try:
                # Get current temperature from HA entity
                temperature = await self._get_current_temperature()

                if temperature is not None:
                    await self._broadcast_sensor(temperature)
                else:
                    _LOGGER.warning(
                        f"Sensor {self.sensor_id} ({sensor_config['name']}): "
                        f"temperature unavailable from entity {sensor_config['entity_id']}"
                    )

            except Exception as e:
                _LOGGER.error(
                    f"Error broadcasting sensor {self.sensor_id} "
                    f"({sensor_config['name']}): {e}",
                    exc_info=True
                )

            # Wait for next broadcast interval
            try:
                await asyncio.wait_for(
                    self._stop_event.wait(),
                    timeout=interval
                )
            except asyncio.TimeoutError:
                # Timeout is expected - continue loop
                pass

    async def _get_current_temperature(self) -> float | None:
        """Get current temperature from HA entity.

        Returns:
            Temperature value, or None if unavailable
        """
        sensor_config = self._sensor_config
        state = self.hass.states.get(sensor_config["entity_id"])

        if state is None:
            _LOGGER.debug(f"Entity {sensor_config['entity_id']} not found")
            return None

        if state.state in ("unknown", "unavailable"):
            _LOGGER.debug(
                f"Entity {sensor_config['entity_id']} state is {state.state}"
            )
            return None

        try:
            return float(state.state)
        except (ValueError, TypeError) as e:
            _LOGGER.error(
                f"Invalid temperature value from {sensor_config['entity_id']}: "
                f"{state.state} - {e}"
            )
            return None

    async def _broadcast_sensor(self, temperature: float) -> None:
        """Build packet and broadcast via UDP.

        Args:
            temperature: Current temperature reading
        """
        sensor_config = self._sensor_config
        storage = self._storage

        # Create sensor instance
        sensor = VenstarSensor(
            sensor_id=self.sensor_id,
            mac_prefix=storage.mac_prefix,
            name=sensor_config["name"],
            purpose=sensor_config["purpose"],
            scale=sensor_config.get("scale", "F"),
            sequence=sensor_config["sequence"],
        )

        # Build packet
        packet = sensor.build_data_packet(temperature)

        # Broadcast UDP (run in executor to avoid blocking)
        await self.hass.async_add_executor_job(broadcast_udp_packet, packet)

        # Update sequence number in storage
        storage.update_sequence(self.sensor_id, sensor.sequence)
        await storage.async_save()

        _LOGGER.debug(
            f"Broadcast sensor {self.sensor_id} ({sensor_config['name']}): "
            f"{temperature}°{sensor_config['scale']} "
            f"(seq={sensor.sequence-1})"
        )

    async def trigger_broadcast(self) -> None:
        """Manually trigger a broadcast immediately (for testing/pairing).

        This doesn't wait for the scheduled interval.
        """
        sensor_config = self._sensor_config
        if not sensor_config:
            _LOGGER.error(f"Cannot broadcast: sensor {self.sensor_id} not found")
            return

        temperature = await self._get_current_temperature()
        if temperature is not None:
            await self._broadcast_sensor(temperature)
        else:
            _LOGGER.warning(
                f"Cannot broadcast sensor {self.sensor_id}: temperature unavailable"
            )
