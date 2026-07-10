# Debug Logging Guide

## Overview

The Venstar ACC-TSENWIFI Emulator integration includes comprehensive debug logging to help troubleshoot issues, especially with protobuf packet generation and UDP broadcasting.

## Enabling Debug Logging

Add this to your Home Assistant `configuration.yaml`:

```yaml
logger:
  default: info
  logs:
    custom_components.venstar_acc_tsenwifi_emulator: debug
```

Then restart Home Assistant.

## What Gets Logged

### Pairing Packets (INFO level)
When you click "Done" in the config UI or call the `pair_sensor` action:

```
Building PAIRING packet for sensor 0 (Living Room): temp=72.5°F, temp_index=126, mac=428e0486d800, purpose=Remote
Sensor 0: Pairing signature_key=8u7K6Lua4yu8kNIc... (truncated)
Sensor 0: Built pairing packet, size=98 bytes, hex=082bd2025d0a2d080110001a0c343238... (truncated)
Pairing packet sent for sensor 0 (Living Room)
```

**Key details logged:**
- Sensor ID and name
- Temperature value and computed temperature index
- MAC address
- Sensor purpose (Outdoor/Remote/Return/Supply)
- Signature key (truncated for security)
- Full packet hex (first 32 chars) for comparison with C# version

### Data Packets (DEBUG level)
Every scheduled broadcast (60s or 300s):

```
Building data packet for sensor 0 (Living Room): temp=72.5°F, temp_index=126, seq=42
Sensor 0: INFO bytes=45, signature=swvLz2AiXF89/1In... (truncated)
Sensor 0: Built data packet, size=98 bytes, next_seq=43
Broadcasting UDP packet: size=98 bytes, destination=255.255.255.255:5001, repeats=5, hex=082ad2025d0a2d082a10001a0c343238...
UDP broadcast 1/5: sent 98 bytes to 255.255.255.255:5001
UDP broadcast 2/5: sent 98 bytes to 255.255.255.255:5001
UDP broadcast 3/5: sent 98 bytes to 255.255.255.255:5001
UDP broadcast 4/5: sent 98 bytes to 255.255.255.255:5001
UDP broadcast 5/5: sent 98 bytes to 255.255.255.255:5001
Successfully broadcast 5 UDP packets
Broadcast sensor 0 (Living Room): 72.5°F (seq=42)
```

**Key details logged:**
- Temperature and computed temperature index
- Current sequence number
- HMAC signature (truncated)
- Packet size
- Number of bytes actually sent to network
- Success confirmation

### Coordinator Lifecycle (INFO/DEBUG level)
When sensors are enabled/disabled:

```
Starting coordinator for sensor 0 (Living Room), interval=60s
Stopping coordinator for sensor 0
```

### Errors (ERROR level)
When things go wrong:

```
Failed to broadcast UDP packet: [Errno 101] Network is unreachable, packet_size=98, port=5001
Cannot pair sensor 0: temperature unavailable from entity sensor.living_room_temperature
Invalid temperature value from sensor.outdoor_temp: unavailable - could not convert string to float: 'unavailable'
Error broadcasting sensor 0 (Living Room): Temperature 200.0°F (=93.5°C) is outside the valid range of -40.0°C to 86.5°C
```

## Comparing with C# Version

The Python packet builder has been verified byte-for-byte against the C# implementation with an automated harness (5,282 packets covering both temperature scales in fine increments, all purposes, boundary/error temperatures, sequence wrap values, and pairing packets). To re-verify manually:

1. **Enable debug logging in both versions**
2. **Compare packet hex output** - should be identical for same inputs:
   - Same temperature
   - Same sequence number
   - Same sensor ID, name, purpose, and MAC

Example comparison (sensor 0, MAC prefix `428e0486d8`, "Living Room", Remote, 72.5°F, seq 42):

**C# log:**
```
Sensor 0: Built data packet, hex=082ad2025d0a2d082a10001a0c343238...
```

**Python log:**
```
Sensor 0: Built data packet, size=98 bytes, hex=082ad2025d0a2d082a10001a0c343238...
```

If hex strings match → protobuf implementation is correct!

One deliberate quirk: when the temperature index is 0 (exactly -40.0°C / -40°F), the `Temperature` field is omitted from the packet entirely, matching the C# serializer's behavior of skipping zero-valued optional fields. Receivers decode the absent field as 0, so the reading is unchanged.

## Useful Grep Commands

View only pairing activity:
```bash
grep -i "pairing" /config/home-assistant.log
```

View only broadcast activity:
```bash
grep -i "broadcast" /config/home-assistant.log
```

View all sensor 0 activity:
```bash
grep "sensor 0" /config/home-assistant.log
```

View errors only:
```bash
grep "ERROR.*venstar_acc_tsenwifi_emulator" /config/home-assistant.log
```

## Debugging Checklist

### Sensor not appearing on thermostat?

1. Check pairing logs - was pairing packet sent?
2. Check temperature entity - is it `unavailable`?
3. Check network - are UDP packets being sent?
4. Check MAC address - is it unique per sensor?
5. Check sensor name - is it ≤14 characters?

### Temperature not updating on thermostat?

1. Check coordinator logs - is it broadcasting?
2. Check sequence numbers - are they incrementing?
3. Check temperature index - is it valid (0-253, where 0 = -40.0°C and 253 = 86.5°C)?
4. Check HMAC signature - is it being generated?

### Broadcasts stopping?

1. Check coordinator lifecycle - did it stop unexpectedly?
2. Check entity state - is it still available?
3. Check errors - any exceptions being thrown?

## Performance Monitoring

At DEBUG level, every broadcast generates ~10 log lines. With 20 sensors broadcasting every minute, that's ~200 lines/minute or ~12,000 lines/hour.

**For production use**, set to INFO level:
```yaml
logger:
  logs:
    custom_components.venstar_acc_tsenwifi_emulator: info
```

This reduces logging to only:
- Pairing packets
- Coordinator lifecycle changes
- Errors/warnings

## Packet Structure Reference

Real pairing packet breakdown (sensor 0, MAC prefix `428e0486d8`, name "Living Room", Remote, 72.5°F). Full hex:

```
082bd2025d0a2d080110001a0c3432386530343836643830302004280230013801420b4c6976696e6720526f6f6d4803507e5864122c<44-char base64 signature>
```

Field by field:
```
08 2b                  # Field 1 (Command), varint: 43 = SENSORPAIR (data packets use 42 = SENSORDATA)
d2 02 5d               # Field 42 (SensorData), length-delimited, 93 bytes
  0a 2d                # Field 1 (Info), length-delimited, 45 bytes
    08 01              # Field 1 (Sequence): 1
    10 00              # Field 2 (SensorId): 0
    1a 0c 3432...3030  # Field 3 (Mac): "428e0486d800" (12 ASCII chars)
    20 04              # Field 4 (FwMajor): 4
    28 02              # Field 5 (FwMinor): 2
    30 01              # Field 6 (Model): TEMPSENSOR (1)
    38 01              # Field 7 (Power): BATTERY (1)
    42 0b 4c69...6f6d  # Field 8 (Name): "Living Room" (11 UTF-8 bytes)
    48 03              # Field 9 (Type): REMOTE (3)
    50 7e              # Field 10 (Temperature): 126 (72.5°F → 73°F → 23.0°C)
    58 64              # Field 11 (Battery): 100
  12 2c ...            # Field 2 (Signature): 44-char base64 string
                       #   pairing: the signature key itself
                       #   data: HMAC-SHA256 of the Info bytes
```

Use this to manually inspect packets if needed!
