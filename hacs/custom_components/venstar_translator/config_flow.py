"""Config flow for Venstar Translator integration."""
from __future__ import annotations

import logging
import secrets
from typing import Any

import voluptuous as vol

from homeassistant.config_entries import ConfigEntry, ConfigFlow, ConfigFlowResult, OptionsFlow
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers import selector

from .const import (
    DOMAIN,
    MAX_NAME_LENGTH,
    MAX_SENSORS,
    VALID_PURPOSES,
    VALID_SCALES,
)
from .coordinator import VenstarSensorCoordinator
from .venstar_sensor import VenstarSensor, broadcast_udp_packet

_LOGGER = logging.getLogger(__name__)


class VenstarTranslatorConfigFlow(ConfigFlow, domain=DOMAIN):
    """Handle a config flow for Venstar Translator."""

    VERSION = 1

    async def async_step_user(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Handle the initial step."""
        if self._async_current_entries():
            return self.async_abort(reason="single_instance_allowed")

        if user_input is not None:
            # Generate random MAC prefix on first setup
            mac_prefix = self._generate_random_mac_prefix()

            return self.async_create_entry(
                title="Venstar Translator",
                data={"mac_prefix": mac_prefix}
            )

        return self.async_show_form(
            step_id="user",
            description_placeholders={
                "description": (
                    "This will create a Venstar Translator instance. "
                    "You can configure up to 20 sensors after setup."
                )
            }
        )

    @staticmethod
    def _generate_random_mac_prefix() -> str:
        """Generate random 10-character hex MAC prefix."""
        return secrets.token_hex(5)  # 10 hex chars

    @staticmethod
    @callback
    def async_get_options_flow(
        config_entry: ConfigEntry,
    ) -> OptionsFlow:
        """Get the options flow for this handler."""
        return VenstarTranslatorOptionsFlow(config_entry)


class VenstarTranslatorOptionsFlow(OptionsFlow):
    """Handle options flow for Venstar Translator."""

    def __init__(self, config_entry: ConfigEntry) -> None:
        """Initialize options flow."""
        self.config_entry = config_entry
        self._sensor_to_edit: int | None = None
        self._sensor_to_delete: int | None = None

    @property
    def _storage(self):
        """Get storage instance."""
        return self.hass.data[DOMAIN][self.config_entry.entry_id]["storage"]

    async def async_step_init(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Manage the options."""
        return await self.async_step_sensor_list()

    async def async_step_sensor_list(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Show list of configured sensors with management options."""
        storage = self._storage

        if user_input is not None:
            action = user_input.get("action")

            if action == "done":
                # User finished configuration - pair all sensors
                return await self.async_step_pair_all_sensors()
            elif action == "add_sensor":
                return await self.async_step_add_sensor()
            elif action == "edit_sensor":
                return await self.async_step_select_sensor_to_edit()
            elif action == "delete_sensor":
                return await self.async_step_select_sensor_to_delete()

        # Build sensor list description
        sensor_count = len(storage.sensors)
        if sensor_count == 0:
            description = "No sensors configured yet. Click 'Add Sensor' to get started."
        else:
            sensor_lines = []
            for sensor_id, config in sorted(storage.sensors.items(), key=lambda x: int(x[0])):
                status = "✓ Enabled" if config.get("enabled", True) else "✗ Disabled"
                sensor_lines.append(
                    f"  [{sensor_id}] {config['name']} - {config['purpose']} - {status}"
                )
            sensor_list = "\n".join(sensor_lines)
            description = f"Configured sensors ({sensor_count}/{MAX_SENSORS}):\n\n{sensor_list}"

        menu_options = ["add_sensor"]
        if sensor_count > 0:
            menu_options.extend(["edit_sensor", "delete_sensor"])
        menu_options.append("done")

        return self.async_show_menu(
            step_id="sensor_list",
            menu_options=menu_options,
            description_placeholders={"sensors": description}
        )

    async def async_step_add_sensor(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Add a new sensor."""
        errors = {}
        storage = self._storage

        if user_input is not None:
            try:
                # Validate sensor name length
                name = user_input["name"].strip()
                if len(name) > MAX_NAME_LENGTH:
                    errors["name"] = "name_too_long"
                elif not name:
                    errors["name"] = "name_required"
                else:
                    # Add sensor to storage
                    sensor_id = storage.add_sensor(
                        entity_id=user_input["entity_id"],
                        name=name,
                        purpose=user_input["purpose"],
                        scale=user_input.get("scale", "F"),
                        enabled=user_input.get("enabled", True),
                    )
                    await storage.async_save()

                    # Start coordinator for this sensor if enabled
                    if user_input.get("enabled", True):
                        coordinator = VenstarSensorCoordinator(
                            self.hass, self.config_entry.entry_id, sensor_id
                        )
                        await coordinator.start()
                        self.hass.data[DOMAIN][self.config_entry.entry_id]["coordinators"][
                            sensor_id
                        ] = coordinator

                    _LOGGER.info(f"Added sensor {sensor_id}: {name}")
                    return await self.async_step_sensor_list()

            except ValueError as e:
                if "already exists" in str(e):
                    errors["name"] = "name_duplicate"
                elif "maximum" in str(e):
                    errors["base"] = "max_sensors_reached"
                else:
                    errors["base"] = "unknown"
                    _LOGGER.error(f"Error adding sensor: {e}")

        # Check if we can add more sensors
        if storage.get_next_sensor_id() is None:
            return self.async_abort(reason="max_sensors_reached")

        return self.async_show_form(
            step_id="add_sensor",
            data_schema=vol.Schema({
                vol.Required("entity_id"): selector.EntitySelector(
                    selector.EntitySelectorConfig(domain=["sensor", "climate"])
                ),
                vol.Required("name"): str,
                vol.Required("purpose"): selector.SelectSelector(
                    selector.SelectSelectorConfig(
                        options=VALID_PURPOSES,
                        mode=selector.SelectSelectorMode.DROPDOWN
                    )
                ),
                vol.Optional("scale", default="F"): selector.SelectSelector(
                    selector.SelectSelectorConfig(
                        options=VALID_SCALES,
                        mode=selector.SelectSelectorMode.DROPDOWN
                    )
                ),
                vol.Optional("enabled", default=True): bool,
            }),
            errors=errors,
        )

    async def async_step_select_sensor_to_edit(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Select which sensor to edit."""
        storage = self._storage

        if user_input is not None:
            self._sensor_to_edit = int(user_input["sensor_id"])
            return await self.async_step_edit_sensor()

        if not storage.sensors:
            return await self.async_step_sensor_list()

        # Build sensor selection options
        sensor_options = [
            {"label": f"[{sid}] {config['name']}", "value": sid}
            for sid, config in sorted(storage.sensors.items(), key=lambda x: int(x[0]))
        ]

        return self.async_show_form(
            step_id="select_sensor_to_edit",
            data_schema=vol.Schema({
                vol.Required("sensor_id"): selector.SelectSelector(
                    selector.SelectSelectorConfig(
                        options=sensor_options,
                        mode=selector.SelectSelectorMode.DROPDOWN
                    )
                ),
            }),
        )

    async def async_step_edit_sensor(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Edit an existing sensor."""
        errors = {}
        storage = self._storage
        sensor_id = self._sensor_to_edit
        sensor_config = storage.get_sensor(sensor_id)

        if sensor_config is None:
            return await self.async_step_sensor_list()

        if user_input is not None:
            try:
                # Validate name length
                name = user_input["name"].strip()
                if len(name) > MAX_NAME_LENGTH:
                    errors["name"] = "name_too_long"
                elif not name:
                    errors["name"] = "name_required"
                else:
                    # Update sensor
                    old_enabled = sensor_config.get("enabled", True)
                    new_enabled = user_input.get("enabled", True)

                    storage.update_sensor(
                        sensor_id=sensor_id,
                        entity_id=user_input["entity_id"],
                        name=name,
                        purpose=user_input["purpose"],
                        scale=user_input.get("scale", "F"),
                        enabled=new_enabled,
                    )
                    await storage.async_save()

                    # Handle coordinator lifecycle
                    coordinators = self.hass.data[DOMAIN][self.config_entry.entry_id][
                        "coordinators"
                    ]

                    if old_enabled and not new_enabled:
                        # Stop coordinator
                        if sensor_id in coordinators:
                            await coordinators[sensor_id].stop()
                            del coordinators[sensor_id]
                    elif not old_enabled and new_enabled:
                        # Start coordinator
                        coordinator = VenstarSensorCoordinator(
                            self.hass, self.config_entry.entry_id, sensor_id
                        )
                        await coordinator.start()
                        coordinators[sensor_id] = coordinator

                    _LOGGER.info(f"Updated sensor {sensor_id}: {name}")
                    return await self.async_step_sensor_list()

            except ValueError as e:
                if "already exists" in str(e):
                    errors["name"] = "name_duplicate"
                else:
                    errors["base"] = "unknown"
                    _LOGGER.error(f"Error updating sensor: {e}")

        return self.async_show_form(
            step_id="edit_sensor",
            data_schema=vol.Schema({
                vol.Required("entity_id", default=sensor_config["entity_id"]): selector.EntitySelector(
                    selector.EntitySelectorConfig(domain=["sensor", "climate"])
                ),
                vol.Required("name", default=sensor_config["name"]): str,
                vol.Required("purpose", default=sensor_config["purpose"]): selector.SelectSelector(
                    selector.SelectSelectorConfig(
                        options=VALID_PURPOSES,
                        mode=selector.SelectSelectorMode.DROPDOWN
                    )
                ),
                vol.Optional("scale", default=sensor_config.get("scale", "F")): selector.SelectSelector(
                    selector.SelectSelectorConfig(
                        options=VALID_SCALES,
                        mode=selector.SelectSelectorMode.DROPDOWN
                    )
                ),
                vol.Optional("enabled", default=sensor_config.get("enabled", True)): bool,
            }),
            errors=errors,
            description_placeholders={"sensor_id": str(sensor_id)}
        )

    async def async_step_select_sensor_to_delete(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Select which sensor to delete."""
        storage = self._storage

        if user_input is not None:
            self._sensor_to_delete = int(user_input["sensor_id"])
            return await self.async_step_confirm_delete()

        if not storage.sensors:
            return await self.async_step_sensor_list()

        # Build sensor selection options
        sensor_options = [
            {"label": f"[{sid}] {config['name']}", "value": sid}
            for sid, config in sorted(storage.sensors.items(), key=lambda x: int(x[0]))
        ]

        return self.async_show_form(
            step_id="select_sensor_to_delete",
            data_schema=vol.Schema({
                vol.Required("sensor_id"): selector.SelectSelector(
                    selector.SelectSelectorConfig(
                        options=sensor_options,
                        mode=selector.SelectSelectorMode.DROPDOWN
                    )
                ),
            }),
        )

    async def async_step_confirm_delete(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Confirm sensor deletion."""
        storage = self._storage
        sensor_id = self._sensor_to_delete
        sensor_config = storage.get_sensor(sensor_id)

        if sensor_config is None:
            return await self.async_step_sensor_list()

        if user_input is not None:
            if user_input.get("confirm"):
                # Stop coordinator if running
                coordinators = self.hass.data[DOMAIN][self.config_entry.entry_id][
                    "coordinators"
                ]
                if sensor_id in coordinators:
                    await coordinators[sensor_id].stop()
                    del coordinators[sensor_id]

                # Delete sensor
                storage.delete_sensor(sensor_id)
                await storage.async_save()

                _LOGGER.info(f"Deleted sensor {sensor_id}: {sensor_config['name']}")

            return await self.async_step_sensor_list()

        return self.async_show_form(
            step_id="confirm_delete",
            data_schema=vol.Schema({
                vol.Required("confirm", default=False): bool,
            }),
            description_placeholders={
                "sensor_name": sensor_config["name"],
                "sensor_id": str(sensor_id),
            }
        )

    async def async_step_pair_all_sensors(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Send pairing packets for all configured sensors."""
        storage = self._storage

        paired_count = 0
        failed_sensors = []

        for sensor_id_str, sensor_config in storage.sensors.items():
            if not sensor_config.get("enabled", True):
                continue

            sensor_id = int(sensor_id_str)

            try:
                # Get current temperature
                state = self.hass.states.get(sensor_config["entity_id"])
                if not state or state.state in ("unknown", "unavailable"):
                    _LOGGER.warning(
                        f"Skipping pairing for sensor {sensor_id}: temperature unavailable"
                    )
                    failed_sensors.append(sensor_config["name"])
                    continue

                temperature = float(state.state)

                # Send pairing packet
                await self._send_pairing_packet(
                    sensor_id, sensor_config, storage.mac_prefix, temperature
                )

                paired_count += 1
                _LOGGER.info(f"Paired sensor {sensor_id} ({sensor_config['name']})")

            except Exception as e:
                _LOGGER.error(f"Failed to pair sensor {sensor_id}: {e}")
                failed_sensors.append(sensor_config.get("name", str(sensor_id)))

        # Show results
        if paired_count == 0:
            return self.async_abort(
                reason="pairing_failed",
                description_placeholders={
                    "message": "No sensors could be paired. Check that temperature entities are available."
                }
            )
        elif failed_sensors:
            return self.async_abort(
                reason="pairing_partial",
                description_placeholders={
                    "paired": str(paired_count),
                    "failed": ", ".join(failed_sensors)
                }
            )
        else:
            return self.async_create_entry(
                title="",
                data={},
                description_placeholders={"count": str(paired_count)}
            )

    async def _send_pairing_packet(
        self,
        sensor_id: int,
        sensor_config: dict,
        mac_prefix: str,
        temperature: float,
    ) -> None:
        """Send a pairing packet for a sensor."""
        sensor = VenstarSensor(
            sensor_id=sensor_id,
            mac_prefix=mac_prefix,
            name=sensor_config["name"],
            purpose=sensor_config["purpose"],
            scale=sensor_config.get("scale", "F"),
            sequence=1,
        )

        packet = sensor.build_pairing_packet(temperature)
        await self.hass.async_add_executor_job(broadcast_udp_packet, packet)
