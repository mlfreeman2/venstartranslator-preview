# Installation Guide - Venstar Translator for Home Assistant

This guide covers installing the Venstar Translator integration into Home Assistant as a custom component.

## Prerequisites

- Home Assistant Core 2024.1.0 or newer
- Python 3.11 or newer (comes with HA)
- Venstar ColorTouch thermostat on the same network/VLAN
- Temperature sensor entities already configured in Home Assistant

## Installation Methods

### Method 1: Manual Installation (Recommended for Testing)

1. **Copy the integration files**

   Copy the entire `venstar_translator` folder to your Home Assistant `custom_components` directory:

   ```bash
   # From the repository root
   cp -r hacs/custom_components/venstar_translator /config/custom_components/
   ```

   Your directory structure should look like:
   ```
   /config/
   ├── custom_components/
   │   └── venstar_translator/
   │       ├── __init__.py
   │       ├── manifest.json
   │       ├── config_flow.py
   │       ├── coordinator.py
   │       ├── storage.py
   │       ├── venstar_sensor.py
   │       ├── const.py
   │       ├── services.yaml
   │       ├── strings.json
   │       ├── translations/
   │       │   └── en.json
   │       └── protobuf/
   │           ├── sensor_message.proto
   │           └── sensor_message_pb2.py
   ```

2. **Restart Home Assistant**

   Go to **Settings** → **System** → **Restart**

3. **Verify installation**

   Check Home Assistant logs for errors:
   ```bash
   tail -f /config/home-assistant.log | grep venstar_translator
   ```

   You should NOT see any import errors or exceptions.

### Method 2: Via HACS (Future)

> **Note**: This integration is not yet available in the HACS default repository.

Once submitted to HACS:

1. Open HACS in Home Assistant
2. Go to **Integrations**
3. Click **+ Explore & Download Repositories**
4. Search for "Venstar Translator"
5. Click **Download**
6. Restart Home Assistant

## Initial Setup

### Step 1: Add the Integration

1. Go to **Settings** → **Devices & Services**
2. Click **+ Add Integration**
3. Search for "Venstar Translator"
4. Click to add it

   ![Add Integration](docs/screenshots/add-integration.png)

5. Click **Submit** on the setup dialog

   The integration will automatically:
   - Generate a random MAC address prefix
   - Initialize storage
   - Prepare for sensor configuration

### Step 2: Configure Sensors

1. Find "Venstar Translator" in your integrations list
2. Click **Configure**

   ![Configure Button](docs/screenshots/configure-button.png)

3. You'll see the sensor management screen:

   ```
   Configured sensors (0/20):

   No sensors configured yet. Click 'Add Sensor' to get started.
   ```

4. Click **Add Sensor**

### Step 3: Add Your First Sensor

Fill in the sensor details:

- **Temperature Entity**: Select from dropdown (e.g., `sensor.living_room_temperature`)
- **Sensor Name**: Give it a name **14 characters or less** (e.g., "Living Room")
- **Sensor Purpose**: Select one:
  - **Outdoor** - Display only, broadcasts every 5 minutes
  - **Remote** - Used for HVAC control, broadcasts every 1 minute
  - **Return** - Return air monitoring, broadcasts every 1 minute
  - **Supply** - Supply air monitoring, broadcasts every 1 minute
- **Temperature Scale**: F (Fahrenheit) or C (Celsius)
- **Enabled**: Check to start broadcasting immediately

Example configuration:
```
Entity: sensor.living_room_temperature
Name: Living Room
Purpose: Remote
Scale: F
Enabled: ✓
```

5. Click **Submit**

The sensor is now added and will appear in the list:
```
Configured sensors (1/20):

  [0] Living Room - Remote - ✓ Enabled
```

### Step 4: Add More Sensors (Optional)

Repeat Step 3 to add up to 20 sensors total.

### Step 5: Pair with Thermostat

1. After adding all desired sensors, click **Done (Pair All Sensors)**

2. The integration will send pairing packets for all enabled sensors via UDP broadcast

3. **On your Venstar thermostat:**
   - Consult your thermostat's manual for instructions on pairing wireless sensors
   - The sensors should appear with the names you configured in Home Assistant
   - Complete the pairing process as described in your thermostat's documentation

**Note**: The integration can only *send* pairing packets - it cannot detect if the thermostat received them or if you completed pairing. Check your thermostat's sensor menu to verify.

### Step 6: Verify Broadcasts

Enable debug logging to verify broadcasts are working:

**configuration.yaml:**
```yaml
logger:
  default: info
  logs:
    custom_components.venstar_translator: debug
```

Restart Home Assistant, then check logs:

```bash
grep "Broadcast sensor" /config/home-assistant.log
```

You should see entries like:
```
Broadcast sensor 0 (Living Room): 72.5°F (seq=42)
Broadcast sensor 1 (Bedroom): 68.0°F (seq=15)
```

## Network Requirements

### Critical: Same VLAN/Broadcast Domain

The integration **MUST** run on the same network segment as your Venstar thermostat because it uses UDP broadcasts (255.255.255.255:5001).

**If your Home Assistant is on a different VLAN:**
- UDP broadcasts will NOT reach the thermostat
- Sensors will not appear on the thermostat
- You'll need to:
  - Move HA to the same VLAN as the thermostat, OR
  - Run the Docker version of VenstarTranslator on the correct VLAN

**How to verify you're on the same network:**

1. Find your thermostat's IP (Menu → WiFi → Network Info)
2. From Home Assistant, check if you can ping it:
   ```bash
   ping <thermostat-ip>
   ```
3. Check your network settings are in the same subnet

## Managing Sensors

### Edit a Sensor

1. Go to integration **Configure** screen
2. Click **Edit Sensor**
3. Select sensor from dropdown
4. Modify any field
5. Click **Submit**

The coordinator will automatically:
- Restart if purpose changed (different broadcast interval)
- Stop if disabled
- Start if enabled

### Delete a Sensor

1. Go to integration **Configure** screen
2. Click **Delete Sensor**
3. Select sensor from dropdown
4. Confirm deletion

The sensor ID is freed and can be reused for a new sensor.

### Disable/Enable Sensors

Edit the sensor and toggle the **Enabled** checkbox. Disabled sensors:
- Don't broadcast
- Don't appear on thermostat
- Free up broadcast bandwidth

## Manual Pairing Service

If you need to re-pair a single sensor (e.g., after thermostat reboot):

**Developer Tools** → **Services**:

```yaml
service: venstar_translator.pair_sensor
data:
  sensor_id: 0
```

Replace `0` with the sensor ID you want to pair.

## Storage and Persistence

All configuration is stored in Home Assistant's storage:

```
/config/.storage/venstar_translator
```

This JSON file contains:
- MAC address prefix (generated once)
- All sensor configurations
- Sequence numbers (persist across restarts)

**Backup recommendation**: Include `.storage/` in your HA backup routine.

## Troubleshooting

### Sensors not appearing on thermostat?

1. **Check pairing**: Look for "Paired sensor X" in logs
2. **Check network**: Verify HA and thermostat on same VLAN
3. **Check entity**: Verify temperature entity has valid state
4. **Check name**: Must be 14 characters or less
5. **Re-pair**: Go to Configure → Done (re-pairs all sensors)

### Temperature not updating?

1. **Check broadcasts**: Look for "Broadcast sensor X" in logs (every 60s or 300s)
2. **Check entity state**: Verify it's not `unavailable` or `unknown`
3. **Check logs**: Look for errors during broadcast

### Integration won't load?

1. **Check Python version**: Must be 3.11+
2. **Check protobuf**: Ensure `sensor_message_pb2.py` exists
3. **Check logs**: Look for import errors
4. **Reinstall**: Delete and re-copy files

### "Max sensors reached" error?

You can only have 20 sensors per integration instance. Delete unused sensors to free up slots.

## Migration from Docker Version

If you're migrating from the Docker VenstarTranslator:

### Before Migration

1. **Note your sensor configurations** from `sensors.json`
2. **Screenshot your thermostat sensor list** for reference

### After Installing HA Integration

1. **Configure sensors** with the **same names** as Docker version
2. **Click "Done"** to pair all sensors
3. **On thermostat**: Sensors should maintain their settings (no need to re-enable)
4. **Verify broadcasts** in logs
5. **Stop Docker container** once verified

**Note**: MAC addresses will be different unless you manually edit `.storage/venstar_translator` to match the old `FakeMacPrefix`.

## Uninstalling

1. Go to **Settings** → **Devices & Services**
2. Find "Venstar Translator"
3. Click the **⋮** menu → **Delete**
4. Confirm deletion

This will:
- Stop all broadcast coordinators
- Remove the integration
- Keep storage file (can be manually deleted)

To fully remove:
```bash
rm -rf /config/custom_components/venstar_translator
rm /config/.storage/venstar_translator
```

## Next Steps

- [Debug Logging Guide](DEBUG_LOGGING.md) - Enable detailed logging
- [Implementation Plan](IMPLEMENTATION_PLAN.md) - Technical details
- [Progress Tracking](PROGRESS.md) - Current development status

## Support

For issues or questions:
- Check logs with debug logging enabled
- Review [DEBUG_LOGGING.md](DEBUG_LOGGING.md) for common issues
- Report bugs on GitHub Issues
