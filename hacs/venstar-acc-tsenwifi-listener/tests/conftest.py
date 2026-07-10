"""Pytest bootstrap for the Venstar ACC-TSENWIFI Listener test suite.

There was no Python test infrastructure in this repo before this suite — it is
bootstrapped from scratch on pytest-homeassistant-custom-component (§8). Run
from the repo root with ``python -m pytest tests/``.
"""
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import pytest

# Make ``custom_components.venstar_acc_tsenwifi_listener`` importable when pytest
# is run from the repo root.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

pytest_plugins = "pytest_homeassistant_custom_component"


@pytest.fixture(autouse=True)
def auto_enable_custom_integrations(enable_custom_integrations):
    """Load custom integrations in every test (required by the HA test harness)."""
    yield


@pytest.fixture(autouse=True)
def mock_datagram_endpoint():
    """Replace the real socket bind with a fake so tests never open a socket.

    The real protocol object is still built from the factory (so ``feed()``
    exercises the whole decode → dispatch path); only the kernel socket is
    skipped. The real ``async_create_listener`` stays reachable via the
    ``listener`` module for the dedicated socket test. This patches the name as
    imported into the integration's ``__init__`` (where setup calls it).
    """
    created: list[SimpleNamespace] = []

    async def _fake(hass, port, protocol_factory):
        protocol = protocol_factory()
        transport = MagicMock()
        transport.get_extra_info.side_effect = (
            lambda key, *a: ("0.0.0.0", port) if key == "sockname" else None
        )
        created.append(SimpleNamespace(port=port, protocol=protocol, transport=transport))
        return transport, protocol

    with patch(
        "custom_components.venstar_acc_tsenwifi_listener.async_create_listener",
        side_effect=_fake,
    ) as mock:
        mock.created = created
        yield mock
