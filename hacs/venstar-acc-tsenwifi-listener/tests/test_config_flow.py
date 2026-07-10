"""Config and options flow tests."""
from __future__ import annotations

from homeassistant.config_entries import SOURCE_USER
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResultType
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.venstar_acc_tsenwifi_listener.const import (
    CONF_IGNORE_LOCAL_EMULATED,
    CONF_PORT,
    DOMAIN,
)

from .helpers import setup_listener


async def test_user_flow_creates_entry(hass: HomeAssistant) -> None:
    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )
    assert result["type"] is FlowResultType.FORM
    assert result["step_id"] == "user"

    result = await hass.config_entries.flow.async_configure(result["flow_id"], {})
    await hass.async_block_till_done()
    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert result["title"] == "Venstar ACC-TSENWIFI Listener"


async def test_single_instance_aborts(hass: HomeAssistant) -> None:
    MockConfigEntry(domain=DOMAIN, data={}).add_to_hass(hass)
    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )
    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "single_instance_allowed"


async def test_options_flow(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)

    result = await hass.config_entries.options.async_init(entry.entry_id)
    assert result["type"] is FlowResultType.FORM
    assert result["step_id"] == "init"

    result = await hass.config_entries.options.async_configure(
        result["flow_id"],
        {CONF_PORT: 15701, CONF_IGNORE_LOCAL_EMULATED: True},
    )
    await hass.async_block_till_done()
    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert entry.options[CONF_PORT] == 15701
    assert entry.options[CONF_IGNORE_LOCAL_EMULATED] is True


async def test_options_flow_defaults(hass: HomeAssistant) -> None:
    entry = await setup_listener(hass)
    result = await hass.config_entries.options.async_init(entry.entry_id)
    schema_keys = {str(key): key for key in result["data_schema"].schema}
    assert CONF_PORT in schema_keys
    assert CONF_IGNORE_LOCAL_EMULATED in schema_keys
    # Defaults: port 5001, filter off.
    assert schema_keys[CONF_PORT].default() == 5001
    assert schema_keys[CONF_IGNORE_LOCAL_EMULATED].default() is False
