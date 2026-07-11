# Remaining Work — Ordered Checklist

Companion context, rationale, and gotchas live in [NOTES.md](NOTES.md). Work top to bottom.

**State as of 2026-07-11:** both feature implementations landed (`7ce7dcd` listener, `5ba7a9d` protobuf page + build identity) and the closing pass has been executed (logging review, status-flips, `CAPTURE_FORMAT.md`, security note, Docker `latest`/semver tags + coverage gate, share-prep drafts, docs archived). The executed plans live in `docs/archive/` (and each component's `docs/archive/`). **Everything below is human-only.**

## Phase 1 — Repo split

- [ ] Rename this repo to `venstartranslator` (decided 2026-07-10: chop `-preview`) — **do this first**: all baked-in cross-repo links already use the final URL, and GitHub only redirects old→new, so links to the final name 404 until the rename happens
- [ ] Create `github.com/mlfreeman2/venstar-acc-tsenwifi-emulator`; push the contents of `hacs/venstar-acc-tsenwifi-emulator/` as the repo root
- [ ] Create `github.com/mlfreeman2/venstar-acc-tsenwifi-listener`; push the contents of `hacs/venstar-acc-tsenwifi-listener/` as the repo root
- [ ] Set description + topics on both repos — descriptions must contain both part numbers (`ACC-TSENWIFI`, `ACC-TSENWIFIPRO`); topics: `home-assistant`, `hacs`, `venstar`
- [ ] Tag **v0.3.0** release on the emulator repo and **v0.1.0** on the listener repo so HACS offers pinned versions (listener plan phase 7)
- [ ] Confirm both repos' CI (`validate.yml`) passes post-upload — some HACS checks read the live repo's description/topics, so they can only pass there
- [ ] Verify the pre-staged **issue templates** render correctly on all three repos
- [ ] Verify a HACS custom-repository install of **both** integrations end-to-end on a test HA instance
- [ ] Delete `hacs/` from this repo and commit

## Phase 2 — Validation (hardware/live)

- [ ] **Test the HACS emulator against your own thermostat** — the one hardware test you can run yourself; update or remove the "Beta / Untested" banner based on the result
- [ ] **Listener live validation on a disposable HA instance** picking up the real C# app's broadcasts, with the **HACS emulator co-installed** to exercise `ignore_local_emulated` (the C# app is invisible to that filter by design) — see "Listener validation plan" in NOTES.md for what this does and doesn't cover

## Phase 3 — Pre-share

- [ ] Screenshots in both HACS READMEs (config flow, entities page) + my.home-assistant.io badges
- [ ] **home-assistant/brands PR**: review the placeholder icons in [docs/brands-prep/](docs/brands-prep/) (generated; emulator = outgoing arcs, listener = incoming), replace or approve, then follow the steps in its README — do before the post so icons show in everyone's screenshots
- [ ] Verify the first `latest`/semver Docker builds once a `v*` tag or main push happens (workflow changes are in; untested against live CI)
- [ ] Finalize [FORUM_POST_DRAFT.md](FORUM_POST_DRAFT.md) (update the emulator hardware-status line, add screenshots, re-verify links), post to the HA forum, amplify on Discord, then delete the draft file
- [ ] *(Later, optional)* HACS default-store submission

## Post-1.0 (tracked, not scheduled)

- [ ] Listener **capture export** fast-follow (its plan's phase 8) — export `venstar-protobuf-capture/1` files from HA, per [CAPTURE_FORMAT.md](CAPTURE_FORMAT.md); becomes urgent with the first physical-sensor issue report
- [ ] If a physical-sensor owner reports in: update the listener plan's §12 unknowns and the "Beta / Untested" banners accordingly

## Done (for the record)

- [x] Restructure into standalone repo layouts + naming (`a76848f`, 2026-07-10)
- [x] C# protobuf listener page + build identity — plan archived at [docs/archive/PROTOBUF_LISTENER_PLAN.md](docs/archive/PROTOBUF_LISTENER_PLAN.md)
- [x] HACS listener integration phases 1–6 incl. CI + tests — plan archived at `hacs/venstar-acc-tsenwifi-listener/docs/archive/`
- [x] Issue templates in all three repos (caveats dropped 2026-07-11)
- [x] Closing pass per [docs/archive/CLOSING_PASS_PLAN.md](docs/archive/CLOSING_PASS_PLAN.md) (2026-07-11): logging review (C# clean; emulator lazy-%/level fixes; listener lifecycle→INFO), status-flips, CAPTURE_FORMAT.md ×3, security note, Docker release tags + 85% coverage floor, FORUM_POST_DRAFT.md, brands prep
