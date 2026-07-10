# Installation Guide - Venstar ACC-TSENWIFI Listener for Home Assistant

This guide covers installing the Venstar ACC-TSENWIFI Listener integration into Home Assistant as a custom component.

> **⚠️ Status: Beta / Untested against hardware** — The decode path is verified against golden C# packets and the companion emulator, but not yet against physical sensors. See the [integration README](README.md) for details, and please [file an issue](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener/issues) with your results if you own real hardware.

## Prerequisites

- Home Assistant 2025.7.1 or newer.
- Physical ACC-TSENWIFI / ACC-TSENWIFIPRO sensors (or the [emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) / the [C# app](https://github.com/mlfreeman2/venstartranslator)) broadcasting on the **same VLAN/broadcast domain** as Home Assistant.
- If Home Assistant runs in Docker: `network_mode: host` (broadcasts do not cross a bridged network).

## Installation methods

### Method 1: Via HACS (custom repository)

1. In HACS, open the **⋮** menu → **Custom repositories**.
2. Add `https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener` with category **Integration**.
3. Install "Venstar ACC-TSENWIFI Listener".
4. Restart Home Assistant.

> HACS is limited to one integration per repository — this repository is the listener's dedicated HACS repo, so nothing else in the family is pulled in. Tagged releases let HACS offer pinned versions instead of tracking the default branch.

### Method 2: Manual installation

1. Copy the `venstar_acc_tsenwifi_listener` folder into your Home Assistant `custom_components` directory:

   ```bash
   # From the repository root
   cp -r custom_components/venstar_acc_tsenwifi_listener /config/custom_components/
   ```

   Your directory structure should look like:
   ```
   /config/
   └── custom_components/
       └── venstar_acc_tsenwifi_listener/
           ├── __init__.py
           ├── manifest.json
           ├── config_flow.py
           ├── const.py
           ├── listener.py
           ├── sensor.py
           ├── storage.py
           ├── diagnostics.py
           ├── strings.json
           ├── translations/
           │   └── en.json
           └── protobuf/
               ├── __init__.py
               ├── sensor_message.proto
               └── sensor_message_pb2.py
   ```

2. Restart Home Assistant (**Settings** → **System** → **Restart**).

3. Verify there are no import errors:
   ```bash
   tail -f /config/home-assistant.log | grep venstar_acc_tsenwifi_listener
   ```

## Initial setup

Only one instance can be added (enforced via `single_config_entry`).

1. Go to **Settings** → **Devices & Services** → **+ Add Integration**.
2. Search for "Venstar ACC-TSENWIFI Listener" and add it.
3. Click **Submit** on the one-click confirm dialog. There is nothing to configure — the listener starts binding UDP 5001 immediately.

As sensors broadcast, devices appear automatically under the integration. A brand-new sensor shows up within one broadcast interval (about a minute, or five for Outdoor sensors).

## Options

Open the integration and click **Configure**:

- **UDP port** (default `5001`) — only change this if you have relocated the traffic.
- **Ignore locally-emulated sensors** (default off) — drop packets from a [venstar_acc_tsenwifi_emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator) integration installed on this *same* Home Assistant, so a mixed emulated + physical fleet doesn't show duplicates (and deleting those devices sticks). Leave it off if you want to see the emulator's sensors (e.g. when testing side by side).

Changing an option reloads the integration and rebinds the socket.

## How discovery, staleness, and deletion behave

- **Discovery**: one HA device per sensor MAC. A temperature entity is always created; battery and humidity entities are created only once a packet actually carries those fields (and the capability is remembered across restarts).
- **Temperature**: reported as native °C — exactly what the thermostat sees, at 0.5 °C resolution. Home Assistant converts it to your display unit. The original Fahrenheit source reading is not recoverable from the wire (the sender rounds before converting); this is expected.
- **Staleness**: a sensor that stops transmitting becomes `unavailable` after 5 minutes (20 minutes for Outdoor). A sensor reporting a shorted/open fault stays **available** with an `unknown` value and a `fault` attribute.
- **Renames**: if a sensor's broadcast name changes, the device is renamed to match — unless you renamed it yourself in Home Assistant, in which case your name always wins.
- **Deletion**: you can delete a discovered device. If it is *still transmitting*, it will be rediscovered on its next packet — stop the source (or, for the co-installed emulator, turn on *Ignore locally-emulated sensors*) to make deletion stick.

## Troubleshooting

### No devices appear

1. **Network**: confirm Home Assistant is on the same VLAN/broadcast domain as the sensors, and that dockerized HA uses `network_mode: host`. The sensors broadcast to `255.255.255.255:5001`, which does not cross subnets or bridged Docker networks.
2. **Traffic**: enable debug logging (below) and confirm packets are being parsed. Download **diagnostics** (integration → **⋮** → *Download diagnostics*) and check the `counters` — if `parsed` is 0 but `dropped_unparseable` is climbing, something on the port isn't Venstar traffic; if everything is 0, no packets are reaching Home Assistant at all (a network/VLAN problem).
3. **Port already in use**: if another process holds UDP 5001 without `SO_REUSEADDR`, setup fails and retries; the log shows `Unable to bind UDP port 5001`. Free the port or change it in options.

### A sensor I deleted keeps coming back

It is still transmitting, so it is rediscovered. Power the sensor off, or — if it's the co-installed emulator — enable *Ignore locally-emulated sensors* in options.

### Integration won't load

- **Home Assistant version**: must be 2025.7.1+. On older versions the bundled protobuf gencode refuses to load (a `VersionError` mentioning "gencode" and "runtime" versions) because HA ships an older `protobuf` runtime.
- **Files**: ensure `protobuf/sensor_message_pb2.py` and `protobuf/__init__.py` are present.

### Debug logging

```yaml
logger:
  logs:
    custom_components.venstar_acc_tsenwifi_listener: debug
```

## Storage and persistence

The discovered-device roster is stored in Home Assistant's storage:

```
/config/.storage/venstar_acc_tsenwifi_listener
```

It holds, per sensor: name, purpose, sensor ID, discovered capabilities, firmware, and last-seen time. Last readings and sequence numbers are intentionally *not* persisted (they read empty after a restart until the next packet). Include `.storage/` in your Home Assistant backup routine.

## Uninstalling

1. Go to **Settings** → **Devices & Services**.
2. Find "Venstar ACC-TSENWIFI Listener" → **⋮** → **Delete**.

To fully remove the files and stored roster:
```bash
rm -rf /config/custom_components/venstar_acc_tsenwifi_listener
rm /config/.storage/venstar_acc_tsenwifi_listener
```

## Support

- Enable debug logging and download diagnostics before filing a report.
- Report issues on [GitHub Issues](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener/issues). If you have physical hardware, a packet capture is the most useful thing you can attach.
