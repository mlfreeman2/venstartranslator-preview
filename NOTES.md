# Project Notes

Context, decisions, and gotchas behind [TODO.md](TODO.md). State as of 2026-07-11, after implementations and the closing pass.

## Current state

The two HACS subfolders under `hacs/` are fully laid out as ready-to-push repo roots and **both features are implemented**: the HACS listener (phases 1–6 + CI + a 67-test pytest suite) and the C# protobuf listener page + build identity (`/api/version`, stamped Docker builds). The closing pass ([docs/archive/CLOSING_PASS_PLAN.md](docs/archive/CLOSING_PASS_PLAN.md)) was executed 2026-07-11 pre-split, in-monorepo: logging review across all three codebases, status-flips, `CAPTURE_FORMAT.md` (duplicated ×3), README security note, Docker `latest`/semver tags + an 85% coverage floor, `FORUM_POST_DRAFT.md`, and brands-PR prep with generated placeholder icons (`docs/brands-prep/`). Executed plan docs are archived under each repo's `docs/archive/`. Nothing has been pushed to the new GitHub repos yet; remaining work is human-only (see TODO.md).

## Naming

- Repos: `venstar-acc-tsenwifi-emulator` / `venstar-acc-tsenwifi-listener`. HA domains: `venstar_acc_tsenwifi_emulator` / `venstar_acc_tsenwifi_listener`. Display names: "Venstar ACC-TSENWIFI Emulator/Listener".
- Why not `...pro`: the **ACC-TSENWIFIPRO was discontinued by Venstar in August 2025** (per supplyhouse.com); distributors recommend the plain **ACC-TSENWIFI**, believed to speak the same protocol. `ACC-TSENWIFI` is also a textual prefix of `ACC-TSENWIFIPRO`, so searches for either part number find the docs. The wire protocol carries no per-part identity — the model field in every packet is literally `TEMPSENSOR`.
- Searchability now lives in repo **descriptions/topics/READMEs** (which name both parts), not the repo names.
- **Decided 2026-07-10:** the C# repo's public name is `venstartranslator` (chop `-preview`). All baked-in cross-repo links already use the final URL, so the rename must happen **before** the HACS repos go public — GitHub redirects old→new only; links to the final name 404 until then.

## Frozen-protocol policy

The protocol is treated as frozen. If Venstar ships a firmware update that changes the wire format, that is a **new protocol**: new HACS components, new mode in the C# app. Consequences:

- The C# app is the *only* multi-protocol candidate. Refactoring for a hypothetical second protocol stays deferred until a real one exists.
- The naming scheme generalizes: `venstar-<part-family>-<role>` per protocol.
- PROTOCOL.md, LICENSE, and the golden fixtures are **deliberately duplicated** across all three repos so each is self-contained. Frozen protocol ⇒ the copies cannot legitimately diverge.

## Golden fixtures = the parity contract

- Canonical copy: `VenstarTranslator.Tests/Fixtures/csharp_golden_packets.json` (this repo generates it; wired into the test csproj as copied content). Duplicates live in both HACS repos' `tests/fixtures/`.
- With the repos split there is **no cross-repo import or in-process parity test** — the fixtures alone pin C# encode, Python decode, and (future) C# decode to identical bytes.
- Regeneration (`_meta.regenerate` in the file) is only for fixing generation mistakes, never for "protocol changes" (those are a new protocol, above).

## Emulator 0.3.0 breaking rename

- Domain renamed `venstar_translator` → `venstar_acc_tsenwifi_emulator`; storage key and service names followed. HA treats it as a brand-new integration.
- INSTALL.md documents two migration paths. The storage-file-rename path preserves the MAC prefix (broadcasting always uses the *storage* prefix), so thermostat pairing survives — but the new config entry records a freshly generated `mac_prefix` in `entry.data` that no longer matches storage. The future listener's `ignore_local_emulated` option reads `entry.data`, so **migrated installs can't rely on that filter** (documented in both the emulator INSTALL.md and listener plan §2.10).

## HACS mechanics

- HACS is hard-limited to **one integration per repository** — the entire reason for the split. Custom-repo install needs `custom_components/<domain>/` at the repo root, `hacs.json`, and a README (all in place).
- Tag GitHub releases so HACS offers pinned versions instead of tracking the default branch.
- Some `hacs/action` CI checks read the live repo's description/topics via the GitHub API — they can only pass after upload. The workflows set `ignore: brands` because custom integrations aren't required to be in home-assistant/brands (but see below).
- Both HACS repos now carry `validate.yml` CI (hassfest + HACS action; the listener's also runs its pytest suite).

## Dev environment (both HACS repos)

- `scripts/develop` runs a local HA (port 8123) with the component symlinked in. UDP broadcasts stay inside the container's network namespace — fine for protocol dev (a listener in the same container sees them), but they won't reach a physical thermostat without host networking.
- `scripts/replay_fixtures.py` (listener repo) broadcasts the golden fixtures — or any `venstar-protobuf-capture/1` file — to UDP 5001. This makes a user's capture file attached to a bug report locally reproducible traffic, and lets the listener be developed with zero other components present. Note: fixed sequence numbers mean the listener's dedup correctly suppresses repeat rounds (refreshes `last_seen` only) — expected, not a bug.

## Listener validation plan

A disposable HA instance on the same broadcast domain as the running C# app, with the HACS emulator co-installed on that same instance.

- **Network prerequisite:** the disposable instance must actually receive broadcasts — dockerized HA needs `network_mode: host`; a NAT'd VM won't see `255.255.255.255` traffic (bridged works).
- **What live C# traffic exercises:** discovery, real-timing 5×-burst dedup, sequence progression, staleness → unavailable → recovery (toggle a sensor in the C# web UI), rename handling, device deletion + rediscovery, HA-restart roster/value restore, resend-last-packet (same-sequence → `last_seen` refresh only), pairing packets including the accepted seq-1 collision, all four purposes and both scales up to 20 sensors.
- **What it can never exercise:** fault sentinels (254/255), humidity-present, battery ≠ 100, absent `Type`, odd MAC formats — the C# app always sends battery=100, never humidity, never faults. These edges are covered only by the pytest suite/fixtures (plan §8), which is why that suite matters despite the live test.
- **`ignore_local_emulated` requires the co-installed HACS emulator** — it detects via config entries, so the C# app can't trigger it. Same instance also validates emulator/listener coexistence.
- Optional bridge: hand-craft edge-case packets (fault, humidity) into a `venstar-protobuf-capture/1` file and replay via `scripts/replay_fixtures.py` — live-path testing of the edge handling without hardware.

## Support loop / share strategy

- The listener plan's §12 hardware unknowns (real MAC formats, humidity, battery values, 5× repeats, name limits) are **unresolvable without community help** — no physical sensors on hand. The channel: issue templates + a forum post framed partly as a call for testers.
- **Issue templates are pre-staged** in all three repos' `.github/ISSUE_TEMPLATE/`: bug report + `config.yml` cross-repo redirects everywhere, a *hardware test report* on the emulator (thermostat owners), and a *physical sensor report* on the listener whose form fields directly harvest the §12 unknowns (part number, humidity entity appeared?, battery ≠ 100?, diagnostics, capture file). The "in builds that include it" caveats were dropped 2026-07-11 (the protobuf page shipped).
- **Capture files can contain cleartext Wi-Fi passwords** (a captured WIFICONFIG packet carries them — that's the wire protocol). Every place that asks users for captures must repeat this warning; the C# page plan masks the field in the UI and warns on export.
- The pre-share test that matters most: run the HACS emulator against the real thermostat here. It's the only first-party hardware validation possible and it removes the scariest README banner.
- home-assistant/brands accepts custom integrations (`custom_integrations/` folder): one PR per domain gives a real icon in the HA UI instead of the gray puzzle piece. Separate and slower: HACS default-store submission — optional, but expect "why isn't this in HACS?" as the first forum question.
- Forum > Discord for the primary post: forum posts are indexed and findable years later.

## Ordering rationale (why TODO.md is sequenced that way)

1. **The repo move no longer gates the listener implementation** (relaxed 2026-07-10 to allow parallel farm-out): the folder layout is repo-root-identical, so implementation may happen pre-move (plan §2.2) and the initial push then contains the finished component. The move must precede only release tagging and HACS-install verification.
2. **Logging review waits until both implementations land** — they add their own logging; reviewing earlier means doing it twice. Build-identity (`/api/version` etc.) ships as phase 7 / §12 of the protobuf listener plan so only one C# coding push remains; status-flip work joins the closing pass.
3. **Issue templates** need the repos to exist; the C# template needs the protobuf page to exist (it asks for capture files).
4. **Brands PR before the forum post** so the icon appears in everyone's first screenshots.

## Misc

- Emulator protobuf requirement is `>=6.31.1,<8` per protobuf's cross-version runtime guarantee (N runtime supports N and N−1 gencode). Maintenance rhythm: when HA's pin enters 8.x, regenerate gencode with 7.x-era protoc, move bound to `<9`. Same rhythm applies to the listener when built.
- Implemented/outdated docs live in `docs/archive/` per repo: the two feature plans (kept as architecture records; their in-document relative paths refer to their original locations), the UI prototype, the closing-pass plan, and the emulator's historical IMPLEMENTATION_PLAN/PROGRESS (which keep pre-rename names on purpose).
- Docker tags: `{branch}-{timestamp}` on main/beta pushes (unchanged), plus `latest` on main pushes and `v*` release tags, plus `X.Y.Z`/`X.Y` semver tags on release tags. `BUILD_VERSION` (→ `/api/version` and the UI footer) always equals the primary image tag. Coverage gate: build fails below 85% line coverage (94.4% when set).
