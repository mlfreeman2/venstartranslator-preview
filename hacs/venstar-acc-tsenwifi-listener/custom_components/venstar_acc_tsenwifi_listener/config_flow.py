"""Config flow for the Venstar ACC-TSENWIFI Listener integration."""
from __future__ import annotations

from typing import Any

import voluptuous as vol

from homeassistant.config_entries import (
    ConfigEntry,
    ConfigFlow,
    ConfigFlowResult,
    OptionsFlow,
)
from homeassistant.core import callback

from .const import (
    CONF_IGNORE_LOCAL_EMULATED,
    CONF_PORT,
    DOMAIN,
    UDP_PORT,
)


class VenstarListenerConfigFlow(ConfigFlow, domain=DOMAIN):
    """One-click setup — the listener is passive, so there is nothing to enter."""

    VERSION = 1

    async def async_step_user(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Handle the initial confirm step."""
        if self._async_current_entries():
            return self.async_abort(reason="single_instance_allowed")

        if user_input is not None:
            return self.async_create_entry(
                title="Venstar ACC-TSENWIFI Listener", data={}
            )

        return self.async_show_form(step_id="user")

    @staticmethod
    @callback
    def async_get_options_flow(config_entry: ConfigEntry) -> OptionsFlow:
        """Get the options flow for this handler."""
        # self.config_entry is provided by the OptionsFlow base class; assigning
        # it explicitly crashes since HA 2025.12 (matches the emulator).
        return VenstarListenerOptionsFlow()


class VenstarListenerOptionsFlow(OptionsFlow):
    """Options: UDP port and the local-emulator filter. Bind address is not an
    option — only 0.0.0.0 receives limited-broadcast datagrams (§6g)."""

    async def async_step_init(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Manage the options; saving reloads the entry to rebind."""
        if user_input is not None:
            return self.async_create_entry(title="", data=user_input)

        options = self.config_entry.options
        schema = vol.Schema(
            {
                vol.Optional(
                    CONF_PORT, default=options.get(CONF_PORT, UDP_PORT)
                ): vol.All(vol.Coerce(int), vol.Range(min=1, max=65535)),
                vol.Optional(
                    CONF_IGNORE_LOCAL_EMULATED,
                    default=options.get(CONF_IGNORE_LOCAL_EMULATED, False),
                ): bool,
            }
        )
        return self.async_show_form(step_id="init", data_schema=schema)
