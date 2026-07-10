# Venstar ACC-TSENWIFI Emulator - Home Assistant Integration

> **⚠️ Status: Beta / Untested** — This integration is feature-complete but has **not yet been tested against real thermostat hardware**. The [Docker/C# version of VenstarTranslator](https://github.com/mlfreeman2/venstartranslator) is the tested and supported way to do this. If you try this integration, please report your results via GitHub issues.
>
> The packet-building code *has* been verified against the C# implementation: an automated harness compared 5,282 packets (both temperature scales swept in fine increments, all sensor purposes, boundary/error temperatures, sequence wrap values, and pairing packets) and confirmed the Python output is byte-for-byte identical to the known-working C# output.

This integration allows Home Assistant to emulate up to 20 Venstar wireless temperature sensors by broadcasting UDP packets to Venstar ColorTouch thermostats. It monitors HA temperature entities and translates their readings into the Venstar sensor protocol format.

## Which sensors does this emulate?

- **ACC-TSENWIFIPRO** — the part this protocol was reverse-engineered from. **Discontinued by Venstar as of August 2025.**
- **ACC-TSENWIFI** — the still-available part that Venstar distributors recommend in its place; believed to speak the same wire protocol.

The wire protocol itself does not distinguish between the two models — the model field in every packet is simply `TEMPSENSOR`. See [PROTOCOL.md](PROTOCOL.md) for the full protocol documentation.

## Renamed from `venstar_translator` (breaking change in 0.3.0)

This integration was previously named **Venstar Translator** with the HA domain `venstar_translator`, developed inside the [VenstarTranslator monorepo](https://github.com/mlfreeman2/venstartranslator). With the move to this dedicated repository it was renamed to domain `venstar_acc_tsenwifi_emulator`. If you installed the old component manually, see [INSTALL.md](INSTALL.md#migration-from-the-venstar_translator-component) for migration steps that preserve your sensor configuration and thermostat pairing.

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

### Via HACS (custom repository)

1. In HACS, choose **Custom repositories** and add `https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator` with category **Integration**
2. Install "Venstar ACC-TSENWIFI Emulator" from HACS
3. Restart Home Assistant
4. Go to **Settings** > **Devices & Services** > **Add Integration** and search for "Venstar"

### Manual Installation

1. Copy the `custom_components/venstar_acc_tsenwifi_emulator` folder from this repository into your Home Assistant `custom_components` directory
2. Restart Home Assistant
3. Go to **Settings** > **Devices & Services** > **Add Integration**
4. Search for "Venstar ACC-TSENWIFI Emulator"

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
| `venstar_acc_tsenwifi_emulator.pair_sensor` | Send a pairing packet for a specific sensor (by ID 0-19) |
| `venstar_acc_tsenwifi_emulator.resend_last_packet` | Resend the last broadcast packet for a sensor (for troubleshooting connectivity) |

## Regenerating the Protobuf Code

`protobuf/sensor_message_pb2.py` is generated from `sensor_message.proto` and checked in. Generated protobuf code refuses to load on a runtime older than the protoc that produced it, and Home Assistant pins its own `protobuf` version (6.31.1 in HA 2025.7, 6.32.0 in HA 2025.12 through at least 2026.4). The checked-in file was generated with protoc 31.1 (via `grpcio-tools` 1.75.1) so it loads on all of those. If you regenerate it, use a protoc no newer than the protobuf version pinned by the oldest Home Assistant release you want to support:

```bash
pip install "grpcio-tools==1.75.1"
cd custom_components/venstar_acc_tsenwifi_emulator/protobuf
python -m grpc_tools.protoc -I. --python_out=. sensor_message.proto
```

The manifest pins `protobuf>=6.31.1,<8`: the protobuf project's [cross-version runtime guarantee](https://protobuf.dev/support/cross-version-runtime-guarantee/) promises a runtime at major N supports gencode from majors N and N−1, so 6.x-era gencode is guaranteed on every 7.x runtime. Before HA's protobuf pin enters 8.x, regenerate the gencode with a 7.x-era protoc and move the bound to `<9`.

## Debug Logging

```yaml
logger:
  logs:
    custom_components.venstar_acc_tsenwifi_emulator: debug
```

See [DEBUG_LOGGING.md](DEBUG_LOGGING.md) for details.

## Development

A devcontainer is included: open this repository in VS Code, reopen in container, and run `scripts/develop` to launch a Home Assistant instance (port 8123) with this integration mounted. Note that UDP broadcasts stay inside the container's network namespace unless you run with host networking, which is fine for protocol-level development (a listener in the same container sees them) but won't reach a physical thermostat.

## Related Projects

- [VenstarTranslator](https://github.com/mlfreeman2/venstartranslator) — the C#/Docker application: same emulation driven by arbitrary JSON HTTP endpoints instead of HA entities, for people who don't run Home Assistant (or whose HA is on the wrong VLAN)
- [venstar-acc-tsenwifi-listener](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener) — the mirror image of this integration: receives broadcasts from *physical* Venstar sensors and surfaces them as HA entities
- [PROTOCOL.md](PROTOCOL.md) — the wire protocol documentation (duplicated in all three repositories; the protocol is frozen)

## License

[MIT](LICENSE)
