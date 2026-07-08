# Venstar Translator - Home Assistant Integration

> **⚠️ Status: Beta / Untested** — This integration is feature-complete but has **not yet been tested against real thermostat hardware**. The [Docker version](../README.md) is the tested and supported way to run Venstar Translator. If you try this integration, please report your results via GitHub issues.
>
> The packet-building code *has* been verified against the C# implementation: an automated harness compared 5,282 packets (both temperature scales swept in fine increments, all sensor purposes, boundary/error temperatures, sequence wrap values, and pairing packets) and confirmed the Python output is byte-for-byte identical to the known-working C# output.

This integration allows Home Assistant to emulate up to 20 Venstar ACC-TSENWIFIPRO wireless temperature sensors by broadcasting UDP packets to Venstar ColorTouch thermostats. It monitors HA temperature entities and translates their readings into the Venstar sensor protocol format.

## Features

- Emulates up to 20 Venstar wireless temperature sensors
- Reads temperature from any HA sensor or climate entity (climate entities use their `current_temperature` attribute)
- Supports all sensor purposes: Outdoor, Remote, Return, Supply
- Fahrenheit and Celsius scales
- Automatic broadcasting (every 1 minute or 5 minutes depending on sensor purpose)
- Full sensor management UI (add, edit, delete, enable/disable)
- Pairing service for initial thermostat setup and re-pairing

## Requirements

- Home Assistant 2025.7.1 or newer (developed and validated against 2026.4.x)
- Venstar ColorTouch thermostat on the **same VLAN/broadcast domain** as Home Assistant
- Temperature sensor entities already configured in Home Assistant

The minimum Home Assistant version matters: the bundled protobuf code requires the `protobuf` 6.31.1+ runtime that Home Assistant 2025.7.1 started shipping, and the options flow uses the `config_entry` property that older Home Assistant versions handled differently.

## Installation

### Manual Installation

1. Copy the `hacs/venstar_translator/custom_components/venstar_translator` folder into your Home Assistant `custom_components` directory
2. Restart Home Assistant
3. Go to **Settings** > **Devices & Services** > **Add Integration**
4. Search for "Venstar Translator"

### Via HACS

Not currently possible. HACS requires `custom_components/` to sit at the **root** of a repository, but this integration lives in the `hacs/` subdirectory of the main VenstarTranslator repo, so adding this repo as a HACS custom repository will fail validation. HACS support would require either publishing the `hacs/` directory as its own repository or shipping zipped release assets (`zip_release` in `hacs.json`). Until then, use the manual installation above.

See [INSTALL.md](INSTALL.md) for detailed setup instructions, sensor configuration, pairing, troubleshooting, and migration from the Docker version.

## Differences from Docker Version

| | Docker (C#) | Home Assistant |
|---|---|---|
| Temperature source | HTTP endpoints + JSONPath | HA entity states |
| Configuration | `sensors.json` file | HA Config Flow UI |
| Scheduling | Hangfire (cron) | Python asyncio |
| MAC prefix | `FakeMacPrefix` env var | Random, persisted |
| Storage | SQLite | HA Store API (JSON) |

The protocol output is identical -- same protobuf packets, same HMAC signatures, same UDP broadcasts. This includes the direct temperature-index calculation (round Fahrenheit to whole degrees half-up, convert to Celsius, round to the nearest 0.5°C, then `index = (celsius + 40) × 2`), which has been verified byte-for-byte against the C# implementation across the full valid range of both scales.

The C# application's HTTP fetching, JSONPath extraction, and healthchecks.io features are intentionally **not** ported: temperature readings come from entities in the Home Assistant instance this integration is installed in, and HA's own tooling covers monitoring.

## Network Requirements

This integration uses UDP broadcast to `255.255.255.255:5001`. Home Assistant **must** be on the same VLAN as the Venstar thermostat. If running HA in Docker, use `network_mode: host`.

## Services (Actions)

Available under **Developer Tools** → **Actions**:

| Action | Description |
|---|---|
| `venstar_translator.pair_sensor` | Send a pairing packet for a specific sensor (by ID 0-19) |
| `venstar_translator.resend_last_packet` | Resend the last broadcast packet for a sensor (for troubleshooting connectivity) |

## Regenerating the Protobuf Code

`protobuf/sensor_message_pb2.py` is generated from `sensor_message.proto` and checked in. Generated protobuf code refuses to load on a runtime older than the protoc that produced it, and Home Assistant pins its own `protobuf` version (6.31.1 in HA 2025.7, 6.32.0 in HA 2025.12 through at least 2026.4). The checked-in file was generated with protoc 31.1 (via `grpcio-tools` 1.75.1) so it loads on all of those. If you regenerate it, use a protoc no newer than the protobuf version pinned by the oldest Home Assistant release you want to support:

```bash
pip install "grpcio-tools==1.75.1"
cd hacs/venstar_translator/custom_components/venstar_translator/protobuf
python -m grpc_tools.protoc -I. --python_out=. sensor_message.proto
```

## Debug Logging

```yaml
logger:
  logs:
    custom_components.venstar_translator: debug
```

See [DEBUG_LOGGING.md](DEBUG_LOGGING.md) for details.

## License

See parent directory LICENSE file.
