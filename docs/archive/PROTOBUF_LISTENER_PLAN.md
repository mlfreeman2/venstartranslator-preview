# Protobuf Listener Page — Implementation Plan (implemented)

> **Archived.** This design plan is implemented; it lives in `docs/archive/` as the architecture record. In-document relative paths refer to its original location (`VenstarTranslator/`).

> **Status: Implemented (July 2026), including the §12 build-identity addendum.** Kept as the architecture record for the feature — design rationale, decode gates, capture-file behavior, and gotchas below all describe the shipped code. The user-facing docs are the "Protobuf Listener" section of the README; the capture-file format now has a standalone spec in `CAPTURE_FORMAT.md`. The feature is a diagnostic "Protobuf Listener" tool page in the C# app, analogous to the JSON Path Tester: it binds UDP 5001, captures incoming Venstar protobuf packets, decodes them, and lets the web UI batch-download what arrived (with arrival timestamps) on a polling interval.
>
> **Not to be confused with the [venstar-acc-tsenwifi-listener](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener) repo's `LISTENER_IMPLEMENTATION_PLAN.md`** — that plan is a standalone *Home Assistant integration*: an end-user product for people who own physical Venstar sensors and want them in HA. This one is a diagnostic page inside the C# app — a maintainer-facing troubleshooting tool, not a user-installable feature. If you were asked to implement "the listener plan," confirm which file you were pointed at.

## 1. Overview

The app currently only **broadcasts** on UDP 5001 (via [`UdpBroadcaster`](Services/UdpBroadcaster.cs)); it has never received. This feature adds a **receive** path exposed as a tool page, mirroring how the [JSON Path Tester](web/jsonpath.html) works: a focused single-purpose page reachable from the main UI.

**Behavior:** a Start/Stop button controls a capture session. While running, the server binds `0.0.0.0:5001`, records every datagram it receives (arrival timestamp, source, raw bytes, decoded Venstar fields), and the page polls every 30 s to pull down whatever arrived since its last poll. The buffer can be **saved to a capture file**, and a saved file **imported** for offline analysis (§4f) — built for the support workflow where someone in the wider community captures what *their* network saw and attaches it to a GitHub issue for the maintainer to open locally.

**Proof of working:** the app's own outbound broadcasts are sent to `255.255.255.255:5001`; on the same host a socket bound to `5001` receives them. So with any enabled sensor broadcasting, clicking Start and watching the app's *own* `SENSORDATA` packets show up — correctly decoded, arriving in bursts of 5 (the 5×-send) — is the acceptance signal. No external hardware required.

## 2. Scope

**In scope (this plan):**
- Bind/listen on UDP 5001, capture datagrams with arrival timestamp + source.
- Decode the **full `SensorMessage` command tree** — SENSORDATA, SENSORPAIR, SETSENSORNAME, WIFICONFIG, WIFISCANRESULTS, FIRMWARECHUNK, FIRMWARECOMPLETE, SUCCESS, FAILURE — not just the two commands this app transmits, using a **decode-only mirror model** (§4b; the emit model fabricates values for absent fields).
- Start/Stop session control; 30 s UI polling that batch-downloads new messages.
- **Save capture:** download the buffer as a `venstar-protobuf-capture/1` JSON file — raw hex + arrival metadata only (§4f).
- **Import capture:** load such a file and browse it with the full decode UI. A pure *view mode*: the live buffer and session are untouched, and packets are **re-decoded on import** by the current decoder (§4f).
- Graceful handling of non-Venstar/garbage packets: still show timestamp/source/length/hex, marked "not decodable as a Venstar message."

**Explicitly deferred (future, not now):**
- Decoding **arbitrary/unknown** protobuf (schemaless wire-format walk). The "undecodable → show raw hex" behavior below is the seam it will plug into once the Venstar page is proven. See §11.

## 3. Architecture & data flow

```
UDP :5001 ──▶ ProtobufCaptureService (singleton)
                 │  UdpClient bound to 0.0.0.0:5001 (ReuseAddress, broadcast)
                 │  receive loop: await ReceiveAsync(ct)
                 │     └─ stamp ReceivedAtUtc + source
                 │     └─ VenstarPacketDecoder.TryDecode(bytes)
                 │     └─ enqueue CapturedMessage (monotonic Id) into bounded ring buffer
                 ▼
   Controllers/ProtobufListenerController  (/api/protobuf-listener/*)
        start │ stop │ status │ messages?afterId=N │ export │ import
                 ▼
   web/protobuf.html  ── poll every 30 s (afterId cursor) ──▶ prepend rows (newest first)
                       ── Save capture ──▶ GET export (file download)
                       ── Import capture ──▶ POST import (re-decode) ──▶ view mode
```

Decoding a ~98-byte packet is microseconds, so it runs inline in the receive loop. Nothing is persisted — the buffer is in-memory only (a live diagnostic, not a log store).

## 4. Server-side components

### 4a. `Services/ProtobufCaptureService.cs` (+ `IProtobufCaptureService.cs`)
Singleton, registered in `Program.cs`. Manages the socket, the receive loop, and the ring buffer.

- **Socket:** created on **Start**, closed on **Stop** (so port 5001 is free when not diagnosing — important because the `FakeMacPrefix` trick lets users run multiple instances on one host, and a permanent bind would contend). Configure before bind:
  ```csharp
  var udp = new UdpClient();
  udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
  udp.EnableBroadcast = true;
  udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
  ```
- **Receive loop:** `Task.Run` with a `CancellationTokenSource`; `while (!ct) { var r = await udp.ReceiveAsync(ct); Capture(r.Buffer, r.RemoteEndPoint); }`. Exceptions from a closed socket on Stop are swallowed.
- **Buffer:** lock-protected ring (e.g. `List<CapturedMessage>` capped at `MaxBufferedMessages = 2000`). Each message gets a monotonic `Id` (`Interlocked.Increment`). When the cap is exceeded, drop oldest and advance `_minRetainedId` so the API can report gaps.
- **Start semantics:** starts a **fresh session** — clears the buffer. The monotonic `Id` counter is **never reset** (process-lifetime); Start's `CaptureStatus` carries the current high-water `Id` (`LastId`), which the page adopts as its cursor, so a stale cursor from a previous session can't replay or skip. Start **returns a result, it does not throw**: bind failure (port already in use) comes back as a failed status the controller maps to `409`.
- **Stop semantics:** cancels the loop and closes the socket; the buffer is **retained** so the user can still review the last capture until the next Start.
- **Configurable port:** `ProtobufListenerPort` config key, default `5001`. `0` binds an ephemeral port (tests do this); after bind, read the *actual* port back from `udp.Client.LocalEndPoint` into `CaptureStatus.Port` so callers always see the real one.
- **Cleanup:** implement `IDisposable` (cancel + close). The DI container disposes singletons at app shutdown, so no extra lifetime wiring is needed.

Public surface:
```csharp
CaptureStatus Start();                 // returns failed status on bind failure (never throws)
CaptureStatus Stop();
CaptureStatus GetStatus();
MessagesPage GetMessagesAfter(long afterId, int limit);
CaptureExport ExportCapture();         // snapshot of the buffer for download — works while stopped (§4f)
```

### 4b. Decode-only wire model + `Services/VenstarPacketDecoder.cs`

**Do not decode with the emit model.** [`ProtobufNetModel.cs`](Models/Protobuf/ProtobufNetModel.cs) initializes `Battery = 100` (and Fw/Model/Power), so a packet *without* a Battery field deserializes as battery-full — absent becomes indistinguishable from a real value; absent `Humidity` reads `0`, absent `Type` reads `0`. A diagnostic must not invent readings. And do **not** "fix" this by adding `*Specified`/`ShouldSerialize*` members to the shared model: protobuf-net consults those when *serializing* too, which would silently strip fields from outbound packets and break emitter parity.

Instead: a **decode-only mirror model** in `Models/ProtobufCapture/Wire.cs` — namespace `VenstarTranslator.Models.ProtobufCapture.Wire`, same class names and `[ProtoMember]` numbers as the emit model, covering the **whole tree** (`SensorMessage`, `SENSORDATA`, `INFO`, `SENSORNAME`, `WIFICONFIG`, `WIFISCANITEM`, `WIFISCANRESULTS`, `FIRMWARECHUNK`, `FIRMWARECOMPLETE`), but with every member nullable (`Commands? Command`, `ushort? Sequence`, `byte? Battery`, `SensorType? Type`, …), **no initializers, no `IsRequired`**. protobuf-net maps absent fields to `null` on nullable members, so `null` = "not on the wire" — the C# equivalent of the HACS listener's `HasField` discipline. Mirror data members only (skip `WIFICONFIG`'s IP helper methods).

`VenstarPacketDecoder.TryDecode(byte[] data)` — pure decode, no state:

1. `Serializer.Deserialize<Wire.SensorMessage>(new MemoryStream(data))` (protobuf-net 3.2.56) in try/catch — a throw → `decoded=false`, error recorded, raw hex still shown.
2. **Parsed ≠ Venstar.** protobuf-net returns a default instance for an *empty* buffer (no exception), tolerates unknown fields, and does not enforce proto2 `required` on read — garbage can "parse." Gate before trusting: `Command` non-null and `Enum.IsDefined`; SENSORDATA/SENSORPAIR additionally require `SensorData?.Info` non-null with a non-empty `Mac`; SETSENSORNAME requires `SensorName` non-null; WIFICONFIG / WIFISCANRESULTS / FIRMWARECHUNK / FIRMWARECOMPLETE require their body field non-null. Gate fails → `decoded=false`, "not decodable as a Venstar message." (SUCCESS/FAILURE are body-less; a defined Command alone suffices — the residual 2-byte false-positive risk is acceptable in a diagnostic.)
3. The decoded `Wire.SensorMessage` becomes the message **body** (§4c); the UI renders it as a collapsible field tree with `null` shown as "absent" (§5).
4. For SENSORDATA/SENSORPAIR, additionally project a flat **summary** for the table columns: `sensorId`, `mac`, `name`, `purpose`, `sequence`, `battery`, `humidity`, `temperatureIndex`, plus reverse temperature — `celsius = index / 2.0 − 40.0`, `fahrenheit = celsius × 9/5 + 32`; report raw index, °C, °F.
   - `index == 254` → fault `"shorted"`, temps null; `255` → fault `"open"`, temps null.
   - **Absent `Temperature` → index 0 → −40.0 °C, flagged `temperaturePresent = false`**: the emitter omits the field at exactly index 0, so absent is a *valid reading*, not an error (same rule as the HACS listener).
   - Caveat to document: this is what the thermostat sees (0.5 °C resolution); the original °F source reading isn't recoverable.

### 4c. Models / DTOs (`Models/ProtobufCapture/`)
- `CapturedMessage`: `Id (long)`, `ReceivedAtUtc (DateTime)`, `Source (string "ip:port")`, `Length (int)`, `Hex (string)`, `Decoded (bool)`, `Command (string?)`, `Summary (SensorSummary?)` — sensor commands only; drives the table columns — `Body (Wire.SensorMessage?)` — the full decoded tree for the expandable view — and `DecodeError (string?)`.
- `SensorSummary`: the flat projection from §4b step 4 (including `temperaturePresent` and `fault`).
- `CaptureStatus`: `Running (bool)`, `Port (int)` — the *actual* bound port (§4a) — `CapturedCount (int)`, `StartedAtUtc (DateTime?)`, `DroppedCount (long)`, `LastId (long)` — cursor seed returned by Start (§4a).
- `MessagesPage`: `Running (bool)`, `Messages (CapturedMessage[])`, `LastId (long)`, `DroppedBeforeId (long)`.

Enums (Command, Purpose, Power) serialize as **names** automatically — `Program.cs` already registers `StringEnumConverter` on the Newtonsoft pipeline. Serialize `Body` with nulls **included** (the default): on the wire model, `null` means "field absent," and the UI renders it exactly that way (§5).

### 4d. `Controllers/ProtobufListenerController.cs`
A dedicated controller (rather than growing [`APIController`](Controllers/APIController.cs)) — cleaner separation and friendlier to the future protocol reorg (idea #3). Constructor-injects `IProtobufCaptureService`, matching the existing DI-field style. Endpoints:

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/protobuf-listener/start` | Begin a fresh capture session. `409 MessageResponse` on bind failure. |
| POST | `/api/protobuf-listener/stop` | Stop capture, keep buffer. |
| GET | `/api/protobuf-listener/status` | Current `CaptureStatus`. |
| GET | `/api/protobuf-listener/messages?afterId={n}&limit={m}` | `MessagesPage` of messages with `Id > afterId` (cap `limit`, default 500). |
| GET | `/api/protobuf-listener/export` | Download the buffer as a capture file (§4f), `Content-Disposition: attachment`. `409` when the buffer is empty. |
| POST | `/api/protobuf-listener/import` | Decode an uploaded capture file for viewing (§4f). Pure — touches no capture state. |

Errors use `MessageResponse` with `StatusCode(...)`, consistent with the existing controller.

### 4f. Capture files — save & import

**File format** (`venstar-protobuf-capture/1`):
```json
{
  "format": "venstar-protobuf-capture/1",
  "exportedAtUtc": "2026-07-09T19:42:11Z",
  "port": 5001,
  "startedAtUtc": "2026-07-09T19:31:02Z",
  "packets": [
    { "receivedAtUtc": "2026-07-09T19:31:07.412Z", "source": "192.168.1.87:38112", "hex": "082ad202..." }
  ]
}
```

**Raw hex only — no decoded fields are persisted.** Import re-runs every packet through `VenstarPacketDecoder.TryDecode`, the *same* pipeline as live capture. That's deliberate: the bytes are ground truth (a sender's older/buggier build can't skew what the maintainer sees), decoder improvements retroactively apply to old capture files, and the files stay small. `Id`s are per-view and never exported; import assigns fresh ones.

- **Export:** filename `venstar-capture-{yyyyMMdd-HHmmss}.json`; the service snapshots the ring buffer under the lock (chronological order); works while stopped — capture → Stop → Save is the natural flow.
- **Import:** request body is the capture file. Validate the `format` major version (`venstar-protobuf-capture/1.x` accepted, anything else → `400` with a clear message); skip entries with malformed hex or missing fields, counting them. Response: `ImportResult { Messages (CapturedMessage[]), Skipped (int) }`. Guard rails: `[RequestSizeLimit]` ~20 MB and a 5 000-packet cap.
- **Privacy — capture files are shareable; treat them like logs.** They contain source IPs, sensor names, and SSIDs — and a captured `WIFICONFIG` packet contains the **Wi-Fi password in cleartext** (that's the wire protocol, not our choice). The UI masks `Password` in the tree (§5) and warns on export when the buffer holds a WIFICONFIG packet; the README section (§8) repeats the warning.

### 4e. `Program.cs` wiring
One line alongside the other singletons (around [Program.cs:61-67](Program.cs#L61)):
```csharp
builder.Services.AddSingleton<IProtobufCaptureService, ProtobufCaptureService>();
```
No startup binding, no hosted service — the socket only opens when the user clicks Start.

## 5. Client-side — `web/protobuf.html`

Mirror [`jsonpath.html`](web/jsonpath.html) conventions exactly: Bootstrap 5 + Font Awesome + Inter via CDN, `style.css`, card layout with `card-header`/`card-icon`/`card-title`/`card-actions`, vanilla `fetch()`, inline `<script>`. **A clickable prototype is checked in at [`PROTOBUF_LISTENER_UI_PROTOTYPE.html`](PROTOBUF_LISTENER_UI_PROTOTYPE.html)** — open it directly in a browser (simulated traffic seeded from the golden fixtures; save/import fully functional) and match its interaction model. Note the prototype decodes imports with an in-page demo wire decoder; the shipped page must call `POST import` instead (§4f).

- **Header:** title "Protobuf Listener", a **Start/Stop** toggle button (green ▶ / red ■), a live status line ("Listening on :5001 · started 12:04:11 · 37 captured"), a **Clear view** button, and a **Back to Sensor Config** button (`window.close()`), like the tester.
- **Poll loop:** on Start, adopt `CaptureStatus.LastId` as the cursor (§4a), then `setInterval(poll, 30000)` plus an immediate poll; `poll()` calls `GET messages?afterId={cursor}`, **prepends** new rows (newest first — it's a live scope), advances the cursor to `lastId`, and surfaces a warning banner if `droppedBeforeId > cursor` ("buffer overflowed — some messages were dropped; poll more often or reduce traffic"). A manual **Poll now** button too.
- **Table columns:** Time (local, ms), Source, Command badge, Summary (sensor rows: name · id · temp °F/°C with raw index or fault badge · battery · seq; other commands: a one-line gist — SSID count for WIFISCANRESULTS, chunk seq/type for FIRMWARECHUNK, the new name for SETSENSORNAME), Length.
- **Expanded row — collapsible field tree, not a JSON blob.** Clicking a row expands the decoded `Body` as an indented field tree: every message-typed field (`SensorData`, `Info`, `WifiScanResults[n]`, …) is a **collapse/expand node** (chevron), scalar fields render as `name: value` rows (enums by name; Mac/Signature/bytes in monospace), and **absent scalar optionals render as a muted "absent"** — field presence is the whole point of the wire model (§4b); show it, don't hide it. (One deliberate exception: at the `SensorMessage` level the command bodies are oneof-like alternatives, so *absent message-typed siblings are omitted* rather than listed — a SENSORDATA packet shouldn't carry five "WifiConfig: absent" noise rows.) Enrich `Info.Temperature` in place (`124 → 22.0 °C / 71.6 °F`). Default state: all nodes expanded (these packets are small); the collapse control earns its keep on WIFISCANRESULTS-sized bodies. The raw spaced hex sits below the tree in the same panel. Undecodable rows expand to hex + the decode error, tagged "not a Venstar message."
- **Optional toggle:** "Collapse duplicates" (group by mac+command+sequence, show a ×N count) — off by default, because seeing the 5 identical copies confirms the 5×-send behavior.
- **Save / Import (§4f):** a **Save capture** button (disabled when the buffer is empty or while viewing an import) triggers the `export` download, warning first if a WIFICONFIG packet is in the buffer; **Import capture…** opens a file picker, posts the file to `import`, and switches the table to a **view mode** with a provenance banner ("Viewing imported capture — *file* · N packets · k skipped · Return to live view"). Polling pauses in view mode; Start exits it and begins a fresh live session. `WIFICONFIG.Password` renders masked in the tree ("`••••••` — present in raw hex/export").
- **Entry point:** add a header button in [`index.html`](web/index.html) next to the existing JSON Path Tester button:
  `onclick="window.open('./protobuf.html', '_blank')"`.

## 6. Key behaviors & gotchas

- **Self-receive is the feature.** The broadcaster sends *to* 5001 from an ephemeral port and never binds it, so there's no conflict with a listener bound *on* 5001; Linux delivers the host's own broadcasts to that listener. This is exactly what proves the page works.
- **5 copies per broadcast.** The emitter sends each packet 5× — expect bursts of 5 identical decodes (same sequence). Honest to show; "collapse duplicates" is opt-in.
- **Bind conflict vs. coexistence.** If 5001 is held by a socket *without* address reuse, Start returns a clear `409`. But both this service and the HACS listener (`venstar_acc_tsenwifi_listener`) set `ReuseAddress`/`SO_REUSEADDR`, so a Home Assistant instance listening on the same host is **not** a conflict — on Linux both sockets receive every broadcast simultaneously. That's the expected dev setup; don't "fix" it. Multiple *capture sessions* on one host should still be avoided (run one at a time).
- **Requires broadcast reachability.** Same constraint as the rest of the app: host networking / same broadcast domain. In Docker this means `network_mode: host` (already required for broadcasting).
- **In-memory only (until saved).** No automatic persistence, capped buffer — a live scope, not a logger. Restart/stop-start loses history by design; **Save capture** (§4f) is the explicit way to keep or share a session.
- **Capture files may carry secrets.** See the §4f privacy note (WIFICONFIG = cleartext Wi-Fi credentials) before telling a user "just attach the capture."
- **Thread-safety.** Receive loop writes under lock; the controller reads a snapshot under the same lock. protobuf-net `Serializer.Deserialize` is safe to call from the loop.

## 7. Testing (VenstarTranslator.Tests, keep the 96 % bar)

- **Decoder parity (mirrors the HACS harness):** feed [`TranslatedVenstarSensor.BuildDataPacket`](Models/Db/TranslatedVenstarSensor.cs) / `BuildPairingPacket` output straight into `VenstarPacketDecoder` and assert every projected field round-trips, including `index → °C/°F` across a temperature sweep of both scales and both commands.
- **Golden fixtures (duplicated across the three repos):** decode every packet in [`VenstarTranslator.Tests/Fixtures/csharp_golden_packets.json`](../VenstarTranslator.Tests/Fixtures/csharp_golden_packets.json) — this repo generates the canonical copy (it's already included in the test project as copied content); the emulator and listener repos vendor duplicates — and assert the `expected` block: `temperature_field_present` maps to the wire model's `Temperature == null`. One fixture set then pins C# encode, Python decode, *and* C# decode.
- **Presence honesty:** a hand-built INFO without Battery/Humidity → wire model decodes `null`, never `100`/`0` (the emit-model trap §4b exists to avoid); absent `Type` → `null`, not `OUTDOOR`.
- **Validity gate:** empty buffer → `decoded=false` (protobuf-net parses it into a default instance without throwing — the gate must catch it); defined command with missing body → `decoded=false`; undefined `Command` value → `decoded=false`.
- **Full-tree decode:** build a `WIFISCANRESULTS` (a few items) and a `FIRMWARECHUNK` with the emit model, decode with the wire model, assert the tree round-trips.
- **Fault/edge decode:** index 254 → shorted, 255 → open, absent temperature → −40.0 °C with `temperaturePresent=false`; garbage bytes → `decoded=false` with hex preserved.
- **Buffer/cursor logic (pure, no socket):** monotonic ids; `afterId` paging returns only newer messages; cap eviction advances `DroppedBeforeId`.
- **Socket integration:** construct the service on an **ephemeral port** (via the configurable port), send a loopback datagram to it, assert one `CapturedMessage` with a sane timestamp/source; Stop closes the socket; Start clears the buffer.
- **Controller:** start/stop/status/messages happy paths + `409` on bind failure (bind the port first, then Start).
- **Capture files (§4f):** export → import round-trips to identical decode results (same summaries and bodies, fresh ids); import rejects an unknown `format` major with a clear `400`; malformed hex entries are skipped and counted in `Skipped`; the packet-count cap rejects oversize files; import leaves `CaptureStatus` and the live buffer untouched; export while empty → `409`.

## 8. Docs

- Update [CLAUDE.md](../CLAUDE.md): note the new receive path, the `ProtobufCaptureService`, the `/api/protobuf-listener/*` endpoints, and `web/protobuf.html`.
- Add a short "Protobuf Listener" section to [README.md](../README.md) describing the diagnostic page and the self-receive proof.

## 9. Phasing (each independently demoable)

1. **Wire model + decoder + unit tests** — `Wire.cs` mirror model and `VenstarPacketDecoder`, proven against the emitter's own packet builders and the golden fixtures. No UI yet.
2. **Capture service + DI** — bind/receive/buffer/cursor, socket integration test.
3. **Controller endpoints** — start/stop/status/messages.
4. **`protobuf.html` + index.html entry point** — Start, 30 s poll, table, expandable field tree + hex (per the prototype, §5). First end-to-end "watch the app's own packets."
5. **Save/import capture (§4f)** — export endpoint + file format, import decode endpoint, view mode + provenance banner. Demo: capture, save, clear, re-import.
6. **Polish** — dropped-buffer banner, collapse-duplicates toggle, fault badges, WIFICONFIG export warning, docs.
7. **Build identity (§12)** — version/commit stamped at build, `/api/version`, startup log line, UI footer. Fully independent of the listener page; bundled here only so a single C# coding push remains.

## 10. Open questions

- Buffer cap (2000) and default page limit (500) — tune after seeing real traffic volume.
- Optional nice-to-have: recompute the HMAC (key = SHA-256 of the MAC, which is in the packet) and show a "signature valid ✓" badge for the app's own data packets — strong integrity proof, cheap to add, but not core. Flagged, not committed.

## 11. Future: arbitrary protobuf (deferred)

Once the Venstar page is proven, unknown packets (today shown as hex + "not a Venstar message") become the input to a schemaless decoder: walk the protobuf wire format (field number + wire type → best-guess rendering of varints, length-delimited blobs, nested messages). protobuf-net has no schemaless mode, so this is a self-contained ~couple-hundred-line wire walker — a separate effort to pick up after this lands.

## 12. Addendum — build identity (same coding push, not part of the listener page)

Unrelated to packet capture, but bundled into this plan so **exactly one C# coding push remains**. Goal: "what build are you on?" is answerable in every issue report — via API, via the UI, and via the first line of the Docker logs.

### 12a. Stamping the build

The Docker image is the unit of identity, and its tag is `{branch}-{timestamp}` (from [docker-build.yml](../.github/workflows/docker-build.yml)). `.git/` is **dockerignored**, so the image build cannot derive the commit itself — both values arrive as build args:

- **csproj** ([VenstarTranslator.csproj](VenstarTranslator.csproj)): dev-safe defaults so local builds work unstamped:
  ```xml
  <PropertyGroup>
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">dev</InformationalVersion>
    <GitSha Condition="'$(GitSha)' == ''">local</GitSha>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyMetadata Include="GitSha" Value="$(GitSha)" />
  </ItemGroup>
  ```
  (Note: on a local git checkout the SDK may append `+<sha>` to InformationalVersion via SourceLink's `SourceRevisionId`; harmless — or set `IncludeSourceRevisionInInformationalVersion=false` to keep `dev` clean.)
- **Dockerfile**: `ARG BUILD_VERSION=dev` / `ARG GIT_SHA=local` in the build stage; publish becomes `dotnet publish ... -p:InformationalVersion=$BUILD_VERSION -p:GitSha=$GIT_SHA`.
- **Workflow**: add `build-args` to the `docker/build-push-action` step — `BUILD_VERSION=${{ steps.branch.outputs.name }}-${{ steps.timestamp.outputs.timestamp }}` (so the reported version **equals the image tag**, which is the whole point) and `GIT_SHA=${{ github.sha }}`. The branch/timestamp steps only run on push builds; PR builds fall back to `dev`/`local` — fine.

### 12b. Runtime surface

- A small static `BuildInfo` helper: reads `AssemblyInformationalVersionAttribute` and the `GitSha` `AssemblyMetadataAttribute` once, caches, never throws (absent → `dev`/`local`).
- **`GET /api/version`** on [`APIController`](Controllers/APIController.cs) (app-level, so not the new protobuf controller) → `{ "version": "beta-202607101530", "commit": "9229162..." }`.
- **Startup log line** (Program.cs, INFO): `VenstarTranslator {version} ({commit}) starting` — makes Docker logs self-identifying; feeds the upcoming logging pass.
- **UI footer**: a muted one-liner on [index.html](web/index.html) (`VenstarTranslator {version} ({short commit})`), populated by a single `fetch('/api/version')` in [sensors.js](web/sensors.js). The new `protobuf.html` and existing `jsonpath.html` get the same footer markup.

### 12c. Tests & docs

- Tests: `BuildInfo` fallback behavior (unstamped assembly → `dev`/`local`, no throw); `GET /api/version` returns 200 with non-empty fields.
- Docs: mention the endpoint in README/CLAUDE.md; the C# issue template (see TODO.md Phase 2) should ask for `/api/version` output or a footer screenshot.
