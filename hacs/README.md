# Venstar Translator - Home Assistant Integration

This integration allows Home Assistant to emulate up to 20 Venstar ACC-TSENWIFIPRO wireless temperature sensors by broadcasting UDP packets to Venstar ColorTouch thermostats. It monitors HA temperature entities and translates their readings into the Venstar sensor protocol format.

## Features

- Emulates up to 20 Venstar wireless temperature sensors
- Reads temperature from any HA sensor or climate entity
- Supports all sensor purposes: Outdoor, Remote, Return, Supply
- Fahrenheit and Celsius scales
- Automatic broadcasting (every 1 minute or 5 minutes depending on sensor purpose)
- Full sensor management UI (add, edit, delete, enable/disable)
- Pairing service for initial thermostat setup and re-pairing

## Requirements

- Home Assistant 2025.7.1 or newer
- Venstar ColorTouch thermostat on the **same VLAN/broadcast domain** as Home Assistant
- Temperature sensor entities already configured in Home Assistant

## Installation

### Manual Installation

1. Copy the `custom_components/venstar_translator` folder into your Home Assistant `custom_components` directory
2. Restart Home Assistant
3. Go to **Settings** > **Devices & Services** > **Add Integration**
4. Search for "Venstar Translator"

### Via HACS

1. Add this repository as a custom repository in HACS
2. Install "Venstar Translator"
3. Restart Home Assistant
4. Add the integration via **Settings** > **Devices & Services**

See [INSTALL.md](INSTALL.md) for detailed setup instructions, sensor configuration, pairing, troubleshooting, and migration from the Docker version.

## Differences from Docker Version

| | Docker (C#) | Home Assistant |
|---|---|---|
| Temperature source | HTTP endpoints + JSONPath | HA entity states |
| Configuration | `sensors.json` file | HA Config Flow UI |
| Scheduling | Hangfire (cron) | Python asyncio |
| MAC prefix | `FakeMacPrefix` env var | Random, persisted |
| Storage | SQLite | HA Store API (JSON) |

The protocol output is identical -- same protobuf packets, same HMAC signatures, same UDP broadcasts.

## Network Requirements

This integration uses UDP broadcast to `255.255.255.255:5001`. Home Assistant **must** be on the same VLAN as the Venstar thermostat. If running HA in Docker, use `network_mode: host`.

## Services

| Service | Description |
|---|---|
| `venstar_translator.pair_sensor` | Send a pairing packet for a specific sensor (by ID 0-19) |
| `venstar_translator.resend_last_packet` | Resend the last broadcast packet for a sensor (for troubleshooting connectivity) |

## Debug Logging

```yaml
logger:
  logs:
    custom_components.venstar_translator: debug
```

See [DEBUG_LOGGING.md](DEBUG_LOGGING.md) for details.

## License

See parent directory LICENSE file.
