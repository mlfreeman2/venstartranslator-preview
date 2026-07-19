# Venstar ACC-TSENWIFI(PRO) Sensor Protocol

This document describes the UDP broadcast protocol used by Venstar wireless temperature sensors to communicate with Venstar ColorTouch thermostats. This protocol can be emulated to create virtual sensors from arbitrary data sources.

> **Which sensors speak this protocol?** It was reverse-engineered from the **ACC-TSENWIFIPRO**, which Venstar discontinued in August 2025. Distributors now point buyers at the plain **ACC-TSENWIFI**, which is believed to speak the same protocol — the wire format itself carries no per-part identity (the model field in every packet is simply `TEMPSENSOR`). This protocol is treated as **frozen**: if Venstar ever ships a firmware update that changes the wire format, that is a new protocol to be documented and implemented separately.
>
> This file is deliberately **duplicated across the three related repositories** ([VenstarTranslator](https://github.com/mlfreeman2/venstartranslator), [venstar-acc-tsenwifi-emulator](https://github.com/mlfreeman2/venstar-acc-tsenwifi-emulator), [venstar-acc-tsenwifi-listener](https://github.com/mlfreeman2/venstar-acc-tsenwifi-listener)) so each is self-contained; because the protocol is frozen, the copies cannot legitimately diverge. The file format for sharing raw packet captures of this protocol is specified separately in [CAPTURE_FORMAT.md](CAPTURE_FORMAT.md), duplicated the same way.

## Table of Contents

- [Overview](#overview)
- [Network Requirements](#network-requirements)
- [Protocol Buffers Schema](#protocol-buffers-schema)
- [Packet Types](#packet-types)
- [Building Packets](#building-packets)
  - [Pairing Packet](#pairing-packet)
  - [Data Packet](#data-packet)
- [Temperature Lookup Tables](#temperature-lookup-tables)
- [Implementation Notes](#implementation-notes)
- [Example Packet Flows](#example-packet-flows)

## Overview

The protocol uses Protocol Buffers (protobuf) for message serialization and UDP broadcast on port 5001 for communication. Two packet types are required:

1. **Pairing Packet** (Command 43): Introduces the sensor to the thermostat
2. **Data Packet** (Command 42): Sends temperature readings

All data packets are authenticated with HMAC-SHA256. The key is whatever was sent in the pairing packet's Signature field: the base64-encoded SHA-256 of *anything*. The protocol does not require it to be derived from the MAC address — that's just a convenient convention some implementations (including VenstarTranslator) use so the key can be regenerated instead of stored.

## Network Requirements

- **Protocol**: UDP broadcast
- **Port**: 5001
- **Broadcast Address**: `255.255.255.255:5001`
- **Network**: Sensors and thermostats MUST be on the same Layer 2 broadcast domain (VLAN)

Packets should be sent **5 times** in rapid succession to ensure reliable delivery over UDP.

## Protocol Buffers Schema

### SensorMessage (Root Message)

```protobuf
message SensorMessage {
  enum Commands {
    SETSENSORNAME = 41;
    SENSORDATA = 42;       // Data packet
    SENSORPAIR = 43;       // Pairing packet
    WIFICONFIG = 44;
    WIFISCANRESULTS = 45;
    FIRMWARECHUNK = 46;
    FIRMWARECOMPLETE = 47;
    SUCCESS = 126;
    FAILURE = 127;
  }

  required Commands Command = 1;
  optional SENSORDATA SensorData = 42;  // Tag 42 matches SENSORDATA command
  // Other optional fields omitted for brevity
}
```

### SENSORDATA

```protobuf
message SENSORDATA {
  required INFO Info = 1;
  required string Signature = 2;  // Base64-encoded signature
}
```

### INFO

```protobuf
message INFO {
  enum SensorType {
    OUTDOOR = 1;
    RETURN = 2;
    REMOTE = 3;
    SUPPLY = 4;
  }

  enum PowerSource {
    BATTERY = 1;
    WIRED = 2;
  }

  enum SensorModel {
    TEMPSENSOR = 1;
  }

  required uint32 Sequence = 1;        // Packet sequence number (ushort in C#)
  required uint32 SensorId = 2;        // Sensor ID 0-19 (byte in C#)
  required string Mac = 3;             // 12-character hex MAC address (no delimiters)
  required uint32 FwMajor = 4;         // Firmware major version (byte in C#)
  required uint32 FwMinor = 5;         // Firmware minor version (byte in C#)
  required SensorModel Model = 6;      // Sensor model type
  required PowerSource Power = 7;      // Power source
  optional string Name = 8;            // Sensor name (max 14 chars)
  optional SensorType Type = 9;        // Sensor purpose/type
  optional uint32 Temperature = 10;    // Lookup table index (byte in C#)
  optional uint32 Battery = 11;        // Battery percentage 0-100 (byte in C#)
  optional uint32 Humidity = 12;       // Humidity (not used, byte in C#)
}
```

**Note**: In protobuf, the wire format for integers is variable-length. The C# types noted above (ushort, byte) indicate the actual value ranges used in this protocol.

## Packet Types

### Pairing Packet

**Purpose**: Register a new sensor with the thermostat.

**When to send**: When manually requested by the user. The thermostat sees the packet on the network and stores it temporarily, so the user has ~30 seconds to tap "Add New Sensor" on their thermostat to complete the pairing process. 

**Command**: `SENSORPAIR` (43)

### Data Packet

**Purpose**: Send temperature readings to the thermostat.

**When to send**:
- **Outdoor sensors**: Every 5 minutes
- **Other sensor types** (Return, Remote, Supply): Every 1 minute

**Command**: `SENSORDATA` (42)

## Building Packets

#### SENSORDATA Fields

##### Signature

In a pairing packet, the signature field is the base64 encoded SHA-256 of anything at all — the thermostat simply stores it. Deriving it from the MAC address sent in the INFO object (as VenstarTranslator does) is just a convenience: the key can then be regenerated on demand instead of having to be persisted.  
In a data packet, the SHA-256 that was base64 encoded and sent in the pairing packet is used as the key in a round of HMAC-SHA256 and the result of the HMAC-SHA256 is put in the Signature field.  
If you don't save the value sent in the pairing packet, the sensor will have to be deleted and re-added in the thermostat.

#### INFO Fields

| Field | Type | Value | Derivation | Notes |
|-------|------|-------|------------|-------|
| `Sequence` | unsigned short | incrementing counter | use 1 for pairing packets, and count from 1 (or 2) to 65,000 for data packets before rolling over back to 1 | None
| `SensorId` | byte (0-19) | User-defined - a number from 0 to 19 | Each sensor needs a unique ID. A single thermostat can only support 20 sensors, ID #0-19 |  If this changes, the sensor will need to be deleted and re-added to the thermostat. 
| `Mac` | string | 12-char hex | Anything matching the regular expression `*[0-9a-f]{12}$`.<br>Recommend a 10-char hex prefix and then using the sensor id in hex for the last two characters.<br> **DOES NOT HAVE TO ACTUALLY EXIST** | If this changes, the sensor will need to be deleted and re-added to the thermostat.<br>Example: For a sensor with ID 7, combine `428e0486d8`  with `07` to get `428e0486d807`  |
| `FwMajor` | byte | `4` | **Hard-coded** | Do not change. Captured from real sensor. |
| `FwMinor` | byte | `2` | **Hard-coded** | Do not change. Captured from real sensor. |
| `Model` | enum | `TEMPSENSOR` (1) | **Hard-coded** | Do not change. Captured from real sensor.  |
| `Power` | enum | `BATTERY` (1) | **Hard-coded** | Do not change. Captured from real sensor.  |
| `Name` | string | User-defined | User input | Max 14 characters |
| `Type` | enum | User-defined | Based on sensor purpose | `OUTDOOR` (1), `RETURN` (2), `REMOTE` (3), `SUPPLY` (4) |
| `Temperature` | byte (0-285) | Computed | Lookup table index | See [Temperature Lookup Tables](#temperature-lookup-tables) |
| `Battery` | byte (0-100) | `100` | **Hard-coded** | Do not change. Captured from real sensor. |
| `Humidity` | byte | `0` | **Hard-coded** | Not used, set to 0 |



### Pairing Packet

#### Sequence Number
- **Value**: `1` (always)
- Set `INFO.Sequence = 1`
- Ensure first data packet after pairing packet starts the sequence at `2`

#### Signature
- **Value**: The signature key itself (Base64-encoded SHA256 of something)
- Set `SENSORDATA.Signature = signature_key`
- **Do NOT use HMAC** for pairing packets

#### Complete Structure

```
SensorMessage
├── Command = SENSORPAIR (43)
└── SensorData
    ├── Info
    │   ├── Sequence = 1
    │   ├── SensorId = {0-19}
    │   ├── Mac = "Anything matching the regular expression *[0-9a-f]{12}$"
    │   ├── FwMajor = 4
    │   ├── FwMinor = 2
    │   ├── Model = TEMPSENSOR (1)
    │   ├── Power = BATTERY (1)
    │   ├── Name = "{sensor_name}"
    │   ├── Type = {OUTDOOR|RETURN|REMOTE|SUPPLY}
    │   ├── Temperature = {lookup_table_index}
    │   └── Battery = 100
    └── Signature = "{base64_sha256_of_something}"
```

### Data Packet

#### Sequence Number
- **Initial value**: `1`
- **Increment**: Add 1 after each data packet sent
- **Rollover**: Reset to `1` eventually (recommended when reaching `65000`, it's a 16 bit unsigned value)


#### Signature
- **Field Value**: HMAC-SHA256 of the serialized INFO message
- **HMAC-SHA256 Secret**: SHA256 value that was base64 encoded & sent in pairing packet (but remember to base64 decode it)
- **HMAC-SHA256 Data**: Serialized INFO message bytes

#### Complete Structure

```
SensorMessage
├── Command = SENSORDATA (42)
└── SensorData
    ├── Info
    │   ├── Sequence = {incrementing_counter}
    │   ├── SensorId = {0-19}
    │   ├── Mac = "same mac sent in pairing packet"
    │   ├── FwMajor = 4
    │   ├── FwMinor = 2
    │   ├── Model = TEMPSENSOR (1)
    │   ├── Power = BATTERY (1)
    │   ├── Name = "{sensor_name}"
    │   ├── Type = {OUTDOOR|RETURN|REMOTE|SUPPLY}
    │   ├── Temperature = {lookup_table_index}
    │   └── Battery = 100
    └── Signature = "{base64_hmac_sha256_of_info}"
```

## Temperature Index Calculation

The Venstar protocol uses byte values (0-255) to represent temperatures:
- **Valid temperatures**: 0-253
- **Error codes**: 254 (shorted sensor), 255 (open sensor)

The index space is fundamentally Celsius-based, representing temperatures from -40.0°C to 86.5°C in 0.5°C increments.

### Index Space Overview

| Index | Celsius | Fahrenheit (approx) |
|-------|---------|---------------------|
| 0 | -40.0°C | -40°F |
| 80 | 0.0°C | 32°F |
| 124 | 22.0°C | 72°F |
| 253 | 86.5°C | 188°F |

### Calculation Method

Temperature indices are calculated directly using this formula:

```
index = (rounded_celsius + 40.0) × 2
```

#### For Celsius Temperatures

1. Round to nearest 0.5°C (multiply by 2, round to integer, divide by 2)
2. Calculate index: `(rounded_celsius + 40.0) × 2`

#### For Fahrenheit Temperatures

1. Round to nearest whole degree (half-up rounding)
2. Convert to Celsius: `(fahrenheit - 32) × 5/9`
3. Round the Celsius result to nearest 0.5°C
4. Calculate index: `(rounded_celsius + 40.0) × 2`

### Python Implementation

```python
from decimal import Decimal, ROUND_HALF_UP

def temperature_to_index(temperature: float, scale: str) -> int:
    """
    Calculate temperature index directly from temperature value.

    Args:
        temperature: Temperature value
        scale: "F" for Fahrenheit or "C" for Celsius

    Returns:
        Index (0-253) representing the temperature

    Raises:
        ValueError: If temperature is out of range (-40.0°C to 86.5°C)
    """
    if scale == "F":
        # Round Fahrenheit to whole degrees first (half-up)
        if temperature < 0:
            rounded_f = -Decimal(str(abs(temperature))).quantize(
                Decimal('1'), rounding=ROUND_HALF_UP
            )
        else:
            rounded_f = Decimal(str(temperature)).quantize(
                Decimal('1'), rounding=ROUND_HALF_UP
            )
        # Convert to Celsius
        celsius = float(rounded_f - 32) * 5.0 / 9.0
    elif scale == "C":
        celsius = temperature
    else:
        raise ValueError(f"Invalid scale: {scale}")

    # Round to nearest 0.5°C
    rounded_c = Decimal(str(celsius * 2)).quantize(
        Decimal('1'), rounding=ROUND_HALF_UP
    ) / 2

    # Validate bounds
    if rounded_c < Decimal('-40.0') or rounded_c > Decimal('86.5'):
        raise ValueError(
            f"Temperature {temperature}°{scale} (={rounded_c}°C) "
            f"outside valid range -40.0°C to 86.5°C"
        )

    # Calculate index
    return int((rounded_c + Decimal('40.0')) * 2)

# Examples
index = temperature_to_index(72.0, "F")   # 72°F → 22.22°C → 22.0°C → index 124
index = temperature_to_index(72.5, "F")   # 72.5°F → rounds to 73°F → 22.78°C → 23.0°C → index 126
index = temperature_to_index(67.5, "F")   # 67.5°F → rounds to 68°F → 20.0°C → index 120
index = temperature_to_index(22.5, "C")   # 22.5°C → index 125
index = temperature_to_index(0.0, "C")    # 0.0°C → index 80 (freezing point)
```

### Why Fahrenheit Has Duplicates

When converting Fahrenheit to Celsius for indexing, some Fahrenheit whole-degree values map to the same Celsius 0.5°C increment. This is because 1°F ≈ 0.56°C, which is finer than the 0.5°C resolution.

For example:
- -38.0°C → -36.4°F → rounds to **-36°F**
- -37.5°C → -35.5°F → rounds to **-36°F**

Both Celsius values (-38.0°C and -37.5°C) round to the same Fahrenheit value (-36°F). This means some Fahrenheit temperatures will never produce certain indices, and some will produce the same index.

Similarly:
- 22.5°C → 72.5°F → rounds to **73°F**
- 23.0°C → 73.4°F → rounds to **73°F**

This is not a bug—it's a mathematical consequence of Fahrenheit having finer granularity (1°F) than the protocol's Celsius-based 0.5°C resolution.

### Valid Temperature Ranges

| Scale | Minimum | Maximum |
|-------|---------|---------|
| Celsius | -40.0°C | 86.5°C |
| Fahrenheit | -40°F | 188°F |

Note: Fahrenheit appears to have a "smaller" range because it's converted through Celsius. Both scales cover the same physical temperature range.

## Example Packet Flows

### Initial Pairing Flow

```
1. Application starts
2. Load sensor configuration (ID, name, type, temperature source)
3. Generate MAC address from sensor ID
4. Generate signature key (base64-encoded SHA256 of anything; this implementation uses SHA256 of the MAC so the key never needs to be stored)
5. Fetch current temperature and convert to lookup index
6. Build pairing packet:
   - Command = SENSORPAIR (43)
   - Sequence = 1
   - Signature = signature_key (base64 encoded SHA256, not HMAC-SHA256)
7. Serialize protobuf packet
8. Send UDP broadcast 5 times to 255.255.255.255:5001
9. Within 30 seconds or so, user taps "Add New Sensor" on thermostat touchscreen and thermostat reports successful pairing.
```

### Ongoing Data Transmission

```
1. Timer triggers (every 1 or 5 minutes based on sensor type)
2. Fetch current temperature from data source
3. Round temperature and look up index in appropriate table
4. Build data packet:
   - Command = SENSORDATA (42)
   - Sequence = {incrementing_counter}
   - Serialize INFO message
   - Compute HMAC-SHA256 of INFO bytes using value sent in signature field during pairing process
   - Set Signature = HMAC result
5. Serialize complete protobuf packet
6. Send UDP broadcast 5 times to 255.255.255.255:5001
7. Increment sequence counter (rollover at 65000)
8. Thermostat updates temperature display
```

## Reference Implementation

The VenstarTranslator C# implementation provides a complete reference:

- **Packet building**: `VenstarTranslator.cs:110-170`
- **Protobuf models**: `ProtobufNetModel.cs:8-140`
- **Temperature tables**: `VenstarTranslator.cs:29-32`
- **Broadcast logic**: `VenstarTranslator.cs:172-191`

## Troubleshooting

### Thermostat not discovering sensor

1. **Check network**: Ensure broadcast packets reach the thermostat (same VLAN/subnet)
2. **Verify MAC format**: Must be exactly 12 hex characters, lowercase, no delimiters
3. **Check signature**: Pairing packet carries the base64-encoded signature key itself (a SHA256 of anything — not an HMAC)
4. **Timing**: The thermostat only caches pairing packets for 30-60 seconds

### Sensor appears but shows no temperature

1. **Check sequence number**: Must increment with each data packet
2. **Verify HMAC signature**: Data packets require HMAC-SHA256 of serialized INFO
3. **Validate temperature index**: Must be 0-285 and within table bounds
4. **Check broadcast schedule**: Outdoor sensors every 5 min, others every 1 min. 

### Temperature reading incorrect

1. **Verify scale**: Ensure using correct lookup table (F vs C)
2. **Check rounding**: Fahrenheit rounds to integer, Celsius to 0.5°C
3. **Validate index**: `Temperature` field is the array index, not the actual temperature

## License

This protocol documentation is provided as-is for educational and interoperability purposes.
