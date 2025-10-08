# Venstar ACC-TSENWIFIPRO Sensor Protocol

This document describes the UDP broadcast protocol used by Venstar ACC-TSENWIFIPRO wireless temperature sensors to communicate with Venstar ColorTouch thermostats. This protocol can be emulated to create virtual sensors from arbitrary data sources.

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

All data packets are authenticated using HMAC-SHA256 signatures derived from the sensor's MAC address.

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

In a pairing packet, the signature field can be the base64 encoded SHA-256 of anything, but it's easier if it's the SHA-256 of from the MAC address sent in the INFO object.  
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

## Temperature Lookup Tables

Temperature values are NOT sent directly. Instead, the temperature is **rounded to the nearest table value**, and the **array index** is sent in the `Temperature` field.

### Fahrenheit Lookup Table (286 entries, indices 0-285)

**Range**: -40°F to 188°F
**Resolution**: ~0.5°F average (varies, some duplicates exist)

The table starts at:
```
Index 0:  -40°F
Index 1:  -39°F
Index 2:  -38°F
...
Index 285: 188°F
```

**Duplicates**: Several temperature values repeat (e.g., "-36" appears at both indices 4 and 5). The first occurrence should be used.

<details>
<summary>Click to view full Fahrenheit table (286 values)</summary>

```python
FAHRENHEIT_TABLE = [
    "-40", "-39", "-38", "-37", "-36", "-36", "-35", "-34", "-33", "-32",
    "-31", "-30", "-29", "-28", "-27", "-27", "-26", "-25", "-24", "-23",
    "-22", "-21", "-20", "-19", "-18", "-18", "-17", "-16", "-15", "-14",
    "-13", "-12", "-11", "-10", "-9", "-9", "-8", "-7", "-6", "-5",
    "-4", "-3", "-2", "-1", "0", "1", "1", "2", "3", "4",
    "5", "6", "7", "8", "9", "10", "10", "11", "12", "13",
    "14", "15", "16", "17", "18", "19", "19", "20", "21", "22",
    "23", "24", "25", "26", "27", "28", "28", "29", "30", "31",
    "32", "33", "34", "35", "36", "37", "37", "38", "39", "40",
    "41", "42", "43", "44", "45", "46", "46", "47", "48", "49",
    "50", "51", "52", "53", "54", "55", "55", "56", "57", "58",
    "59", "60", "61", "62", "63", "64", "64", "65", "66", "67",
    "68", "69", "70", "71", "72", "73", "73", "74", "75", "76",
    "77", "78", "79", "80", "81", "82", "82", "83", "84", "85",
    "86", "87", "88", "89", "90", "91", "91", "92", "93", "94",
    "95", "96", "97", "98", "99", "100", "100", "101", "102", "103",
    "104", "105", "106", "107", "108", "109", "109", "110", "111", "112",
    "113", "114", "115", "116", "117", "118", "118", "119", "120", "121",
    "122", "123", "124", "125", "126", "127", "127", "128", "129", "130",
    "131", "132", "133", "134", "135", "136", "136", "137", "138", "139",
    "140", "141", "142", "143", "144", "145", "145", "146", "147", "148",
    "149", "150", "151", "152", "153", "154", "154", "155", "156", "157",
    "158", "159", "160", "161", "162", "163", "163", "164", "165", "166",
    "167", "168", "169", "170", "171", "172", "172", "173", "174", "175",
    "176", "177", "178", "179", "180", "181", "181", "182", "183", "184",
    "185", "186", "187", "188"
]
```
</details>

### Celsius Lookup Table (286 entries, indices 0-285)

**Range**: -40.0°C to 86.5°C
**Resolution**: 0.5°C (consistent, no duplicates)

The table starts at:
```
Index 0:   -40.0°C
Index 1:   -39.5°C
Index 2:   -39.0°C
...
Index 285: 86.5°C
```

<details>
<summary>Click to view full Celsius table (286 values)</summary>

```python
CELSIUS_TABLE = [
    "-40.0", "-39.5", "-39.0", "-38.5", "-38.0", "-37.5", "-37.0", "-36.5", "-36.0", "-35.5",
    "-35.0", "-34.5", "-34.0", "-33.5", "-33.0", "-32.5", "-32.0", "-31.5", "-31.0", "-30.5",
    "-30.0", "-29.5", "-29.0", "-28.5", "-28.0", "-27.5", "-27.0", "-26.5", "-26.0", "-25.5",
    "-25.0", "-24.5", "-24.0", "-23.5", "-23.0", "-22.5", "-22.0", "-21.5", "-21.0", "-20.5",
    "-20.0", "-19.5", "-19.0", "-18.5", "-18.0", "-17.5", "-17.0", "-16.5", "-16.0", "-15.5",
    "-15.0", "-14.5", "-14.0", "-13.5", "-13.0", "-12.5", "-12.0", "-11.5", "-11.0", "-10.5",
    "-10.0", "-9.5", "-9.0", "-8.5", "-8.0", "-7.5", "-7.0", "-6.5", "-6.0", "-5.5",
    "-5.0", "-4.5", "-4.0", "-3.5", "-3.0", "-2.5", "-2.0", "-1.5", "-1.0", "-0.5",
    "0.0", "0.5", "1.0", "1.5", "2.0", "2.5", "3.0", "3.5", "4.0", "4.5",
    "5.0", "5.5", "6.0", "6.5", "7.0", "7.5", "8.0", "8.5", "9.0", "9.5",
    "10.0", "10.5", "11.0", "11.5", "12.0", "12.5", "13.0", "13.5", "14.0", "14.5",
    "15.0", "15.5", "16.0", "16.5", "17.0", "17.5", "18.0", "18.5", "19.0", "19.5",
    "20.0", "20.5", "21.0", "21.5", "22.0", "22.5", "23.0", "23.5", "24.0", "24.5",
    "25.0", "25.5", "26.0", "26.5", "27.0", "27.5", "28.0", "28.5", "29.0", "29.5",
    "30.0", "30.5", "31.0", "31.5", "32.0", "32.5", "33.0", "33.5", "34.0", "34.5",
    "35.0", "35.5", "36.0", "36.5", "37.0", "37.5", "38.0", "38.5", "39.0", "39.5",
    "40.0", "40.5", "41.0", "41.5", "42.0", "42.5", "43.0", "43.5", "44.0", "44.5",
    "45.0", "45.5", "46.0", "46.5", "47.0", "47.5", "48.0", "48.5", "49.0", "49.5",
    "50.0", "50.5", "51.0", "51.5", "52.0", "52.5", "53.0", "53.5", "54.0", "54.5",
    "55.0", "55.5", "56.0", "56.5", "57.0", "57.5", "58.0", "58.5", "59.0", "59.5",
    "60.0", "60.5", "61.0", "61.5", "62.0", "62.5", "63.0", "63.5", "64.0", "64.5",
    "65.0", "65.5", "66.0", "66.5", "67.0", "67.5", "68.0", "68.5", "69.0", "69.5",
    "70.0", "70.5", "71.0", "71.5", "72.0", "72.5", "73.0", "73.5", "74.0", "74.5",
    "75.0", "75.5", "76.0", "76.5", "77.0", "77.5", "78.0", "78.5", "79.0", "79.5",
    "80.0", "80.5", "81.0", "81.5", "82.0", "82.5", "83.0", "83.5", "84.0", "84.5",
    "85.0", "85.5", "86.0", "86.5"
]
```
</details>

### Temperature Conversion Example

```python
def temperature_to_index(temp_value: float, scale: str) -> int:
    """
    Convert temperature to lookup table index.

    Args:
        temp_value: Temperature value (float)
        scale: "F" for Fahrenheit or "C" for Celsius

    Returns:
        Lookup table index (0-285)

    Raises:
        ValueError: If temperature is out of range
    """
    if scale == "F":
        table = FAHRENHEIT_TABLE
        rounded = str(round(temp_value))
    elif scale == "C":
        table = CELSIUS_TABLE
        rounded = str(round(temp_value, 1))  # Round to 1 decimal place
    else:
        raise ValueError("Scale must be 'F' or 'C'")

    try:
        return table.index(rounded)
    except ValueError:
        raise ValueError(f"Temperature {rounded}°{scale} out of range for lookup table")

# Examples
index_f = temperature_to_index(72.3, "F")  # Returns 126 (rounds to "72")
index_c = temperature_to_index(22.7, "C")  # Returns 125 (rounds to "22.5")
```

## Example Packet Flows

### Initial Pairing Flow

```
1. Application starts
2. Load sensor configuration (ID, name, type, temperature source)
3. Generate MAC address from sensor ID
4. Derive signature key (SHA256 of MAC)
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
3. **Check signature**: Pairing packet uses SHA256 of MAC (not HMAC)
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
