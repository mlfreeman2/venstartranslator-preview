# Venstar Sensor Listener — Home Assistant Custom Component Implementation Plan

> **Status: Planned / not yet built.** This document is the design plan for a *second*, standalone HACS component that **listens** for Venstar sensor protobuf packets and surfaces them as Home Assistant entities. It is the mirror image of the existing `venstar_translator` emitter (see [`../venstar_translator/`](../venstar_translator/)): that one *encodes and broadcasts*; this one *receives and decodes*.

## 1. Overview

The Venstar ACC-TSENWIFIPRO wireless temperature sensors — and the `venstar_translator` emulator — broadcast protobuf packets to `255.255.255.255:5001`. This integration binds that port, decodes each packet, and creates a Home Assistant device + entities per discovered sensor.

Because it listens to the broadcast domain, it surfaces:
- **Real Venstar hardware** on the VLAN (turns genuine ACC-TSENWIFIPRO sensors into HA entities), and
- **Emulated sensors** from `venstar_translator` running anywhere on the same segment (full round-trip visibility of what you emit).

### Relationship to the emitter

| | `venstar_translator` (emitter) | `venstar_listener` (this) |
|---|---|---|
| Direction | Encode + **broadcast** | **Receive** + decode |
| Socket | Sends to `255.255.255.255:5001` | Binds `0.0.0.0:5001` |
| Data source | HA entity states | UDP packets on the wire |
| Produces | UDP packets | HA sensor entities |
| Config | Add/edit/pair sensors | Passive; entities auto-discovered |

The two are fully independent components — either can be installed without the other.

## 2. Decisions (finalized)

1. **Domain / name:** `venstar_listener`, display name **"Venstar Sensor Listener"**.
2. **Layout:** own `custom_components/venstar_listener/` subtree under `hacs/venstar_listener/`, matching the sibling emitter after its move. Each subfolder is shaped like a valid standalone HACS repo root.
3. **Humidity entity:** the `INFO` message carries a humidity field, but real hardware is believed **not** to report it (unconfirmed). Create the entity but with `entity_registry_enabled_default = False` **and** `entity_category = diagnostic`, so it is registered-but-disabled — invisible and silent by default, one toggle away if humidity turns out to be real.
4. **No signature verification.** Verifying the HMAC would require capturing pairing packets to be meaningful and risks rejecting genuine hardware whose signing scheme we can't confirm. Dropped — simplifies the config flow (no toggle).
5. **Battery entity:** included as a `diagnostic` entity (data is free in the packet), enabled by default.
6. **Protobuf:** vendor a copy of the emitter's generated code; custom components can't reliably import each other.

## 3. Directory structure

```
hacs/venstar_listener/
├── LISTENER_IMPLEMENTATION_PLAN.md   # this file
├── .gitignore
└── custom_components/
    └── venstar_listener/
        ├── __init__.py          # setup: load roster, start listener, forward "sensor", unload/close
        ├── manifest.json        # domain, local_push, single_config_entry, protobuf req
        ├── const.py             # DOMAIN, UDP_PORT, dispatcher signals, staleness thresholds
        ├── config_flow.py       # one-click setup + options (port, optional mac filter)
        ├── listener.py          # DatagramProtocol + decode + DeviceManager + dispatch
        ├── sensor.py            # temperature/battery/humidity entities + dynamic add
        ├── storage.py           # roster persistence (Store API) for restart survival
        ├── strings.json
        ├── translations/
        │   └── en.json
        └── protobuf/            # vendored copy from the emitter, unchanged
            ├── __init__.py
            ├── sensor_message.proto
            └── sensor_message_pb2.py
```

## 4. Architecture & data flow

```
UDP :5001  ──▶  DatagramProtocol (listener.py, runs on the HA event loop)
                     │  msg.ParseFromString(data)   (try/except → silently drop noise)
                     │  keep Command ∈ {SENSORDATA, SENSORPAIR}
                     ▼
              decode INFO → DecodedReading
                     │   mac, sensor_id, name, purpose(type), temp_c,
                     │   battery, humidity, sequence, power, fw, source_ip, received_at
                     ▼
              DeviceManager (roster: dict[mac → DiscoveredDevice])
                     │  new mac?   → async_dispatcher_send(SIGNAL_NEW_DEVICE, reading)
                     │  known mac? → async_dispatcher_send(SIGNAL_UPDATE.format(mac), reading)
                     ▼
              sensor.py platform → create/update entities (one Device per mac)
```

A ~98-byte packet decodes in microseconds, so decoding happens inline in `datagram_received` (already on the loop). No executor hop is needed, and `async_dispatcher_send` can be called directly.

## 5. What is reused vs new

**Reused (as protocol knowledge, re-vendored):**
- The protobuf schema and generated `sensor_message_pb2.py`.
- The temperature index ↔ degrees relationship (we implement the **inverse** here).

**New:**
- A UDP **receive** path (the repo has only ever *sent*).
- Reverse temperature mapping `index → °C`.
- Dynamic, push-discovered entity creation.
- Roster persistence for restart survival.

## 6. Component detail

### 6a. `protobuf/` — vendored, unchanged
A straight copy of [`../venstar_translator/custom_components/venstar_translator/protobuf/`](../venstar_translator/custom_components/venstar_translator/protobuf/). Same protoc-31.1 gencode already proven to load on HA's pinned protobuf runtime (6.31.1 in HA 2025.7 through 6.32.0 in 2026.4). Import `sensor_message_pb2` at the **module top** of `listener.py` (the emitter does this in `venstar_sensor.py` so the slow descriptor build happens during HA's executor-run import, not on the event loop — importing lazily triggers HA's "blocking call to import_module inside the event loop" warning).

### 6b. `const.py`
```
DOMAIN = "venstar_listener"
UDP_PORT = 5001
DEFAULT_BIND_ADDRESS = "0.0.0.0"

# dispatcher signals
SIGNAL_NEW_DEVICE = "venstar_listener_new_device"
SIGNAL_UPDATE = "venstar_listener_update_{}"   # .format(mac)

# staleness thresholds (seconds) — mirror the thermostat's own error timing
STALE_OUTDOOR = 20 * 60   # outdoor sensors broadcast every 5 min
STALE_DEFAULT = 5 * 60    # everything else broadcasts every 1 min

STORAGE_VERSION = 1
STORAGE_KEY = "venstar_listener"

# purpose labels (match the emitter)
PURPOSE_OUTDOOR = "Outdoor"
PURPOSE_REMOTE = "Remote"
PURPOSE_RETURN = "Return"
PURPOSE_SUPPLY = "Supply"
```

### 6c. `listener.py` — UDP endpoint + decode + DeviceManager

**Socket setup**
- Build a raw socket: `AF_INET`/`SOCK_DGRAM`, `SO_REUSEADDR = 1`, `bind((bind_address, port))`. Binding `0.0.0.0:5001` receives both unicast and broadcast to the port. (`SO_BROADCAST` is only needed to *send*, so it is not required here.)
- `transport, protocol = await hass.loop.create_datagram_endpoint(lambda: proto, sock=sock)`. Keep `transport` to `.close()` on unload.

**`datagram_received(data, addr)`**
1. `msg = SensorMessage(); msg.ParseFromString(data)` inside `try/except` — **silently drop** anything that doesn't parse. The port is a firehose of arbitrary LAN traffic; never log-spam or raise.
2. Keep only `Command in {SENSORDATA, SENSORPAIR}`; ignore firmware/wifi/name/etc.
3. `info = msg.SensorData.Info`; build a `DecodedReading` (below).
4. Hand to `DeviceManager.handle(reading)`.

**Decode helpers**
- **Index → temperature (exact inverse of the emitter's `get_temperature_index`):**
  `celsius = index / 2 − 40` → `0 → −40.0 °C`, `80 → 0.0`, `124 → 22.0`, `253 → 86.5`.
  Native unit is **°C**; report that and let HA convert to the user's display unit.
  *Caveat to document:* this is exactly what the **thermostat** sees (0.5 °C resolution). The original Fahrenheit source reading is **not** recoverable — the forward map rounds °F → whole degrees before converting — which is expected and fine: the point of a listener is "what's actually on the wire."
- **Absent temperature field:** `Temperature` is an optional `uint32`; the emitter omits it at index 0 to stay byte-identical, and protobuf then yields default `0` → `−40.0 °C`. No special handling.
- **Fault sentinels:** index `254` = shorted sensor, `255` = open sensor. Never emitted by our code but possible from real hardware → temperature entity becomes **unavailable** with the fault reason recorded as an attribute.

**`DecodedReading` (dataclass)**
`mac`, `sensor_id`, `name`, `purpose` (from `INFO.Type`), `temp_c | None`, `fault | None`, `battery`, `humidity`, `sequence`, `power`, `fw_major/minor`, `source_ip` (`addr[0]`), `received_at`.

**`DeviceManager`**
- Holds `roster: dict[mac → DiscoveredDevice]`.
- New mac → add to roster, persist immediately, `async_dispatcher_send(SIGNAL_NEW_DEVICE, reading)`.
- Known mac → update `last_seen`/values, `async_dispatcher_send(SIGNAL_UPDATE.format(mac), reading)`, debounced roster write.

### 6d. `sensor.py` — dynamic discovery + entities

Standard HA push-discovery pattern:
- `async_setup_entry` stashes `async_add_entities` and, for each device already in the restored roster, creates entities immediately (as **unavailable** until the next packet). It also subscribes to `SIGNAL_NEW_DEVICE` and creates entities for devices discovered live.
- Each entity subscribes to `SIGNAL_UPDATE.format(mac)` and writes state via `async_write_ha_state`.
- Entities use `RestoreEntity` so the last value returns after a restart until a fresh packet lands.

**Device** (one per physical sensor):
- `identifiers = {(DOMAIN, mac)}`, `name = packet Name`, `manufacturer = "Venstar"`, `model = "ACC-TSENWIFIPRO"`.

**Entities** (unique_id `{mac}_{kind}`):

| Entity | device_class | unit | category | enabled by default | source field |
|---|---|---|---|---|---|
| Temperature | `temperature` | °C (native) | — | ✅ | `INFO.Temperature` (index) |
| Battery | `battery` | % | diagnostic | ✅ | `INFO.Battery` |
| Humidity | `humidity` | % | diagnostic | ❌ (registered, disabled) | `INFO.Humidity` |

State class `measurement` on all three. Diagnostic **attributes** on the temperature entity: `sensor_id`, `purpose`, `sequence`, `power_source`, `firmware`, `source_ip`, `last_seen`, `raw_index`, and `fault` when applicable.

### 6e. Availability / staleness
Mirror the thermostat's error timing: **Outdoor → unavailable after 20 min**, everything else → **5 min**, keyed off the decoded purpose. An `async_track_time_interval` (~60 s) re-evaluates `now − last_seen` and flips entities to unavailable when stale; they auto-recover when packets resume. Each entity's `available` property enforces the same check on read.

### 6f. `storage.py` — roster persistence
Persist the discovered-device roster (mac → name/purpose/last metadata) via the HA Store API, mirroring the emitter's `storage.py`. On startup, recreate entities as **unavailable** so they exist on dashboards before the next broadcast (up to 5 min away for outdoor), with `RestoreEntity` repopulating the last value. New-device discovery writes immediately; routine `last_seen` updates are debounced so we don't hit disk on every packet.

### 6g. `config_flow.py` — minimal
- `async_step_user`: single confirm step; `single_config_entry: true` (abort on second instance).
- **Options flow:** `port` (default 5001) and an optional `mac_prefix_filter` (blank = surface everything; set it to *exclude* your own emulated prefix if the self-echo — see §7 — is noise). Changing options reloads the entry to rebind / re-filter.

### 6h. `__init__.py` — wiring
`async_setup_entry`: load roster → start listener (bind + `create_datagram_endpoint`) → `async_forward_entry_setups(entry, ["sensor"])` → register the staleness timer. `runtime_data` holds the `DeviceManager`, transport, and unsub callbacks (same `entry.runtime_data` style as the emitter). `async_unload_entry`: cancel timer, `transport.close()`, unload the sensor platform, persist roster.

### 6i. `manifest.json`
```json
{
  "domain": "venstar_listener",
  "name": "Venstar Sensor Listener",
  "codeowners": ["@mlfreeman2"],
  "config_flow": true,
  "documentation": "https://github.com/mlfreeman2/venstartranslator-preview/tree/main/hacs/venstar_listener",
  "integration_type": "hub",
  "iot_class": "local_push",
  "issue_tracker": "https://github.com/mlfreeman2/venstartranslator-preview/issues",
  "requirements": ["protobuf>=6.31.1"],
  "single_config_entry": true,
  "version": "0.1.0"
}
```
`integration_type: hub` fits a component that discovers and produces multiple devices. `iot_class: local_push` matches the passive-receive model.

## 7. Edge cases & gotchas

- **Self-echo when the emitter runs on the same host.** The emitter broadcasts to `255.255.255.255:5001`; a listener bound to `5001` on the same host **will receive the emitter's own packets** (Linux delivers broadcast to local listeners), so emulated sensors appear here too. Arguably a feature (round-trip visibility); the `mac_prefix_filter` option hides them if unwanted. There is **no bind conflict** — the emitter never binds 5001, it only sends to it.
- **`network_mode: host`.** HA must be on the thermostat's VLAN/broadcast domain, and dockerized HA needs host networking to receive broadcasts. Document it (same requirement as the emitter).
- **Never raise/log-spam on bad packets.** The port sees arbitrary LAN traffic; parse failures must be swallowed quietly.
- **Blocking-import warning.** Import `sensor_message_pb2` at module top (executor time), never lazily on the loop.
- **HACS distribution caveat inherited.** Living under `hacs/` (a subdirectory) means HACS-from-repo won't validate — manual install only, unless the `hacs/venstar_listener/` tree is published as its own repo or ships `zip_release` assets. State this in the component README.
- **Lossy Fahrenheit round-trip.** See §6c — report native °C as truth; do not attempt to reconstruct the source °F.

## 8. Testing (pytest-homeassistant-custom-component, like the emitter)

- **Round-trip parity:** feed the emitter's own `build_data_packet(temp)` output into this component's decode and assert the recovered °C matches the index math across a full sweep of both scales — this pins the two components together.
- Fault sentinels (254/255) → temperature unavailable; absent-temperature field → −40.0 °C.
- Malformed / non-Venstar / wrong-command packets → silently ignored, no entity created.
- Dynamic discovery: unseen mac → new Device + entities; repeat packet → state update, no duplicate.
- Humidity entity created but **disabled by default**; battery + temperature enabled.
- Staleness → unavailable after the per-purpose threshold; recovery on resume.
- Config/options flow: setup, second-instance abort, port + filter changes reload cleanly.
- Restart: roster restores entities as unavailable, `RestoreEntity` brings back last value.

## 9. Docs

New `hacs/venstar_listener/custom_components/venstar_listener/README.md` (+ INSTALL notes), and a short "emitter vs listener" section added to the top-level HACS README, using the same "Beta / untested against hardware" framing the emitter uses.

## 10. Phasing (each phase independently demoable)

1. **Skeleton + vendored protobuf + listener that logs decoded readings** — prove packets decode end-to-end.
2. **Sensor platform + dynamic discovery + device model** — temperature only. First "watch real packets become HA entities."
3. **Battery + disabled-by-default humidity + diagnostic attributes + staleness/availability.**
4. **Roster persistence + restart restore.**
5. **Config/options flow polish (port, mac filter).**
6. **Tests + docs.**

## 11. Open / future

- Confirm whether real ACC-TSENWIFIPRO hardware ever populates humidity; if so, flip the humidity entity to enabled-by-default.
- Optional: surface pairing packets (`SENSORPAIR`) as a discovery event or log, useful for debugging thermostat pairing.
- Optional future convergence with C# app idea #2 (a protobuf listener/tester on the server side) — shared protocol understanding, different runtimes.
