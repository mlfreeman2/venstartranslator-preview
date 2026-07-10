# Venstar ACC-TSENWIFI Listener - Home Assistant Integration

> **⚠️ Status: Planned / not yet implemented.** This repository currently contains the [design plan](LISTENER_IMPLEMENTATION_PLAN.md), the [wire protocol documentation](PROTOCOL.md), golden test fixtures, and a development environment — **the integration code has not been written yet**. Watch this repository for progress.

A Home Assistant custom integration that **listens** for the UDP broadcasts sent by Venstar wireless temperature sensors and surfaces each discovered sensor as a Home Assistant device — no cloud, no polling, no configuration beyond adding the integration.

## Which sensors?

- **ACC-TSENWIFIPRO** — the part the protocol was reverse-engineered from. Discontinued by Venstar as of August 2025.
- **ACC-TSENWIFI** — the still-available part that Venstar distributors recommend in its place; believed to speak the same wire protocol.

The wire protocol does not distinguish between the two models — the model field in every packet is simply `TEMPSENSOR`. See [PROTOCOL.md](PROTOCOL.md) for the full protocol documentation.

## What it will do (per the design plan)

- Bind UDP port 5001 and passively decode sensor broadcasts (`local_push` — packets arrive roughly every 1 or 5 minutes per sensor)
- Auto-discover every sensor heard on the wire: one HA device per sensor MAC, with a temperature entity (native °C, exactly what the thermostat sees) and battery/humidity entities created only if the sensor actually reports those fields
- Mirror the thermostat's own staleness rules: a sensor that stops transmitting goes `unavailable` after 5 minutes (20 for Outdoor sensors)
- Survive restarts (discovered-sensor roster and last values persist)
- Optionally ignore sensors emulated by the companion [emulator integration](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) on the same HA instance

See [LISTENER_IMPLEMENTATION_PLAN.md](LISTENER_IMPLEMENTATION_PLAN.md) for the full design, including edge cases and open hardware questions.

## Network requirements

Home Assistant must be on the **same VLAN/broadcast domain** as the sensors (they broadcast to `255.255.255.255:5001`). Dockerized HA needs `network_mode: host` to receive broadcasts.

## Repository contents

| Path | What |
|---|---|
| `LISTENER_IMPLEMENTATION_PLAN.md` | The full design plan |
| `PROTOCOL.md` | Wire protocol documentation (duplicated across all three related repos; the protocol is frozen) |
| `tests/fixtures/csharp_golden_packets.json` | Golden packets serialized by the reference C# implementation — the cross-implementation parity contract |
| `custom_components/venstar_acc_tsenwifi_listener/` | The integration (empty until implementation starts) |
| `.devcontainer/`, `scripts/` | Development environment (see below) |

## Development

Open this repository in VS Code and reopen in the devcontainer, then:

- `scripts/develop` — run a Home Assistant instance (port 8123) with this integration mounted
- `scripts/replay_fixtures.py` — broadcast the golden fixture packets to UDP 5001 in a loop, so the listener has traffic to decode without any emulator or physical sensor present (the same trick works with any `venstar-protobuf-capture/1` capture file)
- `python -m pytest tests/` — run the test suite (once it exists; deps in `requirements-test.txt`)

## Related projects

- [venstar-acc-tsenwifi-emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) — the mirror image: emulates these sensors from HA entities, broadcasting *to* a Venstar thermostat
- [VenstarTranslator](https://github.com/mlfreeman2/venstartranslator) — the C#/Docker application that emulates these sensors from arbitrary JSON HTTP endpoints; also the reference implementation of the protocol

## License

[MIT](LICENSE)
