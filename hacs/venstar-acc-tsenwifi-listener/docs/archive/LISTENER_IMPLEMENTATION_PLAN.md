# Venstar ACC-TSENWIFI Listener — Home Assistant Custom Component Implementation Plan (implemented)

> **Archived.** This design plan is implemented (phases 1–6); it lives in `docs/archive/` as the architecture record. Phases 7 (first release) and 8 (capture export) remain open. In-document relative paths refer to its original location (the repo root).

> **Status: Implemented (July 2026) through phase 6; phases 7 (first tagged release) and 8 (capture export fast-follow) remain open.** Kept as the architecture record for the integration — the decode gates, dedup, discovery, staleness, and storage behavior below all describe the shipped code; the user-facing docs are README.md and INSTALL.md. This is a standalone HACS component that **listens** for Venstar sensor protobuf packets and surfaces them as Home Assistant entities. It is the mirror image of the [Venstar ACC-TSENWIFI Emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) (HA domain `venstar_acc_tsenwifi_emulator`): that one *encodes and broadcasts*; this one *receives and decodes*.
>
> **Not to be confused with the C# app's [`PROTOBUF_LISTENER_PLAN.md`](https://github.com/mlfreeman2/venstartranslator/blob/main/docs/archive/PROTOBUF_LISTENER_PLAN.md)** — that plan adds a diagnostic capture *page to the C# VenstarTranslator app*. Both bind UDP 5001 and decode the same protocol, but they are different deliverables for different audiences: **this component is an end-user product** — someone who owns physical Venstar sensors installs it to pull them into HA — while the C# page is a maintainer-facing troubleshooting tool. If you were asked to implement "the listener plan," confirm which file you were pointed at.

## 1. Overview

The Venstar wireless temperature sensors — the **ACC-TSENWIFIPRO** (the part this protocol was reverse-engineered from; discontinued by Venstar in August 2025) and the still-available **ACC-TSENWIFI** that distributors recommend in its place (believed to speak the same wire protocol — the packets' model field says only `TEMPSENSOR`, with no per-part distinction) — and the emulator component all broadcast protobuf packets to `255.255.255.255:5001`. This integration binds that port, decodes each packet, and creates a Home Assistant device + entities per discovered sensor. The wire protocol itself — packet types, temperature lookup tables, HMAC details, example flows — is documented in [`PROTOCOL.md`](PROTOCOL.md) (duplicated in this repo; the protocol is frozen); read it alongside this plan.

**The primary goal is ingesting real Venstar hardware into HA directly.** A user who owns physical ACC-TSENWIFI(PRO) sensors should be able to install this component by itself and ignore the emulator and the C# app entirely — no shared code, no cross-imports (protobuf is vendored, §6a).

Because it listens to the broadcast domain, it also surfaces **emulated sensors** from `venstar_acc_tsenwifi_emulator` / the C# app on the same segment. That is how the component will actually be *tested* (no physical sensor is on hand), so the decode path must stay liberal about anything real hardware might do differently — the unconfirmed hardware behaviors are tracked in §12.

### Relationship to the emulator

| | `venstar_acc_tsenwifi_emulator` | `venstar_acc_tsenwifi_listener` (this) |
|---|---|---|
| Direction | Encode + **broadcast** | **Receive** + decode |
| Socket | Sends to `255.255.255.255:5001` | Binds `0.0.0.0:5001` |
| Data source | HA entity states | UDP packets on the wire |
| Produces | UDP packets | HA sensor entities |
| Config | Add/edit/pair sensors | Passive; entities auto-discovered |

The two are fully independent components — either can be installed without the other.

## 2. Decisions (finalized)

1. **Domain / name:** `venstar_acc_tsenwifi_listener`, display name **"Venstar ACC-TSENWIFI Listener"**.
2. **Layout & distribution:** this repository (`mlfreeman2/venstar-acc-tsenwifi-listener`) is the component's dedicated home. HACS is hard-limited to one integration per repository, so the integration lives at `custom_components/venstar_acc_tsenwifi_listener/` at the repo root and the repo doubles as its own HACS distribution repo (see §10). Protocol knowledge is duplicated into this repo (`PROTOCOL.md`, the golden fixtures) so it is fully independent of the emulator repo and the C# app — the protocol is frozen, so the copies cannot drift. (If implementation starts while this folder still lives in the VenstarTranslator monorepo's `hacs/` directory, nothing changes — the layout is identical, and the initial push to the dedicated repo simply includes the finished component.)
3. **Humidity & battery entities — dynamic capability discovery.** Real hardware is believed not to report humidity (the T8900 ignores it), but this is unconfirmed. Best practice for "might always be bogus": **create the entity only when a packet actually carries the field** (`HasField`), persist a per-device capability flag in the roster so the entity survives restarts, and never create it otherwise. Once created, humidity is a normal enabled-by-default measurement (no `diagnostic` category — that was only a hiding mechanism, unnecessary under dynamic creation). Battery uses the same pattern for uniformity (`diagnostic` category, enabled); the emulator always sends `Battery=100`, so battery entities will always exist for emulated sensors.
4. **No signature verification.** Verifying the HMAC would require capturing pairing packets to be meaningful and risks rejecting genuine hardware whose signing scheme we can't confirm. Dropped — simplifies the config flow (no toggle).
5. **Protobuf:** vendor a copy of the emulator's generated code (identical gencode lives in the emulator repo); custom components can't reliably import each other.
6. **Default purpose is `Remote`, not proto2's silent default.** `INFO.Type` is optional; reading it directly on an absent field yields `OUTDOOR` (first enum value) with no error. Always check `HasField("Type")`; absent or unrecognized → **Remote** (and Remote's 5-minute staleness threshold).
7. **Device deletion is supported** via `async_remove_config_entry_device` (purges the mac from the roster). A deleted device that is *still transmitting* will simply be rediscovered on its next packet — expected; stop the source to make deletion stick. Document this.
8. **Renames follow the wire.** The mac is the identity key (same as the thermostat's own view); the packet `Name` is display metadata. When a known mac's Name changes, update the device registry via `device_registry.async_update_device` — which preserves `name_by_user`, so a user's manual rename in HA always wins.
9. **No general MAC filtering.** Surface every sensor heard on the wire. Users disable entities they don't want, or delete the device and stop the source. (An earlier draft had a user-managed `mac_prefix_filter` option; dropped — its include/exclude semantics were ambiguous. The one targeted exception is §2.10, which needs no user-managed list.)
10. **Optional `ignore_local_emulated` toggle (default off) for mixed emulated + physical fleets.** With both HACS components on one HA instance, the listener re-ingests sensors the emulator derived from HA entities in the first place. The listener can detect these *exactly*, with zero configuration and zero coupling: the emulator's config entry stores its 10-hex `mac_prefix` (`entry.data["mac_prefix"]`), and every MAC it broadcasts is that prefix + a 2-hex sensor id. When the toggle is on, packets whose normalized mac starts with any co-installed `venstar_acc_tsenwifi_emulator` entry's prefix are dropped at the §6c validation gate — no device, no unique_id — and deleting an already-created emulated device finally *sticks* (the §2.7 rediscovery caveat doesn't apply to filtered macs). Detection is race-free by causality: the emulator cannot broadcast before its config entry (and thus the prefix) exists. Default **off**, because watching the emulator's packets is exactly how this component gets tested (§1) — a fresh side-by-side install must show them. This deliberately covers *only* the same-instance emulator: the C# app or an emulator on another HA instance can't be identified authoritatively, and covering them would reopen the generic-filter ambiguity that §2.9 rejected (those users keep the manual disable/delete path). One known gap, documented in the emulator's INSTALL.md: an install migrated from the pre-rename `venstar_translator` component via the storage-file path broadcasts its *storage* prefix while the entry records a freshly generated one, so detection misses it — those users also keep the manual disable/delete path.

## 3. Directory structure

```
venstar-acc-tsenwifi-listener/        # repo root == HACS repo root
├── LISTENER_IMPLEMENTATION_PLAN.md   # this file
├── .gitignore
├── .devcontainer/           # Python dev environment; scripts/develop runs HA on :8123
├── .github/                 # dependabot; CI validation lands with phase 1 code (§11)
├── scripts/                 # setup / develop / replay_fixtures.py dev harness
├── hacs.json                # name, render_readme, min HA version (match emulator: 2025.7.1)
├── PROTOCOL.md              # wire protocol doc — duplicated across all three repos (frozen protocol)
├── LICENSE                  # MIT
├── README.md                # standalone install + usage; written for physical-sensor owners
├── INSTALL.md               # manual + HACS-custom-repo install steps (written when the component ships)
├── requirements-test.txt    # pytest harness deps (§8)
├── tests/
│   ├── conftest.py          # harness bootstrap (§8) — created alongside the first tests
│   ├── fixtures/
│   │   └── csharp_golden_packets.json   # ALREADY CHECKED IN — golden C#-app packets (§8)
│   └── test_*.py            # split by area (decode, dedup, discovery, …) — implementer's call
└── custom_components/
    └── venstar_acc_tsenwifi_listener/
        ├── __init__.py          # setup order, unload, async_remove_config_entry_device
        ├── manifest.json        # domain, local_push, single_config_entry, protobuf req
        ├── const.py             # DOMAIN, UDP_PORT, dispatcher signals, staleness thresholds
        ├── config_flow.py       # one-click setup + options (port, ignore_local_emulated)
        ├── listener.py          # DatagramProtocol + decode + DeviceManager + dispatch
        ├── sensor.py            # temperature/battery/humidity entities + dynamic add
        ├── storage.py           # roster persistence (Store API) for restart survival
        ├── diagnostics.py       # config-entry diagnostics: roster, counters, last readings
        ├── strings.json
        ├── translations/
        │   └── en.json
        └── protobuf/            # vendored copy from the emulator, unchanged
            ├── __init__.py
            ├── sensor_message.proto
            └── sensor_message_pb2.py
```

README/INSTALL/hacs.json live at the repo root (not inside `custom_components/`) — this repository is itself the HACS repo root (§10). `tests/` ships with the repo; parity with the other implementations is pinned entirely by the checked-in golden fixtures (§8), so the suite has no cross-repo dependencies.

## 4. Architecture & data flow

```
UDP :5001  ──▶  DatagramProtocol (listener.py, runs on the HA event loop)
                     │  msg.ParseFromString(data)   (try/except → silently drop noise)
                     │  validate: HasField(SensorData), Command ∈ {SENSORDATA, SENSORPAIR},
                     │            mac normalizes to 12 hex chars, not locally-emulated
                     │            (optional, §2.10), temp index sane
                     ▼
              decode INFO → DecodedReading
                     │   mac, sensor_id, name, purpose(type), temp_c, fault,
                     │   battery|None, humidity|None, sequence, power, fw,
                     │   source_ip, received_at
                     ▼
              DeviceManager (roster: dict[mac → DiscoveredDevice])
                     │  duplicate sequence? → refresh last_seen only, no dispatch
                     │  new mac?        → roster add, persist, SIGNAL_NEW_DEVICE
                     │  new capability? → set flag, persist, SIGNAL_NEW_DEVICE (platform adds
                     │                     only the missing entity — see §6d created-set)
                     │  name changed?   → device_registry.async_update_device
                     │  known mac       → update values, SIGNAL_UPDATE.format(mac), debounced save
                     ▼
              sensor.py platform → create/update entities (one Device per mac)
```

A ~98-byte packet decodes in microseconds, so decoding happens inline in `datagram_received` (already on the loop). No executor hop is needed, and `async_dispatcher_send` can be called directly.

**Duplicate suppression is required, not optional.** Both the emulator and the C# app send every packet **5 times** (`BROADCAST_REPEAT_COUNT = 5`; believed to be a Venstar protocol convention, so real hardware likely repeats too). Without dedup, every reading is 5 state writes. The dedup key is plain **`sequence`** per mac. There is one known collision, deliberately accepted: a pairing packet (always sequence 1) is followed by a data packet that *also* carries sequence 1 (the emulator resets to 1 after pairing), so that first data packet gets deduped. That's fine — pairing requires a physical button push on real hardware, so it's vanishingly rare, and the pairing packet carries the same INFO payload (temperature included), so the reading was already delivered; the stream self-heals at the next broadcast (sequence 2). Not worth widening the key to `(command, sequence)`. Duplicates still refresh `last_seen` (so a C#-app "resend last packet" — same sequence by design — keeps the sensor from going stale) but are not dispatched and trigger no save.

## 5. What is reused vs new

**Reused (as protocol knowledge, re-vendored):**
- The protobuf schema and generated `sensor_message_pb2.py`.
- The temperature index ↔ degrees relationship (we implement the **inverse** here).

**New:**
- A UDP **receive** path (the repo has only ever *sent*).
- Reverse temperature mapping `index → °C`.
- Dynamic, push-discovered entity creation (devices *and* per-device capabilities).
- Roster persistence for restart survival.

## 6. Component detail

### 6a. `protobuf/` — vendored, unchanged
A straight copy of the emulator's [`custom_components/venstar_acc_tsenwifi_emulator/protobuf/`](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator/tree/main/custom_components/venstar_acc_tsenwifi_emulator/protobuf). Same protoc-31.1 gencode already proven to load on HA's pinned protobuf runtime (6.31.1 in HA 2025.7 through 6.32.0 in 2026.4). Import `sensor_message_pb2` at the **module top** of `listener.py` (the emulator does this in `venstar_sensor.py` so the slow descriptor build happens during HA's executor-run import, not on the event loop — importing lazily triggers HA's "blocking call to import_module inside the event loop" warning).

### 6b. `const.py`
```python
DOMAIN = "venstar_acc_tsenwifi_listener"
UDP_PORT = 5001
DEFAULT_BIND_ADDRESS = "0.0.0.0"   # must stay wildcard: binding a specific unicast
                                   # address does NOT receive 255.255.255.255 broadcasts

# dispatcher signals
SIGNAL_NEW_DEVICE = f"{DOMAIN}_new_device"
SIGNAL_UPDATE = DOMAIN + "_update_{}"   # .format(mac)
SIGNAL_AVAILABILITY = f"{DOMAIN}_availability_tick"   # payload-less; staleness sweep (§6e)

# options
CONF_PORT = "port"
CONF_IGNORE_LOCAL_EMULATED = "ignore_local_emulated"   # §2.10, default False
EMITTER_DOMAIN = "venstar_acc_tsenwifi_emulator"   # co-installed emulator detection (§6c step 4)

# staleness thresholds (seconds) — mirror the thermostat's own error timing
STALE_OUTDOOR = 20 * 60   # outdoor sensors broadcast every 5 min
STALE_DEFAULT = 5 * 60    # everything else broadcasts every 1 min

STORAGE_VERSION = 1
STORAGE_KEY = DOMAIN

# purpose labels (match the emulator)
PURPOSE_OUTDOOR = "Outdoor"
PURPOSE_REMOTE = "Remote"
PURPOSE_RETURN = "Return"
PURPOSE_SUPPLY = "Supply"
DEFAULT_PURPOSE = PURPOSE_REMOTE   # for absent/unknown INFO.Type — see §2.6
```

### 6c. `listener.py` — UDP endpoint + decode + DeviceManager

**Socket setup**
- Build a raw socket: `AF_INET`/`SOCK_DGRAM`, `SO_REUSEADDR = 1`, `sock.setblocking(False)` (required when handing a pre-built socket to asyncio), `bind((bind_address, port))`. Binding `0.0.0.0:5001` receives both unicast and broadcast to the port. (`SO_BROADCAST` is only needed to *send*, so it is not required here.)
- **Bind failure → `ConfigEntryNotReady`.** If something else already holds 5001 (without `SO_REUSEADDR`), `bind` raises `OSError`; catch it in `async_setup_entry` and raise `ConfigEntryNotReady` so HA retries with backoff instead of hard-failing the entry. Note `SO_REUSEADDR` only enables coexistence if *both* binders set it — it is a mitigation, not a guarantee.
- `transport, protocol = await hass.loop.create_datagram_endpoint(lambda: proto, sock=sock)`. Keep `transport` to `.close()` on unload.

**`datagram_received(data, addr)` — parse, then validate**
1. `msg = SensorMessage(); msg.ParseFromString(data)` inside `try/except` — **silently drop** anything that doesn't parse. The port is a firehose of arbitrary LAN traffic; never log-spam or raise. (Proto2 `required` fields make random bytes fail to parse most of the time, which is a useful natural filter.)
2. Drop unless `msg.HasField("SensorData")` **and** `Command in {SENSORDATA, SENSORPAIR}`. The HasField check matters: `SensorData` is optional, and a message carrying only a Command still parses — reading `msg.SensorData.Info` on it returns a default INFO with `Mac=""`, which would create an empty-mac ghost device. Nothing raises without the explicit check.
3. **Validate & normalize the mac**: lowercase, strip `:`/`-` separators, require exactly 12 hex chars — else drop. The emulator emits 12 bare lowercase hex chars; real hardware format is unconfirmed (§12), hence normalize rather than exact-match. The normalized mac is the roster key and unique_id seed — unique_ids are forever, so nothing unvalidated gets one.
4. **Local-emulator filter** (only when the `ignore_local_emulated` option is on, §2.10): drop the packet if `mac[:10]` is in `{e.data["mac_prefix"].lower() for e in hass.config_entries.async_entries(EMITTER_DOMAIN) if e.data.get("mac_prefix")}`. Recompute that set inline per packet — it reads one domain's entry list and does a set-membership check; do **not** add caching or entry-update listeners for it. Count drops in the diagnostics counters (§6j).
5. **Validate the temperature index**: `Temperature` is a `uint32`, so the wire can carry anything; a value of 300 would decode to 110 °C. Values > 253 that aren't the fault sentinels (254/255) → drop the packet.
6. Build a `DecodedReading` (below); hand to `DeviceManager.handle(reading)`.

**Decode helpers**
- **Index → temperature (exact inverse of the emulator's `get_temperature_index`):**
  `celsius = index / 2 − 40` → `0 → −40.0 °C`, `80 → 0.0`, `124 → 22.0`, `253 → 86.5`.
  Native unit is **°C**; report that and let HA convert to the user's display unit.
  *Caveat to document:* this is exactly what the **thermostat** sees (0.5 °C resolution). The original Fahrenheit source reading is **not** recoverable — the forward map rounds °F → whole degrees before converting — which is expected and fine: the point of a listener is "what's actually on the wire."
- **Absent `Temperature` field → index 0 → −40.0 °C, deliberately.** The emulator *omits* the field at index 0 to stay byte-identical with the C# serializer, so absent genuinely means −40.0 °C. This is intentionally **asymmetric** with the humidity/battery handling below — do not "clean it up" into consistency.
- **Absent `Battery` / `Humidity` → `None`, never 0.** Proto2 reads an absent optional uint32 as `0`; the emulator never sets Humidity at all, so reading the field directly would show every emulated sensor at 0 % humidity. Gate on `HasField` — absent means "sensor doesn't report this," which also drives capability discovery (§6d).
- **Absent/unknown `Type` → `Remote`** via `HasField("Type")` (§2.6; an unrecognized enum value on the wire also reads as absent in proto2).
- **Absent/empty `Name` → fallback** `"Venstar {mac[-4:]}"` so the device always has a usable display name.
- **Fault sentinels:** index `254` = shorted sensor, `255` = open sensor. Never emitted by our code but possible from real hardware → the temperature entity stays **available** (packets are still arriving) but reports `native_value = None` (state `unknown`), with the fault reason as a `fault` attribute. Do **not** mark it unavailable: HA drops `extra_state_attributes` from unavailable entities, so the fault reason would be invisible — and unavailable is reserved for "stopped transmitting" (§6e), a different failure worth telling apart.
- **`received_at`:** `homeassistant.util.dt.utcnow()` (tz-aware) — used for all staleness math.

**`DecodedReading` (dataclass)**
`mac` (normalized), `command`, `sensor_id`, `name`, `purpose`, `temp_c | None`, `fault | None`, `battery | None`, `humidity | None`, `sequence`, `power`, `fw_major/minor`, `source_ip` (`addr[0]`), `received_at`.

**`DeviceManager`**
- Holds `roster: dict[mac → DiscoveredDevice]`; the roster is the **single source of truth for `last_seen`** — entities read it via their device reference rather than keeping copies, so availability stays consistent across the timer, the `available` property, and restarts.
- **Dedup first**: if `reading.sequence` equals the last-seen sequence for this mac → update `last_seen`, stop (no dispatch, no save). See §4.
- New mac → add to roster, persist immediately, `async_dispatcher_send(SIGNAL_NEW_DEVICE, reading)`.
- Known mac → update `last_seen`/values; if a capability just appeared (first non-None battery/humidity) set the roster flag, persist immediately, and re-fire `SIGNAL_NEW_DEVICE` (the platform's created-set makes this idempotent, §6d); if `name` changed, update the device registry (§2.8); then `async_dispatcher_send(SIGNAL_UPDATE.format(mac), reading)` with a debounced roster write.

### 6d. `sensor.py` — dynamic discovery + entities

Standard HA push-discovery pattern, with one ordering rule and one guard:

- **Subscribe before scanning.** `async_setup_entry` connects to `SIGNAL_NEW_DEVICE` *first*, then iterates the restored roster creating entities. Combined with starting the listener *after* platform forward (§6h), no discovery can fall between the cracks.
- **Created-set guard.** The platform keeps `created: set[(mac, kind)]`. Every `SIGNAL_NEW_DEVICE` handler run creates exactly the entities implied by the device's current capability flags that aren't in the set. This single mechanism serves triple duty: dedupes roster-restore vs. live discovery, makes capability re-fires idempotent, and adds only the missing entity when humidity appears later on a known device.
- Each entity subscribes to `SIGNAL_UPDATE.format(mac)` and writes state via `async_write_ha_state`.
- Entities use **`RestoreSensor`** (not plain `RestoreEntity`): `async_get_last_sensor_data()` restores the *native* value + native unit. `RestoreEntity` restores the display state string, which round-trips badly through unit conversion.
- `_attr_has_entity_name = True` with device-class / translation-key names (`strings.json` + `translations/en.json` hold the entity name keys and config-flow text), so entities render as "Bedroom Temperature" etc.

**Device** (one per physical sensor):
- `identifiers = {(DOMAIN, mac)}`, `name = packet Name` (fallback §6c), `manufacturer = "Venstar"`, `model = "ACC-TSENWIFI / ACC-TSENWIFIPRO"` — the wire's own model field says only `TEMPSENSOR`, so the two parts cannot be told apart from packets.

**Entities** (unique_id `{mac}_{kind}`):

| Entity | device_class | unit | category | created | enabled | source field |
|---|---|---|---|---|---|---|
| Temperature | `temperature` | °C (native) | — | always | ✅ | `INFO.Temperature` (index) |
| Battery | `battery` | % | diagnostic | first packet with `HasField(Battery)` | ✅ | `INFO.Battery` |
| Humidity | `humidity` | % | — | first packet with `HasField(Humidity)` | ✅ | `INFO.Humidity` |

State class `measurement` on all three.

**Attributes on the temperature entity** — deliberately only the *stable* fields: `sensor_id`, `purpose`, `power_source`, `firmware`, `source_ip`, `raw_index`, and `fault` when applicable. **`sequence` and `last_seen` are intentionally excluded**: they change on every reading, and any attribute change defeats HA's "same state, no recorder write" optimization — including them means a recorder row per sensor per minute even when the temperature never moves. They're exposed through `diagnostics.py` (§6j) instead.

### 6e. Availability / staleness
Mirror the thermostat's error timing: **Outdoor → unavailable after 20 min**, everything else → **5 min**, keyed off the decoded purpose. Unavailable means exactly "stopped transmitting" — a faulted-but-still-transmitting sensor stays available (§6c fault sentinels). One rule, three enforcement points, all reading the same roster `last_seen`:
- Each entity's `available` property: `now − last_seen < threshold`.
- A staleness sweep: `async_track_time_interval` (~60 s, registered in `__init__.py`, §6h) dispatches the **payload-less `SIGNAL_AVAILABILITY`**; every entity subscribes, re-evaluates `available`, and calls `async_write_ha_state` only if availability differs from the last value it wrote (each entity caches that one bool). The dispatcher hop is what lets the timer live in `__init__.py` without holding entity references, and the flip check keeps the sweep from re-writing unchanged state every minute. (A time-based `available` property alone never re-renders — the sweep is what makes stale sensors actually *show* unavailable.)
- **After a restart, the same rule applies to the *persisted* `last_seen`**: recently-seen sensors come back available showing their `RestoreSensor` value; sensors already past their threshold come back unavailable. No special restart state.

### 6f. `storage.py` — roster persistence
Persist the discovered-device roster via the HA Store API, mirroring the emulator's `storage.py` (including its debounce rationale — SD-card wear). Per mac: `name`, `purpose`, `sensor_id`, `has_battery`, `has_humidity`, `fw`, `last_seen`.
- **Immediate save**: new device, capability flag change, rename, device deletion.
- **Debounced save** (`async_delay_save`, ~10 s): routine `last_seen` updates — never hit disk per packet.
- **Unload flushes immediately**: `async_unload_entry` does a final *immediate* save so a pending debounced write from the old instance can't race a freshly reloaded entry.

### 6g. `config_flow.py` — minimal
- `async_step_user`: single confirm step; `single_config_entry: true` (abort on second instance).
- **Options flow:** `port` (default 5001) and `ignore_local_emulated` (default off, §2.10) — the general MAC filter stays gone (§2.9). Register an update listener via `entry.async_on_unload(entry.add_update_listener(...))`; the listener reloads the entry to rebind and re-apply the filter. A rebind failure surfaces as `ConfigEntryNotReady` and retries, same as first setup (§6c).
- Bind address is deliberately **not** an option: only `0.0.0.0` receives limited-broadcast datagrams (see const.py note).

### 6h. `__init__.py` — wiring
`async_setup_entry`, **in this order**: load roster → `async_forward_entry_setups(entry, ["sensor"])` → start listener (bind + `create_datagram_endpoint`) → register the staleness timer (dispatches `SIGNAL_AVAILABILITY`, §6e). Platforms *must* be forwarded before the socket opens: a packet that arrives before `sensor.py` subscribes would fire `SIGNAL_NEW_DEVICE` into the void while still landing in the roster, leaving that device permanently entity-less until reload (its later packets all take the known-mac path).

`entry.runtime_data` holds the `DeviceManager`, transport, and unsub callbacks (same style as the emulator).

`async_unload_entry`: cancel timer → `transport.close()` (stop packets first) → unload the sensor platform → immediate roster save (§6f).

`async_remove_config_entry_device`: return `True` and purge the mac from the roster (immediate save). HA then removes the device and its entities. Rediscovery-if-still-transmitting is documented behavior (§2.7).

### 6i. `manifest.json`
```json
{
  "domain": "venstar_acc_tsenwifi_listener",
  "name": "Venstar ACC-TSENWIFI Listener",
  "codeowners": ["@mlfreeman2"],
  "config_flow": true,
  "documentation": "https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener",
  "integration_type": "hub",
  "iot_class": "local_push",
  "issue_tracker": "https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener/issues",
  "requirements": ["protobuf>=6.31.1,<8"],
  "single_config_entry": true,
  "version": "0.1.0"
}
```
`integration_type: hub` fits a component that discovers and produces multiple devices. `iot_class: local_push` matches the passive-receive model. `hacs.json` at the repo root pins the minimum HA version (`2025.7.1`, matching the emulator — that's the protobuf-6.31 floor; `single_config_entry` needs only ≥ 2024.3).

**Why `protobuf>=6.31.1,<8` — the bound is `<8`, not `<7`, on purpose.** Protobuf majors are *not* the usual "every major is a breaking risk" situation: the project publishes a [cross-version runtime guarantee](https://protobuf.dev/support/cross-version-runtime-guarantee/) that a runtime at major N supports generated code from majors N **and** N−1. Our vendored gencode is 6.x-era (protoc 31.1 ↔ Python runtime 6.31.x), so it is *guaranteed* to work on every 7.x runtime; 8.0 is the first release allowed to drop it. `<8` is therefore the widest bound that stays inside the written guarantee. Tightening to `<7` would buy no safety the guarantee doesn't already provide, and it has a real cost: HA core pins protobuf in its package constraints, and once HA's pin moves into 7.x a `<7` requirement becomes unresolvable — the integration fails to load about a year early for nothing.

*Maintenance rhythm:* when HA's protobuf pin enters 7.x — nothing to do. Before it enters 8.x, regenerate the vendored `sensor_message_pb2.py` with a 7.x-era protoc and move the bound to `<9`. The emulator's manifest already carries the same `<8` bound and rhythm (applied in its 0.3.0 during the repo split).

### 6j. `diagnostics.py`
Config-entry diagnostics dump: the roster (per mac: name, purpose, capabilities, `last_seen`, last `sequence`, last reading, `source_ip` — the last three are in-memory only, deliberately not persisted (§6f), so after a restart they read empty until the next packet), plus listener counters (packets parsed / dropped-unparseable / dropped-invalid / dropped-filtered (§2.10) / deduped). This is where the churn-prone fields excluded from entity attributes (§6d) live, and it's the first thing to ask for in a "why isn't my sensor showing up" issue.

## 7. Edge cases & gotchas

- **Self-echo when the emulator runs on the same host.** The emulator broadcasts to `255.255.255.255:5001`; a listener bound to `5001` on the same host **will receive the emulator's own packets** (Linux delivers broadcast to local listeners), so emulated sensors appear here too — which is exactly how this component gets tested. For mixed emulated + physical fleets, the `ignore_local_emulated` option (§2.10) suppresses the co-installed emulator's sensors automatically — and makes deleting their devices stick. Otherwise disable the entities or delete the device and disable the emulated sensor. There is **no bind conflict** — the emulator never binds 5001, it only sends to it.
- **5× repeats / duplicate suppression.** Every broadcast arrives ~5 times; dedup on `sequence` per mac. The pairing→data sequence-1 collision is a known, accepted loss (§4) — the pairing packet already delivered the same reading.
- **`network_mode: host`.** HA must be on the thermostat's VLAN/broadcast domain, and dockerized HA needs host networking to receive broadcasts. Document it (same requirement as the emulator).
- **Never raise/log-spam on bad packets.** The port sees arbitrary LAN traffic; parse failures must be swallowed quietly. Count them in diagnostics instead.
- **Parsed ≠ valid.** A packet can parse yet be garbage — hence the HasField(SensorData), mac-shape, and index-range gates in §6c. Anything that survives them gets a permanent unique_id, so the gates are the last line of defense.
- **Proto2's silent defaults.** Absent `Type` reads as `OUTDOOR`, absent `Humidity` reads as `0` — both wrong for us. Every optional field goes through `HasField` except `Temperature`, whose absent-means-index-0 is intentional emulator parity (§6c).
- **Blocking-import warning.** Import `sensor_message_pb2` at module top (executor time), never lazily on the loop.
- **Startup ordering.** Platforms before socket (§6h); subscribe before roster scan (§6d). Both are one-line ordering decisions that prevent permanently entity-less devices.
- **Recorder churn.** No per-packet-changing attributes on entities (§6d).
- **Lossy Fahrenheit round-trip.** See §6c — report native °C as truth; do not attempt to reconstruct the source °F.

## 8. Testing (pytest-homeassistant-custom-component)

**There is no Python test infrastructure in this repo yet — bootstrap it from scratch; don't hunt for precedent.** (The emulator was hand-verified with `pytest-homeassistant-custom-component` during development, but that harness was never checked in there either.) Tests live in `tests/` at the repo root (§3):

- `requirements-test.txt`: `pytest`, `pytest-asyncio`, `pytest-homeassistant-custom-component`, and `protobuf>=6.31.1,<8` (manifest requirements are **not** auto-installed in tests, and the vendored gencode needs the runtime). Pin `pytest-homeassistant-custom-component` to a release matching the HA version you develop against — its versions track HA releases; anything at or above the 2025.7 floor works.
- `conftest.py`: the standard `auto_enable_custom_integrations` autouse fixture, plus whatever `sys.path` setup makes `custom_components/venstar_acc_tsenwifi_listener` importable.
- Run from the repo root with `python -m pytest tests/`.

Physical hardware is unavailable, and the emulator lives in its own repository — so **the checked-in golden fixtures are the sole cross-implementation parity mechanism** (there is deliberately no in-process emulator round-trip test and no cross-repo import):

- **Golden C# fixtures — already checked in** at [`tests/fixtures/csharp_golden_packets.json`](tests/fixtures/csharp_golden_packets.json): five packets serialized by the actual C# app (both scales, all four purposes, data + pairing, the omitted-Temperature −40 °C edge, index 253 max), each with its expected decode values alongside; wire-format-verified at generation time. Decode must recover every `expected` field, including `temperature_field_present` via `HasField`. The same file is duplicated in the emulator repo and the C# repo (whose test suite generates it), pinning all three implementations to identical bytes — the protocol is frozen, so the copies cannot legitimately diverge. If the protocol ever changes, that's a *new* protocol with new components; regeneration of this file (see `_meta.regenerate` inside it) is only for fixing generation mistakes.
- Fault sentinels (254/255) → temperature state `unknown` (native_value `None`) with `fault` attribute, entity still **available** (§6c); absent-temperature field → −40.0 °C.
- Index > 253 non-sentinel (e.g. 300) → packet dropped.
- Malformed / non-Venstar / wrong-command packets → silently ignored, no entity created.
- Command-only packet (no `SensorData` field) → ignored; no empty-mac ghost device.
- Mac normalization: uppercase / colon-separated forms map to the same device; non-mac strings dropped.
- Local-emulator filter (§2.10): with a mock `venstar_acc_tsenwifi_emulator` config entry present (`entry.data["mac_prefix"]` set) and the option **on** → matching-prefix packets dropped (counted in diagnostics), no device created, and a deleted emulated device is *not* rediscovered; option **off** (default) → the same packets create devices normally; non-matching macs unaffected either way.
- Dedup: 5 identical-sequence packets → one dispatch; `last_seen` still refreshed; pairing (seq 1) followed by data (seq 1) → data packet deduped (accepted by design, §4), next data packet (seq 2) dispatched.
- Dynamic discovery: unseen mac → new Device + entities; repeat packet → state update, no duplicate (created-set).
- Capability discovery: packet without Humidity → no humidity entity; later packet with Humidity → entity appears, flag persisted, survives restart; same for Battery. Absent fields decode as `None`, never 0.
- Absent `Type` → purpose Remote, 5-min staleness (not proto2's OUTDOOR default).
- Staleness → unavailable after the per-purpose threshold; recovery on resume; restored `last_seen` past threshold → starts unavailable, recent → starts available with `RestoreSensor` value.
- Rename: changed packet Name updates device name; a `name_by_user` set in HA is preserved.
- Device deletion: `async_remove_config_entry_device` purges roster; next packet rediscovers.
- Bind failure (port already held) → `ConfigEntryNotReady`, retry succeeds once free.
- Config/options flow: setup, second-instance abort, port change reloads and rebinds cleanly, toggling `ignore_local_emulated` takes effect after the reload.
- Restart: roster restores entities, values via `RestoreSensor`, availability per restored `last_seen`.

## 9. Docs

`README.md` + `INSTALL.md` at the repo root. A status-stub README is already checked in (pre-implementation); **rewrite it for the physical-sensor owner when the component ships**: what it does, HACS custom-repo install, host-networking requirement, how discovery/deletion/staleness behave, the `ignore_local_emulated` option for mixed fleets (§2.10) — with the emulator/C# app mentioned only as related projects, and both sensor part numbers (ACC-TSENWIFI and the discontinued ACC-TSENWIFIPRO) named for searchability. Keep the "Beta / untested against hardware" framing the emulator uses (extra honest here: §12 hardware unknowns). The C# repo's README hosts the family-level "which piece do I need?" overview; this repo just cross-links.

## 10. Distribution (HACS)

HACS is **hard-limited to one integration per repository** — it resolves a repo to a single `custom_components/<domain>/`. That is why this repository exists: it is the listener's dedicated home *and* its HACS distribution repo, with `custom_components/venstar_acc_tsenwifi_listener/` at the root. (The emulator has the identical arrangement at [`mlfreeman2/venstar-acc-tsenwifi-emulator`](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator).)

- Users with physical sensors add this repo as a **HACS custom repository** and never touch the rest of the project family.
- **Tag a GitHub release per version** so HACS offers pinned versions instead of tracking the default branch.
- Manual install (copy `custom_components/venstar_acc_tsenwifi_listener/`) works too — state both paths in INSTALL.md.

## 11. Phasing (each phase independently demoable)

1. **Skeleton + vendored protobuf + listener that logs decoded readings** (debug level) — prove packets decode end-to-end, including the §6c validation gates and dedup.
2. **Sensor platform + dynamic discovery + device model** — temperature only. First "watch real packets become HA entities."
3. **Capability-discovered battery/humidity + diagnostic attributes + staleness/availability + rename handling.**
4. **Roster persistence + restart restore + device deletion.**
5. **Config/options flow polish (port, `ignore_local_emulated` §2.10) + `diagnostics.py`.**
6. **Tests + docs** (the pytest harness is bootstrapped from scratch, §8; the golden C# fixtures are already checked in). CI (pytest + hassfest + the HACS validation action) can land as early as phase 1 — it's absent today only because hassfest fails on an empty component skeleton.
7. **First release** (§10): tag v0.1.0, verify HACS custom-repo install end-to-end.
8. **Capture export — committed fast-follow, deliberately not in v0.1.0:** raw-packet capture export using the same `venstar-protobuf-capture/1` file format as the C# listener page (§12 last bullet). Scheduled after first release so v0.1.0 ships sooner; it becomes urgent the moment the first physical-sensor owner files an issue, because it is the main channel for real hardware bytes to reach the maintainer.

## 12. Open / hardware unknowns

Everything below is unconfirmable without physical hardware; the decode path is deliberately liberal about all of it:

- Does real hardware ever populate `Humidity`? (T8900 ignores it either way. Dynamic creation §2.3 means nothing breaks in either case — if it appears, the entity just shows up.)
- Wire format of `Mac` from real hardware (separators? case?) — normalization §6c covers the plausible variants.
- Real `Battery` values (the emulator hardcodes 100).
- Whether real hardware repeats each packet 5× and how its sequence numbers behave across reboots (dedup only compares against the immediately-previous `sequence`, so a reboot-reset colliding once self-heals on the next packet).
- Name encoding/length limits on the wire (emulator enforces 14 chars; hardware unverified).
- Optional: surface pairing packets (`SENSORPAIR`) as a discovery event or log, useful for debugging thermostat pairing.
- Optional future convergence with the C# app's own listener tool — planned separately in the C# repo's [`PROTOBUF_LISTENER_PLAN.md`](https://github.com/mlfreeman2/venstartranslator/blob/main/docs/archive/PROTOBUF_LISTENER_PLAN.md) — shared protocol understanding, different runtimes.
- Raw-packet capture export from HA using the same `venstar-protobuf-capture/1` file format as the C# listener page (its §4f) — lets a physical-sensor owner attach exactly what their network saw to a bug report, importable by the maintainer's C# app. With the repos now independent, this is the main channel for real ACC-TSENWIFI(PRO) bytes to reach the maintainer — committed as a post-v0.1.0 fast-follow (§11 phase 8), not optional.
