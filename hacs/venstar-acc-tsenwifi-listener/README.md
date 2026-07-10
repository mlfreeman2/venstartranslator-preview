# Venstar ACC-TSENWIFI Listener - Home Assistant Integration

> **⚠️ Status: Beta / Untested against hardware** — This integration is feature-complete but has **not yet been tested against physical Venstar sensors** (none were on hand during development). Its decode path *has* been verified byte-for-byte against golden packets serialized by the reference C# implementation, and end-to-end against the companion [emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator), which is how it was developed and tested. Real-hardware unknowns (does it report humidity? how is its MAC formatted? does it repeat packets?) are tracked in the [implementation plan](LISTENER_IMPLEMENTATION_PLAN.md#12-open--hardware-unknowns) — the decode path is deliberately liberal about all of them. **If you own physical sensors, please report results** (and, ideally, a packet capture) via [GitHub issues](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener/issues).

This integration **listens** for the UDP broadcasts that Venstar wireless temperature sensors send on your LAN and surfaces each one as a Home Assistant device — no cloud, no polling, no configuration beyond adding the integration. It is the mirror image of the [Venstar ACC-TSENWIFI Emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator): that one *encodes and broadcasts*; this one *receives and decodes*.

## Which sensors?

- **ACC-TSENWIFIPRO** — the part this protocol was reverse-engineered from. **Discontinued by Venstar as of August 2025.**
- **ACC-TSENWIFI** — the still-available part that Venstar distributors recommend in its place; believed to speak the same wire protocol.

The wire protocol does not distinguish between the two models — the model field in every packet is simply `TEMPSENSOR`. See [PROTOCOL.md](PROTOCOL.md) for the full protocol documentation.

## What it does

- Binds UDP port 5001 and passively decodes sensor broadcasts (`local_push` — packets arrive roughly every 1 minute, or every 5 minutes for Outdoor sensors).
- **Auto-discovers every sensor heard on the wire**: one HA device per sensor MAC, with a **temperature** entity reporting native °C (exactly what the thermostat sees, at the protocol's 0.5 °C resolution).
- **Battery and humidity entities are created only when a sensor actually reports those fields**, and the capability is remembered across restarts. (The emulator always sends battery, so emulated sensors always get a battery entity; humidity is believed to be absent on real hardware, so a humidity entity may never appear — that's expected.)
- **Mirrors the thermostat's own staleness rules**: a sensor that stops transmitting goes `unavailable` after 5 minutes (20 minutes for Outdoor sensors). A sensor reporting a fault (shorted/open) stays *available* with an `unknown` value and a `fault` attribute — that's a different failure from "stopped transmitting."
- **Follows renames on the wire**, while always preferring a name you set manually in Home Assistant.
- **Survives restarts**: the discovered-device roster and last values persist, and availability is recomputed from the persisted last-seen time.
- **Deleting a device** is supported. Note that a device that is *still transmitting* is simply rediscovered on its next packet — stop the source (or use the option below) to make deletion stick.

## The `ignore_local_emulated` option

If you run the companion [emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) on the **same** Home Assistant instance, this listener will also hear the sensors the emulator derived from your HA entities in the first place. That is useful for testing (and is **off by default** so a fresh side-by-side install shows them), but for a mixed emulated + physical fleet you usually don't want the duplicates. Turn on **Ignore locally-emulated sensors** (integration → **Configure**) and the listener drops the co-installed emulator's packets automatically — and deleting those devices finally sticks. It covers only the same-instance emulator; the C# app or an emulator on another host can't be identified authoritatively (disable/delete those entities manually).

## Network requirements

Home Assistant must be on the **same VLAN/broadcast domain** as the sensors (they broadcast to `255.255.255.255:5001`). Dockerized Home Assistant needs `network_mode: host` to receive broadcasts. There is no bind conflict with the emulator — it only ever *sends* to port 5001.

## Installation

### Via HACS (custom repository)

1. In HACS, choose **Custom repositories** and add `https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener` with category **Integration**.
2. Install "Venstar ACC-TSENWIFI Listener" from HACS.
3. Restart Home Assistant.
4. Go to **Settings** → **Devices & Services** → **Add Integration** and search for "Venstar".

### Manual installation

Copy `custom_components/venstar_acc_tsenwifi_listener/` into your Home Assistant `custom_components` directory and restart. See [INSTALL.md](INSTALL.md) for detailed steps, verification, and troubleshooting.

## Requirements

- Home Assistant 2025.7.1 or newer. The bundled protobuf code needs the `protobuf` 6.31.1+ runtime that HA 2025.7.1 started shipping, and `single_config_entry` requires a reasonably recent HA.
- Physical ACC-TSENWIFI(PRO) sensors (or the emulator / the C# app) broadcasting on the same network segment.

## Debug logging

```yaml
logger:
  logs:
    custom_components.venstar_acc_tsenwifi_listener: debug
```

The integration also exposes **config-entry diagnostics** (integration → **⋮** → *Download diagnostics*): the full discovered-device roster, packet counters (parsed / dropped / deduped / filtered), and each sensor's last reading. This is the first thing to grab for a "why isn't my sensor showing up?" report.

## Regenerating the protobuf code

`protobuf/sensor_message_pb2.py` is vendored (a straight copy of the emulator's, generated with protoc 31.1) and checked in for HACS distribution. The manifest pins `protobuf>=6.31.1,<8`: the protobuf project's [cross-version runtime guarantee](https://protobuf.dev/support/cross-version-runtime-guarantee/) promises a runtime at major N supports gencode from majors N and N−1, so this 6.x-era gencode is guaranteed on every 7.x runtime. Before HA's protobuf pin enters 8.x, regenerate with a 7.x-era protoc and move the bound to `<9`.

## Development

A devcontainer is included. Open this repository in VS Code, reopen in the container, then:

- `scripts/develop` — run a Home Assistant instance (port 8123) with this integration mounted.
- `scripts/replay_fixtures.py` — broadcast the golden fixture packets to UDP 5001 in a loop, so the listener has traffic to decode without any emulator or physical sensor present. It also replays any `venstar-protobuf-capture/1` capture file, which turns a user's capture attached to a bug report into locally reproducible traffic.
- `python -m pytest tests/` — run the test suite (deps in `requirements-test.txt`).

## Related projects

- [venstar-acc-tsenwifi-emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) — the mirror image: emulates these sensors from HA entities, broadcasting *to* a Venstar thermostat.
- [VenstarTranslator](https://github.com/mlfreeman2/venstartranslator) — the C#/Docker application that emulates these sensors from arbitrary JSON HTTP endpoints; also the reference implementation of the protocol, and home of the family-level "which piece do I need?" overview.
- [PROTOCOL.md](PROTOCOL.md) — the wire protocol documentation (duplicated across all three repositories; the protocol is frozen).

## License

[MIT](LICENSE)
