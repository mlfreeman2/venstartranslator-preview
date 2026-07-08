# Protobuf Listener Page — Implementation Plan

> **Status: Planned / not yet built.** Design plan for a diagnostic "Protobuf Listener" tool page in the C# app, analogous to the existing JSON Path Tester. It binds UDP 5001, captures incoming Venstar protobuf packets, decodes them, and lets the web UI batch-download what arrived (with arrival timestamps) on a polling interval.

## 1. Overview

The app currently only **broadcasts** on UDP 5001 (via [`UdpBroadcaster`](VenstarTranslator/Services/UdpBroadcaster.cs)); it has never received. This feature adds a **receive** path exposed as a tool page, mirroring how the [JSON Path Tester](VenstarTranslator/web/jsonpath.html) works: a focused single-purpose page reachable from the main UI.

**Behavior:** a Start/Stop button controls a capture session. While running, the server binds `0.0.0.0:5001`, records every datagram it receives (arrival timestamp, source, raw bytes, decoded Venstar fields), and the page polls every 30 s to pull down whatever arrived since its last poll.

**Proof of working:** the app's own outbound broadcasts are sent to `255.255.255.255:5001`; on the same host a socket bound to `5001` receives them. So with any enabled sensor broadcasting, clicking Start and watching the app's *own* `SENSORDATA` packets show up — correctly decoded, arriving in bursts of 5 (the 5×-send) — is the acceptance signal. No external hardware required.

## 2. Scope

**In scope (this plan):**
- Bind/listen on UDP 5001, capture datagrams with arrival timestamp + source.
- Decode **Venstar** `SensorMessage` packets (data + pairing) using the existing protobuf-net model.
- Start/Stop session control; 30 s UI polling that batch-downloads new messages.
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
        start │ stop │ status │ messages?afterId=N
                 ▼
   web/protobuf.html  ── poll every 30 s (afterId cursor) ──▶ append rows
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
- **Start semantics:** starts a **fresh session** — clears the buffer and resets the client's effective cursor. Returns an error if the bind fails (port already in use) so the UI can surface it.
- **Stop semantics:** cancels the loop and closes the socket; the buffer is **retained** so the user can still review the last capture until the next Start.
- **Configurable port:** `ProtobufListenerPort` config key, default `5001`. Lets tests bind an ephemeral port and lets advanced users relocate it.
- **Cleanup:** implement `IDisposable` (cancel + close). The DI container disposes singletons at app shutdown, so no extra lifetime wiring is needed.

Public surface:
```csharp
CaptureStatus Start();                 // throws/returns error on bind failure
CaptureStatus Stop();
CaptureStatus GetStatus();
MessagesPage GetMessagesAfter(long afterId, int limit);
```

### 4b. `Services/VenstarPacketDecoder.cs`
Pure decode, no state. `TryDecode(byte[] data)` → `DecodedSensorMessage?` + error string.

- `Serializer.Deserialize<SensorMessage>(new MemoryStream(data))` (protobuf-net 3.2.56, same [`SensorMessage`](VenstarTranslator/Models/Protobuf/ProtobufNetModel.cs) model used to build packets — no new proto needed). Wrap in try/catch; any failure → `decoded=false`, error recorded, raw hex still shown.
- For `Command ∈ {SENSORDATA, SENSORPAIR}`, read `SensorData.Info` and project: `sensorId`, `mac`, `name`, `purpose` (from `Info.Type`), `sequence`, `power`, `model`, `fwMajor/Minor`, `battery`, `humidity`, `signature`, `temperatureIndex`.
- **Reverse temperature (inverse of the emitter's index math):**
  `celsius = index / 2.0 − 40.0`, `fahrenheit = celsius × 9/5 + 32`. Report raw index, °C, and °F.
  - `index == 254` → fault `"shorted"`, temps null. `index == 255` → fault `"open"`, temps null.
  - Absent `Temperature` field decodes to `0` → `−40.0 °C` (valid; matches the emitter omitting it at index 0).
  - Document the caveat: this is exactly what the thermostat sees (0.5 °C resolution); the original °F source reading isn't recoverable.
- Other commands (wifi/firmware/name/etc.) → decode the `Command` name and note "non-sensor command; body not projected."

### 4c. Models / DTOs (`Models/ProtobufCapture/`)
- `CapturedMessage`: `Id (long)`, `ReceivedAtUtc (DateTime)`, `Source (string "ip:port")`, `Length (int)`, `Hex (string)`, `Decoded (bool)`, `Command (string?)`, `Message (DecodedSensorMessage?)`, `DecodeError (string?)`.
- `DecodedSensorMessage`: the projected fields from §4b.
- `CaptureStatus`: `Running (bool)`, `Port (int)`, `CapturedCount (int)`, `StartedAtUtc (DateTime?)`, `DroppedCount (long)`.
- `MessagesPage`: `Running (bool)`, `Messages (CapturedMessage[])`, `LastId (long)`, `DroppedBeforeId (long)`.

Enums (Command, Purpose, Power) serialize as **names** automatically — `Program.cs` already registers `StringEnumConverter` on the Newtonsoft pipeline.

### 4d. `Controllers/ProtobufListenerController.cs`
A dedicated controller (rather than growing [`APIController`](VenstarTranslator/Controllers/APIController.cs)) — cleaner separation and friendlier to the future protocol reorg (idea #3). Constructor-injects `IProtobufCaptureService`, matching the existing DI-field style. Endpoints:

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/protobuf-listener/start` | Begin a fresh capture session. `409 MessageResponse` on bind failure. |
| POST | `/api/protobuf-listener/stop` | Stop capture, keep buffer. |
| GET | `/api/protobuf-listener/status` | Current `CaptureStatus`. |
| GET | `/api/protobuf-listener/messages?afterId={n}&limit={m}` | `MessagesPage` of messages with `Id > afterId` (cap `limit`, default 500). |

Errors use `MessageResponse` with `StatusCode(...)`, consistent with the existing controller.

### 4e. `Program.cs` wiring
One line alongside the other singletons (around [Program.cs:61-67](VenstarTranslator/Program.cs#L61)):
```csharp
builder.Services.AddSingleton<IProtobufCaptureService, ProtobufCaptureService>();
```
No startup binding, no hosted service — the socket only opens when the user clicks Start.

## 5. Client-side — `web/protobuf.html`

Mirror [`jsonpath.html`](VenstarTranslator/web/jsonpath.html) conventions exactly: Bootstrap 5 + Font Awesome + Inter via CDN, `style.css`, card layout with `card-header`/`card-icon`/`card-title`/`card-actions`, vanilla `fetch()`, inline `<script>`.

- **Header:** title "Protobuf Listener", a **Start/Stop** toggle button (green ▶ / red ■), a live status line ("Listening on :5001 · started 12:04:11 · 37 captured"), a **Clear view** button, and a **Back to Sensor Config** button (`window.close()`), like the tester.
- **Poll loop:** on Start, `setInterval(poll, 30000)` plus an immediate poll; `poll()` calls `GET messages?afterId={cursor}`, appends new rows, advances the cursor to `lastId`, and surfaces a warning banner if `droppedBeforeId > cursor` ("buffer overflowed — some messages were dropped; poll more often or reduce traffic"). A manual **Poll now** button too.
- **Table columns:** Time (local, ms), Source IP, Command, Sensor (name/id), Temp (°F / °C + raw index, or fault badge), Battery, Humidity, Seq, Length. Each row expands to show pretty-printed decoded JSON + spaced hex. Undecodable rows show Source/Length/Hex with a muted "not a Venstar message" tag.
- **Optional toggle:** "Collapse duplicates" (group by mac+command+sequence) — off by default, because seeing the 5 identical copies confirms the 5×-send behavior.
- **Entry point:** add a header button in [`index.html`](VenstarTranslator/web/index.html) next to the existing JSON Path Tester button:
  `onclick="window.open('./protobuf.html', '_blank')"`.

## 6. Key behaviors & gotchas

- **Self-receive is the feature.** The broadcaster sends *to* 5001 from an ephemeral port and never binds it, so there's no conflict with a listener bound *on* 5001; Linux delivers the host's own broadcasts to that listener. This is exactly what proves the page works.
- **5 copies per broadcast.** The emitter sends each packet 5× — expect bursts of 5 identical decodes (same sequence). Honest to show; "collapse duplicates" is opt-in.
- **Bind conflict.** If 5001 is already bound (another instance, or a stale session), Start returns a clear `409`. Multi-instance hosts should run one capture at a time.
- **Requires broadcast reachability.** Same constraint as the rest of the app: host networking / same broadcast domain. In Docker this means `network_mode: host` (already required for broadcasting).
- **In-memory only.** No persistence, capped buffer — a live scope, not a logger. Restart/stop-start loses history by design.
- **Thread-safety.** Receive loop writes under lock; the controller reads a snapshot under the same lock. protobuf-net `Serializer.Deserialize` is safe to call from the loop.

## 7. Testing (VenstarTranslator.Tests, keep the 96 % bar)

- **Decoder parity (mirrors the HACS harness):** feed [`TranslatedVenstarSensor.BuildDataPacket`](VenstarTranslator/Models/Db/TranslatedVenstarSensor.cs) / `BuildPairingPacket` output straight into `VenstarPacketDecoder` and assert every projected field round-trips, including `index → °C/°F` across a temperature sweep of both scales and both commands.
- **Fault/edge decode:** index 254 → shorted, 255 → open, absent temperature → −40.0 °C; garbage bytes → `decoded=false` with hex preserved.
- **Buffer/cursor logic (pure, no socket):** monotonic ids; `afterId` paging returns only newer messages; cap eviction advances `DroppedBeforeId`.
- **Socket integration:** construct the service on an **ephemeral port** (via the configurable port), send a loopback datagram to it, assert one `CapturedMessage` with a sane timestamp/source; Stop closes the socket; Start clears the buffer.
- **Controller:** start/stop/status/messages happy paths + `409` on bind failure (bind the port first, then Start).

## 8. Docs

- Update [CLAUDE.md](CLAUDE.md): note the new receive path, the `ProtobufCaptureService`, the `/api/protobuf-listener/*` endpoints, and `web/protobuf.html`.
- Add a short "Protobuf Listener" section to [README.md](README.md) describing the diagnostic page and the self-receive proof.

## 9. Phasing (each independently demoable)

1. **Decoder + unit tests** — `VenstarPacketDecoder`, proven against the emitter's own packet builders. No UI yet.
2. **Capture service + DI** — bind/receive/buffer/cursor, socket integration test.
3. **Controller endpoints** — start/stop/status/messages.
4. **`protobuf.html` + index.html entry point** — Start, 30 s poll, table, expand hex/JSON. First end-to-end "watch the app's own packets."
5. **Polish** — dropped-buffer banner, collapse-duplicates toggle, fault badges, docs.

## 10. Open questions

- Buffer cap (2000) and default page limit (500) — tune after seeing real traffic volume.
- Optional nice-to-have: recompute the HMAC (key = SHA-256 of the MAC, which is in the packet) and show a "signature valid ✓" badge for the app's own data packets — strong integrity proof, cheap to add, but not core. Flagged, not committed.

## 11. Future: arbitrary protobuf (deferred)

Once the Venstar page is proven, unknown packets (today shown as hex + "not a Venstar message") become the input to a schemaless decoder: walk the protobuf wire format (field number + wire type → best-guess rendering of varints, length-delimited blobs, nested messages). protobuf-net has no schemaless mode, so this is a self-contained ~couple-hundred-line wire walker — a separate effort to pick up after this lands.
