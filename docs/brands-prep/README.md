# home-assistant/brands PR prep

Without a brands entry, both HACS integrations show as a gray puzzle piece in the HA UI. This folder holds everything needed for the PR, ready to copy into a fork of [home-assistant/brands](https://github.com/home-assistant/brands).

> **⚠️ The icons here are generated placeholders** (flat badge + thermometer glyph; outgoing arcs = emulator, incoming arcs = listener), produced by `generate_icons.py`. **A human must approve or replace them before opening the PR.**

## Steps (human)

1. Look at `venstar_acc_tsenwifi_emulator/icon.png` and `venstar_acc_tsenwifi_listener/icon.png`. Keep, tweak (`generate_icons.py`), or replace with proper artwork — requirements: PNG, transparent-friendly, `icon.png` 256×256 and `icon@2x.png` 512×512, no trademark issues (do **not** use Venstar's own logo).
2. Fork `home-assistant/brands`, then copy each folder into the fork under `custom_integrations/`:
   ```
   custom_integrations/venstar_acc_tsenwifi_emulator/icon.png
   custom_integrations/venstar_acc_tsenwifi_emulator/icon@2x.png
   custom_integrations/venstar_acc_tsenwifi_listener/icon.png
   custom_integrations/venstar_acc_tsenwifi_listener/icon@2x.png
   ```
   (The folder name must equal the integration domain. `custom_integrations/` is the correct root for non-core integrations; no `manifest.json` is needed there.)
3. Open one PR covering both domains. PR title convention: `Add icons for venstar_acc_tsenwifi_emulator and venstar_acc_tsenwifi_listener`.
4. After merge, icons appear via `https://brands.home-assistant.io/venstar_acc_tsenwifi_emulator/icon.png` (CDN may take a while). Nothing in the integrations needs to change.

Do this **before** the forum post so the icons show in everyone's screenshots.
