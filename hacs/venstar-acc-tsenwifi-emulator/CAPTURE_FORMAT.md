# `venstar-protobuf-capture/1` — Capture File Format

A small JSON file format for sharing raw Venstar sensor-protocol UDP traffic — typically saved from the C# VenstarTranslator app's **Protobuf Listener** page and attached to a GitHub issue, then re-opened (imported) by a maintainer. It is a cross-implementation contract: any tool in the project family may produce or consume it.

> This file is deliberately **duplicated across the three related repositories** ([VenstarTranslator](https://github.com/mlfreeman2/venstartranslator), [venstar-acc-tsenwifi-emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator), [venstar-acc-tsenwifi-listener](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener)), the same way as [PROTOCOL.md](PROTOCOL.md).

## Design principle: raw hex only

**No decoded fields are ever persisted.** A capture stores the bytes as received plus arrival metadata, nothing else. Consumers re-decode every packet on import. This is deliberate:

- The bytes are ground truth — a producer running an older or buggier decoder cannot skew what the consumer sees.
- Decoder improvements retroactively apply to old capture files.
- Files stay small.

## Structure

```json
{
  "format": "venstar-protobuf-capture/1",
  "exportedAtUtc": "2026-07-09T19:42:11Z",
  "port": 5001,
  "startedAtUtc": "2026-07-09T19:31:02Z",
  "packets": [
    {
      "receivedAtUtc": "2026-07-09T19:31:07.412Z",
      "source": "192.168.1.87:38112",
      "hex": "082ad2025a0a2a082a10001a0c..."
    }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `format` | string | Format identifier + version. **Required.** |
| `exportedAtUtc` | ISO-8601 UTC | When the file was written. |
| `port` | int | UDP port the capture socket was bound to (normally 5001). |
| `startedAtUtc` | ISO-8601 UTC or null | When the capture session began (null if unknown). |
| `packets[]` | array | Chronological order. |
| `packets[].receivedAtUtc` | ISO-8601 UTC | Arrival timestamp of the datagram. |
| `packets[].source` | string `"ip:port"` | Sender endpoint as seen by the capture socket. |
| `packets[].hex` | string | The complete raw datagram, lowercase hex, no separators. |

## Versioning

The version is **major-only compatibility**: consumers accept any `format` beginning with `venstar-protobuf-capture/1` (so a hypothetical `/1.1` still loads) and reject anything else with a clear error. Additive optional fields do not bump the major; a change that breaks the rules above does, and gets a new spec.

## Consumer rules

- Re-decode every packet through your current decoder; never trust (or expect) decoded data in the file.
- Skip entries with malformed hex or missing required fields, and report a skipped count rather than failing the whole import.
- Apply sanity caps (the C# reference implementation enforces a 20 MB request limit and a 5,000-packet maximum per file).
- Import is a *view* operation: it must not touch any live capture state.

## ⚠️ Privacy — treat capture files like logs

Capture files contain source IPs, sensor names, and SSIDs. A capture taken while a sensor was being configured for Wi-Fi can contain a **`WIFICONFIG` packet, which carries the Wi-Fi password in cleartext** — that is the wire protocol, not a choice made by these tools. Captures of ordinary temperature broadcasts contain no credentials. Producers should warn before exporting a buffer that holds a `WIFICONFIG` packet; humans should review before posting a capture publicly.

## Reference implementations

- **Producer + consumer:** the C# app's Protobuf Listener page (`GET /api/protobuf-listener/export`, `POST /api/protobuf-listener/import`).
- **Consumer (dev harness):** the listener repo's `scripts/replay_fixtures.py` re-broadcasts a capture file's packets onto UDP 5001.
- **Planned producer:** the HACS listener integration's capture export (its plan's phase 8 fast-follow).
