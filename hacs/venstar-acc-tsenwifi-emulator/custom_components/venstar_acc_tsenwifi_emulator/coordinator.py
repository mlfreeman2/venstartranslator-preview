"""Broadcast coordinator for Venstar ACC-TSENWIFI Emulator sensors."""
from __future__ import annotations

import asyncio
import logging
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from homeassistant.core import HomeAssistant, State

    from .storage import VenstarEmulatorStorage

from .const import (
    DEFAULT_INTERVAL,
    OUTDOOR_INTERVAL,
    PURPOSE_OUTDOOR,
)
from .venstar_sensor import VenstarSensor, broadcast_udp_packet

_LOGGER = logging.getLogger(__name__)


def extract_temperature(state: State | None) -> float | None:
    """Extract a temperature reading from an HA state object.

    Climate entities report the measured temperature in their
    current_temperature attribute (their state is the HVAC mode, e.g. "heat");
    everything else uses the state value itself.

    Returns None if the entity is missing/unavailable or non-numeric.
    """
    if state is None or state.state in ("unknown", "unavailable"):
        return None
    if state.domain == "climate":
        value = state.attributes.get("current_temperature")
    else:
        value = state.state
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


class VenstarSensorCoordinator:
    """Manages broadcast scheduling and execution for a single sensor."""

    def __init__(
        self,
        hass: HomeAssistant,
        storage: VenstarEmulatorStorage,
        sensor_id: int,
    ) -> None:
        """Initialize the coordinator.

        Args:
            hass: Home Assistant instance
            storage: Shared storage instance for this config entry
            sensor_id: Sensor ID (0-19)
        """
        self.hass = hass
        self._storage = storage
        self.sensor_id = sensor_id
        self._task: asyncio.Task | None = None
        self._stop_event = asyncio.Event()

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
        # HA-tracked background task: named in diagnostics and cancelled
        # automatically at shutdown (bare asyncio.create_task is neither)
        self._task = self.hass.async_create_background_task(
            self._broadcast_loop(interval),
            name=f"venstar_acc_tsenwifi_emulator broadcast loop sensor {self.sensor_id}",
        )

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

        temperature = extract_temperature(state)
        if temperature is None:
            _LOGGER.error(
                f"No numeric temperature available from {sensor_config['entity_id']} "
                f"(state: {state.state})"
            )
        return temperature

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

        # Update sequence number and cache packet in storage
        # (debounced write - this runs once per sensor per broadcast interval)
        storage.update_sequence(self.sensor_id, sensor.sequence)
        storage.update_last_packet(self.sensor_id, packet)
        await storage.async_save(immediate=False)

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
