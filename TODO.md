# Remaining Work — Ordered Checklist

Companion context, rationale, and gotchas live in [NOTES.md](NOTES.md). Work top to bottom; the ordering is deliberate (see "Ordering rationale" in the notes).

## Delegation map

Three plan files cover everything an agent (Opus/Sonnet) can do; everything else is human-only.

| Plan file | Scope | Can start |
|---|---|---|
| [VenstarTranslator/PROTOBUF_LISTENER_PLAN.md](VenstarTranslator/PROTOBUF_LISTENER_PLAN.md) | C# protobuf listener page + build identity (phases 1–7) | **Now** |
| `LISTENER_IMPLEMENTATION_PLAN.md` (in the listener folder/repo) | HACS listener integration (phases 1–7; capture export = phase 8 fast-follow) | **Now** — pre-move implementation is explicitly fine (plan §2.2) |
| [CLOSING_PASS_PLAN.md](CLOSING_PASS_PLAN.md) | Logging review, status-flips, capture-format spec, security sentence, Docker `latest`/semver tags, forum-post + brands-PR drafts, final sanity check | **After** both implementations land |

The two implementation plans touch disjoint file sets (`VenstarTranslator/` + `VenstarTranslator.Tests/` vs. the listener folder), so they can run as parallel agent sessions.

**Human-only, no plan file:** the C# repo name decision; creating the GitHub repos, first push, descriptions/topics; release tagging; HACS-install verification; the disposable-HA live validation; the thermostat hardware test; screenshots; icon approval + brands PR submission; actually posting to the forum/Discord.

## Phase 1 — Repo split

- [ ] Rename this repo to `venstartranslator` (decided 2026-07-10: chop `-preview`) — **do this first**: all baked-in cross-repo links already use the final URL, and GitHub only redirects old→new, so links to the final name 404 until the rename happens
- [ ] Create `github.com/mlfreeman2/venstar-acc-tsenwifi-emulator`; push the contents of `hacs/venstar-acc-tsenwifi-emulator/` as the repo root
- [ ] Create `github.com/mlfreeman2/venstar-acc-tsenwifi-listener`; push the contents of `hacs/venstar-acc-tsenwifi-listener/` as the repo root
- [ ] Set description + topics on both repos — descriptions must contain both part numbers (`ACC-TSENWIFI`, `ACC-TSENWIFIPRO`); topics: `home-assistant`, `hacs`, `venstar`
- [ ] Tag **v0.3.0** release on the emulator repo so HACS offers pinned versions
- [ ] Confirm the emulator's CI (`validate.yml`: hassfest + HACS action) passes in the new repo — some HACS checks read the live repo's description/topics, so they can only pass post-upload
- [ ] Verify the pre-staged **issue templates** render correctly on all three repos (already written: bug report + hardware test report + cross-repo redirect links in each `.github/ISSUE_TEMPLATE/`)
- [ ] Verify a HACS custom-repository install of the emulator end-to-end on a test HA instance
- [ ] Delete `hacs/` from this repo and commit

## Phase 2 — Implement the two planned features (either order; may start before Phase 1 — only release/verification steps need the repos to exist)

- [ ] **C# protobuf listener page + build identity** — per [VenstarTranslator/PROTOBUF_LISTENER_PLAN.md](VenstarTranslator/PROTOBUF_LISTENER_PLAN.md), phases 1–7 (UI prototype checked in alongside it). Phase 7 is the build-identity addendum (§12: `/api/version`, startup log line, UI footer, Docker build-arg stamping) — bundled so this is the **only remaining C# coding push**
- [ ] **HACS listener integration** — per `LISTENER_IMPLEMENTATION_PLAN.md` in its new repo, phases 1–7
  - [ ] Add CI with phase 1 code (copy the emulator's `validate.yml`, add a pytest job) — absent today only because hassfest fails on an empty component skeleton
  - [x] ~~Decide where the HA-side capture export lands~~ — resolved 2026-07-10: committed post-v0.1.0 fast-follow, now phase 8 in the plan's §11
  - [ ] Live validation on a **disposable HA instance** picking up the real C# app's broadcasts, with the **HACS emulator co-installed** to exercise `ignore_local_emulated` (the C# app is invisible to that filter by design) — see "Listener validation plan" in NOTES.md for what this does and doesn't cover
- [x] ~~Issue templates in all three repos~~ — **pre-staged 2026-07-10** (bug reports, hardware test/physical-sensor reports, cross-repo redirects; WIFICONFIG password warning included on every capture ask). Capture-file asks carry an "in builds that include it" caveat — drop it in the Phase 3 status-flip pass once the protobuf page ships

## Phase 3 — Closing pass (after both implementations land) — fully delegated via [CLOSING_PASS_PLAN.md](CLOSING_PASS_PLAN.md); the items below are its summary

- [ ] **Logging review** across all three codebases: sensible levels, useful messages, consistent phrasing for equivalent events, sane defaults (no DEBUG on by default; check C# `appsettings.json` levels and both integrations' logger usage). The version-stamped startup line from the build-identity work (plan §12) is part of the expected output
- [ ] **Status-flips**: remove the "Planned / not yet built" banners from both plan docs and archive/convert them to architecture notes; rewrite the listener README for physical-sensor owners and write its INSTALL.md (plan §9); update CLAUDE.md + README for the protobuf page (its plan §8)
- [ ] Extract the `venstar-protobuf-capture/1` file format from the C# plan's §4f into a short spec doc, duplicated across repos like PROTOCOL.md (justified once two implementations exist)
- [ ] Add one deliberate sentence to the C# README on the unauthenticated LAN API ("no auth by design; reverse-proxy it if you must expose it")
- [ ] Final overall sanity check (links, versions, banners, install instructions, fixture copies identical)

## Phase 4 — Pre-share

- [ ] **Test the HACS emulator against your own thermostat** — the one hardware test you can run yourself; update or remove the "Beta / Untested" banner based on the result
- [ ] Screenshots in both HACS READMEs (config flow, entities page) + my.home-assistant.io badges
- [ ] **home-assistant/brands PR** (`custom_integrations/`) for both domains — integration icon instead of the gray puzzle piece; do before the post so it shows in everyone's screenshots. *Folder layout + placeholder icon prepped by the closing pass (§6); you approve the icon and submit the PR*
- [ ] Docker release story: `latest`/semver tags on ghcr — *implemented by the closing pass (§5); just verify the first tagged build*
- [ ] Finalize and post the HA forum "Share your Projects" post — *draft produced by the closing pass (§6, `FORUM_POST_DRAFT.md`)*; amplify on Discord after
- [ ] *(Later, optional)* HACS default-store submission
