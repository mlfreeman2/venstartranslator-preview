# Debug Logging Guide

## Overview

The Venstar Translator integration includes comprehensive debug logging to help troubleshoot issues, especially with protobuf packet generation and UDP broadcasting.

## Enabling Debug Logging

Add this to your Home Assistant `configuration.yaml`:

```yaml
logger:
  default: info
  logs:
    custom_components.venstar_translator: debug
```

Then restart Home Assistant.

## What Gets Logged

### Pairing Packets (INFO level)
When you click "Done" in the config UI or call the `pair_sensor` service:

```
Building PAIRING packet for sensor 0 (Living Room): temp=72.5°F, temp_index=112, mac=428e0486d800, purpose=Remote
Sensor 0: Pairing signature_key=abcdef1234567890... (truncated)
Sensor 0: Built pairing packet, size=87 bytes, hex=0a552a530802100018c42e204220... (truncated)
Pairing packet sent for sensor 0 (Living Room)
```

**Key details logged:**
- Sensor ID and name
- Temperature value and lookup table index
- MAC address
- Sensor purpose (Outdoor/Remote/Return/Supply)
- Signature key (truncated for security)
- Full packet hex (first 32 chars) for comparison with C# version

### Data Packets (DEBUG level)
Every scheduled broadcast (60s or 300s):

```
Building data packet for sensor 0 (Living Room): temp=72.5°F, temp_index=112, seq=42
Sensor 0: INFO bytes=54, signature=1a2b3c4d5e6f7g8h... (truncated)
Sensor 0: Built data packet, size=87 bytes, next_seq=43
Broadcasting UDP packet: size=87 bytes, destination=255.255.255.255:5001, repeats=5, hex=0a552a530802...
UDP broadcast 1/5: sent 87 bytes to 255.255.255.255:5001
UDP broadcast 2/5: sent 87 bytes to 255.255.255.255:5001
UDP broadcast 3/5: sent 87 bytes to 255.255.255.255:5001
UDP broadcast 4/5: sent 87 bytes to 255.255.255.255:5001
UDP broadcast 5/5: sent 87 bytes to 255.255.255.255:5001
Successfully broadcast 5 UDP packets
Broadcast sensor 0 (Living Room): 72.5°F (seq=42)
```

**Key details logged:**
- Temperature and lookup index
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
Failed to broadcast UDP packet: [Errno 101] Network is unreachable, packet_size=87, port=5001
Cannot pair sensor 0: temperature unavailable from entity sensor.living_room_temperature
Invalid temperature value from sensor.outdoor_temp: unavailable - could not convert string to float: 'unavailable'
Temperature 200°F out of range (rounded to 200, valid range: -40 to 188)
```

## Comparing with C# Version

To verify the Python implementation matches the C# version:

1. **Enable debug logging in both versions**
2. **Compare packet hex output** - should be identical for same inputs:
   - Same temperature
   - Same sequence number
   - Same sensor ID and MAC

Example comparison:

**C# log:**
```
Sensor 0: Built data packet, hex=0a552a530802100018c42e2042201a7b8c9d...
```

**Python log:**
```
Sensor 0: Built data packet, size=87 bytes, hex=0a552a530802100018c42e2042201a7b8c9d...
```

If hex strings match → protobuf implementation is correct!

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
grep "ERROR.*venstar_translator" /config/home-assistant.log
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
3. Check temperature index - is it valid (0-248 for F, 0-253 for C)?
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
    custom_components.venstar_translator: info
```

This reduces logging to only:
- Pairing packets
- Coordinator lifecycle changes
- Errors/warnings

## Packet Structure Reference

Example pairing packet breakdown (hex):
```
0a55              # Field 1 (Command): SENSORPAIR (43 = 0x2b)
  2a53            # Field 42 (SensorData)
    0802          # Field 1 (Info.Sequence): 1
    1000          # Field 2 (Info.SensorId): 0
    18c42e        # Field 3 (Info.Mac): "428e0486d800"
    2004          # Field 4 (Info.FwMajor): 4
    2002          # Field 5 (Info.FwMinor): 2
    3001          # Field 6 (Info.Model): TEMPSENSOR (1)
    3801          # Field 7 (Info.Power): BATTERY (1)
    420c4c69...   # Field 8 (Info.Name): "Living Room"
    4803          # Field 9 (Info.Type): REMOTE (3)
    5070          # Field 10 (Info.Temperature): 112 (72°F)
    5864          # Field 11 (Info.Battery): 100
  12...           # Field 2 (Signature): base64 signature
```

Use this to manually inspect packets if needed!
