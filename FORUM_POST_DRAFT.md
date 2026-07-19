# DRAFT — Home Assistant Community "Share your Projects" post

> **This file is a draft for a human to finalize and post, then delete from the repo.**
> Before posting: (1) test the emulator against your real thermostat and update the status
> lines below to match reality, (2) add screenshots (config flow, entities page, the C# web UI,
> the Protobuf Listener page), (3) verify all links resolve after the repo rename/split.

---

**Suggested title:** Venstar wireless temperature sensors (ACC-TSENWIFI/PRO) — emulate them from HA, or ingest real ones into HA

I reverse-engineered the wire protocol that Venstar's wireless temperature sensors (ACC-TSENWIFIPRO, and the ACC-TSENWIFI that replaced it) use to talk to ColorTouch/Explorer thermostats — protobuf broadcast over UDP — and built a small family of tools around it. Sharing here in case anyone else has a Venstar thermostat and wants more flexible sensors, or has the physical sensors and wants their readings in HA.

## Which piece do you need?

| You have | You want | Use |
|---|---|---|
| Temperature data behind any JSON API | Readings on a Venstar thermostat | [VenstarTranslator](https://github.com/mlfreeman2/venstartranslator) (C#/Docker app) |
| Home Assistant entities | Readings on a Venstar thermostat | [venstar-acc-tsenwifi-emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) (HACS integration) |
| Physical Venstar ACC-TSENWIFI(PRO) sensors | Their readings in Home Assistant | [venstar-acc-tsenwifi-listener](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener) (HACS integration) |
| Curiosity about the wire protocol | Documentation | [PROTOCOL.md](https://github.com/mlfreeman2/venstartranslator/blob/main/PROTOCOL.md) |

The thermostat can't tell the difference: the emulated sensors pair and update exactly like the real accessory (same protobuf packets, same HMAC signatures). Up to 20 sensors per instance, all four sensor purposes (Outdoor / Remote / Return / Supply), °F and °C.

## Honest status, per component

- **VenstarTranslator (C#/Docker)** — the original, running against a real thermostat for a long time. Pulls temperatures from any JSON endpoint (HA's REST API, Ecowitt stations, anything) via JSONPath. Web UI for config, healthchecks.io integration, and a built-in "Protobuf Listener" diagnostic page that captures and decodes the protocol traffic on your network.
- **Emulator (HACS)** — feature-complete; its packets are byte-for-byte identical to the C# app's (verified by an automated 5,000+ packet comparison harness). *[UPDATE BEFORE POSTING: tested / not yet tested against a physical thermostat.]*
- **Listener (HACS)** — feature-complete; decodes verified against golden packets from the reference implementation and tested end-to-end against emulated traffic. **Never yet run against physical sensors — this is where I need you.**

## 📢 Call for testers: do you own ACC-TSENWIFI or ACC-TSENWIFIPRO sensors?

I don't own the physical sensors, so a handful of real-hardware behaviors are educated guesses (documented as such): does real hardware report humidity? what do real battery levels look like? how are MACs formatted on the wire? The listener is deliberately liberal about all of them — but I'd love to *know*. If you have the sensors, installing the listener and filing a [Physical sensor report](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener/issues/new/choose) (ideally with a packet capture — the issue template walks you through it) would directly answer questions nobody else can.

## Notes

- Everything is local — no cloud, no accounts. The one hard requirement is being on the **same VLAN/broadcast domain** as the thermostat/sensors (UDP broadcast); Docker deployments need `network_mode: host`.
- Compatible thermostats: ColorTouch T7850/T7900/T8850/T8900, Explorer and Explorer Mini series (some need the ACC-VWF2 accessory). Older T5800/6800-era ColorTouch models use a different protocol and are **not** supported.
- The protocol docs and golden test fixtures are duplicated across all three repos, so each stands alone.

*[SCREENSHOTS HERE: emulator config flow, listener devices page, C# web UI, Protobuf Listener page]*
