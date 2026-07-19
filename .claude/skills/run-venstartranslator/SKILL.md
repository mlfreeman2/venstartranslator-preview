---
name: run-venstartranslator
description: Build, run, and drive VenstarTranslator. Use when asked to start or launch the app, smoke-test it, screenshot its web UI (sensor table, JSONPath tester, Protobuf Listener), or verify a change against the running app.
---

ASP.NET Core 10 app that broadcasts Venstar sensor packets over UDP and serves a web UI on HTTP. Drive it with `.claude/skills/run-venstartranslator/driver.mjs` — a Playwright script that launches the app with throwaway state, exercises the sensor table and the Protobuf Listener (including a real UDP self-received broadcast), takes screenshots, and tears down. All paths are relative to the repo root.

## Prerequisites

.NET 10 SDK (already in the devcontainer). For the headless browser (one-time):

```bash
cd .claude/skills/run-venstartranslator
npm install
npx playwright install chromium
sudo npx playwright install-deps chromium   # apt packages; passwordless sudo works in the devcontainer
```

## Run (agent path)

```bash
node .claude/skills/run-venstartranslator/driver.mjs
```

What it does: wipes and recreates `/tmp/venstartranslator-run/` (sensors.json, SQLite DBs), starts a stub JSON data source on `:19090`, launches the app on **:18080** via `dotnet run` (cold start includes a build — the driver waits up to 2 min), creates a "Test Sensor" through the API, screenshots the sensor table, then opens the Protobuf Listener, starts a capture, fires a pairing broadcast (`GET /api/sensors/0/pair`), and confirms the listener self-receives the 5-datagram burst. Prints `PASS`/`FAIL` per check; exit 0 = all pass.

Artifacts: screenshots → `/tmp/venstartranslator-run/shots/`, app log → `/tmp/venstartranslator-run/app.log`.

To leave the app running for manual poking (curl, more browsing):

```bash
node .claude/skills/run-venstartranslator/driver.mjs --keep   # stays in foreground; Ctrl-C/kill stops app+stub
```

Useful endpoints while it's up: `GET /api/version`, `GET /api/sensors`, `GET /api/sensors/{id}/pair` (broadcasts immediately), `POST /api/protobuf-listener/start|stop`, `GET /api/protobuf-listener/messages?afterId=0&limit=500`.

## Run (human path)

```bash
cd VenstarTranslator
SensorFilePath=/tmp/venstartranslator-run/sensors.json \
Kestrel__Endpoints__Http__Url="http://*:18080" \
ConnectionStrings__Hangfire=/tmp/venstartranslator-run/hangfire.db \
ConnectionStrings__DataCache="Data Source=/tmp/venstartranslator-run/datacache.db" \
dotnet run --no-launch-profile
# → serves http://localhost:18080 ; Ctrl-C to stop. Needs an existing sensors file: echo '[]' > .../sensors.json first.
```

Without `--no-launch-profile`, launchSettings.json forces `ASPNETCORE_ENVIRONMENT=Development`, which expects `../sensors.dev.json` (not in the repo — it's gitignored).

## Test

```bash
dotnet test VenstarTranslator.sln   # 292 tests, all pass, <1 s test run after build
```

## Gotchas

- **The app rewrites its sensors file on startup and on every API change.** Never point `SensorFilePath` at a config you care about — always a throwaway copy.
- **UDP self-receive works in the devcontainer.** The Protobuf Listener (bound to 0.0.0.0:5001) hears the app's own broadcasts to 255.255.255.255:5001, so a full send→receive check needs no hardware and no LAN.
- **`/api/sensors/{id}/pair` is a GET**, not a POST (POSTing returns 405). Pairing fetches the sensor's data URL first, so the sensor URL must serve valid JSON — that's what the driver's `:19090` stub is for.
- **An `Enabled` sensor starts broadcasting on Hangfire's minute cron** — extra `SENSORDATA` bursts can appear in listener captures beyond what you triggered yourself.
- **The Protobuf Listener UI is poll-based** (30 s interval). After triggering a broadcast, click "Poll now" / call the messages endpoint rather than waiting for rows to appear.
- **Port 8080 is the app default**; the driver uses 18080 to avoid colliding with any dev instance.

## Troubleshooting

- **`browserType.launch: Host system is missing dependencies to run browsers`** — Chromium's shared libraries aren't installed. Run `sudo npx playwright install-deps chromium` (this happens on a fresh container even after `npx playwright install chromium` downloaded the browser).
- **App exits immediately / never comes up** — check `/tmp/venstartranslator-run/app.log`. Most likely `SensorFilePath` is unset or points at a missing/invalid file; the app validates sensors on startup and quits on failure.
