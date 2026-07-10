# Closing Pass — Implementation Plan

> **Status: Blocked until both feature implementations land.** Do not start this plan before (1) the C# protobuf listener page + build identity ([VenstarTranslator/PROTOBUF_LISTENER_PLAN.md](VenstarTranslator/PROTOBUF_LISTENER_PLAN.md), phases 1–7) and (2) the HACS listener integration (`LISTENER_IMPLEMENTATION_PLAN.md` in the listener repo, phases 1–6) are implemented. This plan is the cleanup/polish sweep that runs across **all three codebases** afterward.
>
> **Working set:** this repo, plus checkouts of `mlfreeman2/venstar-acc-tsenwifi-emulator` and `mlfreeman2/venstar-acc-tsenwifi-listener` (or, if the repo split hasn't happened yet, their pre-split homes under `hacs/` here — same layouts either way). Changes to the two HACS repos are separate commits/PRs in those repos.
>
> **Out of scope (human-only, do not attempt):** creating GitHub repos, pushing to them for the first time, tagging releases, setting repo descriptions/topics, submitting the brands PR, posting to the HA forum/Discord, anything requiring hardware. Where this plan produces material for those steps, it produces *drafts*.

## 1. Logging review (all three codebases)

Goal: sensible levels, useful messages, consistent phrasing for equivalent events, sane defaults. Produce a short findings list first, then apply the fixes.

**Conventions to enforce:**
- **ERROR** = an operation failed and someone should look (always include the exception/details). **WARNING** = degraded but self-healing (e.g., stale broadcasts). **INFO** = lifecycle only (startup, config changes, pairing) — *never* per-broadcast/per-packet. **DEBUG** = per-packet/per-broadcast detail, off by default.
- Equivalent events should read equivalently across implementations (e.g., a broadcast log line should carry sensor id, name, temperature, and sequence in all three).

**C# app:**
- `appsettings.json` / `appsettings.Production.json`: Default level `Information` or quieter in production; Hangfire and `Microsoft.*` categories quieted (`Warning`) so Docker logs aren't scheduler noise. No `Debug` defaults anywhere.
- Verify the §12 build-identity startup line exists and is the first useful INFO line: `VenstarTranslator {version} ({commit}) starting`.
- `BroadcastTrackingFilter`: failures at ERROR with exception; staleness warnings at WARNING; confirm the new `ProtobufCaptureService` follows suit (Start/Stop at INFO, per-datagram strictly DEBUG or not logged — the capture buffer *is* the record).
- Sweep for `Console.Write*` that should be `ILogger`.

**Emulator (HA):**
- Routine broadcast logs at DEBUG (INSTALL.md's "Verify Broadcasts" flow depends on them existing at debug); lifecycle at INFO; entity-unavailable/temperature-extraction failures at WARNING with entity id; service-call misuse (bad sensor id, no cached packet) at ERROR (current behavior) — confirm messages name the sensor.
- Convert f-string logging calls to lazy `%`-style (`_LOGGER.info("Added sensor %s: %s", id, name)`) — HA convention, and avoids formatting cost when the level is off. The codebase currently uses f-strings throughout.

**Listener (HA):**
- Verify the plan's invariants survived implementation: unparseable/invalid packets are **silently counted, never logged** (the port is a firehose); discovery/rename/deletion at INFO; per-packet handling at DEBUG at most.

**Deliverable:** fixes applied in all three codebases + a one-paragraph summary per codebase of what changed.

## 2. Status-flips and doc truth

- Remove the "Status: Planned / not yet built" banners from `PROTOBUF_LISTENER_PLAN.md` and `LISTENER_IMPLEMENTATION_PLAN.md`; retitle/annotate both as implemented design docs (keep them — they're the architecture record), or move to a `docs/` folder if the root is crowded.
- Listener repo: rewrite `README.md` for the physical-sensor owner (per its plan §9 — install, discovery/deletion/staleness behavior, `ignore_local_emulated`, both part numbers named); write `INSTALL.md` (HACS custom-repo + manual paths); remove the "not yet implemented" note from the bug-report issue template's markdown preamble.
- C# repo: update `CLAUDE.md` and `README.md` for the protobuf page (per its plan §8) and the `/api/version` endpoint.
- Issue templates: drop the "in builds that include the Protobuf Listener page" caveats from the capture-file asks (C# `bug_report.yml`, listener `hardware_report.yml`) — the feature exists now.
- `PROTOBUF_LISTENER_UI_PROTOTYPE.html`: delete or move under `docs/`, and update the plan's reference to it accordingly — the shipped page supersedes it.

## 3. Capture-format spec

Extract the `venstar-protobuf-capture/1` file format from the C# plan's §4f into a standalone `CAPTURE_FORMAT.md`: format string/versioning rule (major-version compatibility), full field list with an example, the raw-hex-only principle (re-decode on import), and the WIFICONFIG credential warning. Duplicate it into all three repos next to `PROTOCOL.md` (same frozen-contract rationale) and cross-link from both plans and from `PROTOCOL.md`'s header note.

## 4. Security-posture sentence

Add to the C# `README.md` (near the web UI/quick-start section): the web UI and API are deliberately unauthenticated and intended for trusted LANs; put a reverse proxy with auth in front if it must be reachable more widely; note that sensor `Headers` may contain API tokens and appear in `sensors.json`.

## 5. Docker release story

In `.github/workflows/docker-build.yml`: keep the `{branch}-{timestamp}` tags, and additionally tag `latest` on pushes to `main` (`type=raw,value=latest,enable={{is_default_branch}}` via `docker/metadata-action`). Add semver tags (`type=semver`) triggered by git tags `v*` so a future `v1.0.0` produces `1.0.0`/`latest` images. Confirm the `BUILD_VERSION` build-arg (plan §12a) carries the semver when built from a tag.

## 6. Share-prep drafts (drafts only — a human publishes)

- **`FORUM_POST_DRAFT.md`** (repo root, temporary): an HA-community "Share your Projects" post covering the family — skeleton is the root README's "which piece do you need?" matrix; honest per-component status (C# battle-tested; emulator byte-verified + hardware status as of writing; listener tested against emulated traffic only); explicit call for ACC-TSENWIFI(PRO) hardware owners, pointing at the listener repo's *Physical sensor report* issue template; links to all three repos.
- **Brands PR prep**: in a scratch folder, lay out `custom_integrations/venstar_acc_tsenwifi_emulator/` and `custom_integrations/venstar_acc_tsenwifi_listener/` per home-assistant/brands rules (`icon.png` 256×256, optional `icon@2x.png` 512×512). If no icon asset is provided, generate a clean placeholder (simple thermometer/broadcast glyph, flat background) and **flag it for human approval before any PR is opened**. Write out the exact fork/PR steps for the human.

## 7. Final sanity check

- All inter-repo links resolve (README family matrix, plan cross-references, issue-template redirect links, manifest `documentation`/`issue_tracker` URLs).
- The three copies of `PROTOCOL.md`, `CAPTURE_FORMAT.md`, and `tests/fixtures/csharp_golden_packets.json` are byte-identical across repos (`diff` them).
- Versions consistent: emulator manifest vs. its release tag; listener manifest vs. its release tag; `/api/version` output matches the image tag scheme.
- No remaining "Planned / not yet built" / "not yet implemented" text anywhere (grep for it).
- Both HACS repos' CI green; C# CI green with tests passing and the coverage bar held (consider flipping `fail_below_min: true` with a threshold at or just below current coverage in `docker-build.yml` — flag if coverage dropped instead of silently lowering the bar).
- Update `TODO.md`: check off everything this pass completed; whatever remains should be exactly the human-only items.
