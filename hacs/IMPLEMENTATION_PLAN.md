# VenstarTranslator → Home Assistant Custom Component (HACS) Implementation Plan

## Executive Summary

This document outlines converting the ASP.NET Core VenstarTranslator application into a native Home Assistant Custom Component. The integration will monitor Home Assistant temperature entity states and broadcast UDP packets in the Venstar protocol format.

## Key Architecture Changes

### What Gets Reused (C# → Python)
- Protocol Buffer serialization logic
- HMAC-SHA256 signature generation
- Temperature lookup tables (Fahrenheit and Celsius)
- UDP broadcast mechanism (255.255.255.255:5001, send 5 times)
- Sensor domain model and validation logic

### What Changes
- **Data Source**: Replace HTTP/JSONPath polling → Monitor HA entity states
- **Scheduling**: Replace Hangfire cron jobs → Python asyncio time-based scheduler
- **Storage**: Replace SQLite → Home Assistant Store API (JSON in `.storage/`)
- **Configuration**: Replace sensors.json editing → HA Config Flow UI
- **MAC Generation**: Replace FakeMacPrefix → Random 10-char hex (generated once, persisted)

## Critical Constraints

### Broadcast Rate Limits (IMPORTANT!)
The Venstar thermostat enforces strict rate limiting to prevent errors:

- **Remote/Return/Supply sensors**: Max 1 broadcast per 60 seconds (battery mode)
- **Outdoor sensors**: Max 1 broadcast per 300 seconds (5 minutes)
- **Exceeding these limits**: Causes thermostat errors and sensor problems
- **AC-powered mode**: Could allow 3x per minute, but requires `Power=WIRED` in protobuf (future enhancement)

### Implementation Strategy
1. Use time-based scheduling (NOT immediate state change broadcasting)
2. Cache latest HA entity state between broadcasts
3. When scheduled time arrives, broadcast cached current temperature
4. This gives near-real-time data (60s latency) without violating rate limits

## Directory Structure

```
custom_components/venstar_translator/
├── __init__.py                 # Integration setup, coordinator, state listeners
├── manifest.json               # Integration metadata & dependencies
├── config_flow.py             # UI configuration flow (add/edit/delete sensors)
├── const.py                   # Constants (DOMAIN, defaults, etc.)
├── strings.json               # UI text strings (English)
├── translations/
│   └── en.json               # English translations
├── services.yaml             # Service definitions (pairing)
├── protobuf/
│   ├── __init__.py
│   ├── sensor_message.proto  # Protobuf definition (source)
│   └── venstar_pb2.py        # Compiled protobuf (generated)
├── venstar_sensor.py         # Core sensor logic, packet building, UDP broadcast
└── coordinator.py            # Broadcast scheduler & state caching
```

## Core Components Mapping

| C# Component | Python HA Component | Purpose |
|-------------|-------------------|---------|
| `TranslatedVenstarSensor.cs` | `venstar_sensor.py` | Sensor domain logic, packet building |
| `ProtobufNetModel.cs` | `protobuf/venstar_pb2.py` | Protocol Buffer definitions |
| `UdpBroadcaster.cs` | `venstar_sensor.py` (method) | UDP broadcast on port 5001 |
| `Tasks.cs` (Hangfire) | `coordinator.py` | Broadcast scheduling (asyncio) |
| `Program.cs` | `__init__.py` | Integration setup, validation |
| `APIController.cs` | `config_flow.py` | Configuration UI |
| SQLite Database | HA Storage API | Persist MAC, sequence numbers, mappings |

## Data Model

### Storage Format (Home Assistant Store API)
```json
{
  "mac_prefix": "428e0486d8",
  "sensors": {
    "0": {
      "entity_id": "sensor.living_room_temperature",
      "name": "Living Room",
      "purpose": "Remote",
      "scale": "F",
      "enabled": true,
      "sequence": 42
    },
    "1": {
      "entity_id": "sensor.outdoor_temperature",
      "name": "Outside",
      "purpose": "Outdoor",
      "scale": "F",
      "enabled": true,
      "sequence": 157
    }
  }
}
```

### Sensor ID Assignment
- **IDs 0-19**: Automatically assigned in order of sensor creation
- **Max 20 sensors**: Enforced in config flow validation
- **Deleting a sensor**: Frees up that ID for reuse
- **MAC addresses**: Derived as `{mac_prefix}{sensor_id:02x}` (e.g., `428e0486d800`)

## Key Code Ports

### 1. Temperature Lookup Tables
**Source**: `VenstarTranslator/Models/Db/TranslatedVenstarSensor.cs:27-30`

```python
# venstar_sensor.py
TEMPERATURES_FAHRENHEIT = ["-40", "-39", "-38", ..., "188"]  # 249 elements
TEMPERATURES_CELSIUS = ["-40.0", "-39.5", "-39.0", ..., "86.5"]  # 254 elements

def get_temperature_index(temperature: float, scale: str) -> int:
    """Map temperature to Venstar's lookup table index."""
    rounded = str(round(temperature)) if scale == "F" else str(round(temperature, 1))
    lookup = TEMPERATURES_FAHRENHEIT if scale == "F" else TEMPERATURES_CELSIUS
    try:
        return lookup.index(rounded)
    except ValueError:
        raise ValueError(f"Temperature {temperature}°{scale} out of range")
```

### 2. MAC Address & Signature Generation
**Source**: `VenstarTranslator/Models/Db/TranslatedVenstarSensor.cs:46-58`

```python
import hashlib
import hmac
import base64

class VenstarSensor:
    def __init__(self, sensor_id: int, mac_prefix: str):
        self.sensor_id = sensor_id
        self.mac_address = f"{mac_prefix}{sensor_id:02x}"

    @property
    def signature_key(self) -> str:
        """Generate HMAC key from MAC address (SHA256 hash, base64 encoded)."""
        mac_bytes = self.mac_address.encode('utf-8')
        sha256_hash = hashlib.sha256(mac_bytes).digest()
        return base64.b64encode(sha256_hash).decode('utf-8')

    def generate_signature(self, info_bytes: bytes) -> str:
        """Generate HMAC-SHA256 signature for INFO protobuf."""
        key = base64.b64decode(self.signature_key)
        signature = hmac.new(key, info_bytes, hashlib.sha256).digest()
        return base64.b64encode(signature).decode('utf-8')
```

### 3. Protobuf Packet Building
**Source**: `VenstarTranslator/Models/Db/TranslatedVenstarSensor.cs:110-170`

```python
from .protobuf import venstar_pb2

class VenstarSensor:
    def build_data_packet(self, temperature: float) -> bytes:
        """Build protobuf data packet with HMAC signature."""
        # Build INFO message
        info = venstar_pb2.INFO(
            Sequence=self.sequence,
            SensorId=self.sensor_id,
            Mac=self.mac_address,
            FwMajor=4,
            FwMinor=2,
            Model=venstar_pb2.INFO.TEMPSENSOR,
            Power=venstar_pb2.INFO.BATTERY,
            Name=self.name,
            Type=self._get_protobuf_type(),
            Temperature=get_temperature_index(temperature, self.scale),
            Battery=100
        )

        # Generate HMAC signature
        info_bytes = info.SerializeToString()
        signature = self.generate_signature(info_bytes)

        # Build SENSORDATA message
        sensor_data = venstar_pb2.SENSORDATA(
            Info=info,
            Signature=signature
        )

        # Build SensorMessage
        message = venstar_pb2.SensorMessage(
            Command=venstar_pb2.SensorMessage.SENSORDATA,
            SensorData=sensor_data
        )

        # Increment sequence
        self.sequence = (self.sequence + 1) % 65000 or 1

        return message.SerializeToString()

    def build_pairing_packet(self, temperature: float) -> bytes:
        """Build protobuf pairing packet (sequence=1, signature=base64 key)."""
        info = venstar_pb2.INFO(
            Sequence=1,
            SensorId=self.sensor_id,
            Mac=self.mac_address,
            FwMajor=4,
            FwMinor=2,
            Model=venstar_pb2.INFO.TEMPSENSOR,
            Power=venstar_pb2.INFO.BATTERY,
            Name=self.name,
            Type=self._get_protobuf_type(),
            Temperature=get_temperature_index(temperature, self.scale),
            Battery=100
        )

        sensor_data = venstar_pb2.SENSORDATA(
            Info=info,
            Signature=self.signature_key  # Pairing uses key directly
        )

        message = venstar_pb2.SensorMessage(
            Command=venstar_pb2.SensorMessage.SENSORPAIR,
            SensorData=sensor_data
        )

        self.sequence = 1
        return message.SerializeToString()
```

### 4. UDP Broadcasting
**Source**: `VenstarTranslator/Services/UdpBroadcaster.cs:9-18`

```python
import socket

def broadcast_udp_packet(packet: bytes, port: int = 5001):
    """Broadcast UDP packet 5 times to 255.255.255.255:5001."""
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        for _ in range(5):
            sock.sendto(packet, ("255.255.255.255", port))
```

### 5. Broadcast Scheduler (Respects Rate Limits)

```python
import asyncio
from datetime import datetime
import logging

_LOGGER = logging.getLogger(__name__)

class VenstarSensorCoordinator:
    """Manages broadcast scheduling and state caching for a sensor."""

    def __init__(self, hass, sensor_id, sensor_config):
        self.hass = hass
        self.sensor_id = sensor_id
        self.sensor_config = sensor_config
        self.last_broadcast = None
        self._task = None

    async def start(self):
        """Start the broadcast scheduler."""
        # Determine interval based on sensor type
        interval = 300 if self.sensor_config["purpose"] == "Outdoor" else 60

        async def broadcast_loop():
            while True:
                await asyncio.sleep(interval)

                # Get current temperature from HA entity
                temperature = await self._get_current_temperature()

                if temperature is not None:
                    await self._broadcast_sensor(temperature)
                    self.last_broadcast = datetime.now()

        self._task = self.hass.loop.create_task(broadcast_loop())

    async def _get_current_temperature(self):
        """Get current temperature from HA entity."""
        state = self.hass.states.get(self.sensor_config["entity_id"])
        if state and state.state not in ("unknown", "unavailable"):
            try:
                return float(state.state)
            except ValueError:
                _LOGGER.error(f"Invalid temperature value: {state.state}")
        return None

    async def _broadcast_sensor(self, temperature):
        """Build packet and broadcast via UDP."""
        # Import here to avoid circular dependency
        from .venstar_sensor import VenstarSensor, broadcast_udp_packet

        # Get storage data for MAC prefix and sequence
        data = self.hass.data[DOMAIN]["storage"]

        # Create sensor instance
        sensor = VenstarSensor(
            sensor_id=self.sensor_id,
            mac_prefix=data.mac_prefix,
            name=self.sensor_config["name"],
            purpose=self.sensor_config["purpose"],
            scale=self.sensor_config.get("scale", "F"),
            sequence=self.sensor_config["sequence"]
        )

        # Build packet
        packet = sensor.build_data_packet(temperature)

        # Broadcast UDP
        await self.hass.async_add_executor_job(broadcast_udp_packet, packet)

        # Update sequence number in storage
        self.sensor_config["sequence"] = sensor.sequence
        await data.async_save()

        _LOGGER.debug(
            f"Broadcast sensor {self.sensor_id} ({self.sensor_config['name']}): "
            f"{temperature}°{self.sensor_config['scale']}"
        )

    async def stop(self):
        """Stop the broadcast scheduler."""
        if self._task:
            self._task.cancel()
```

## Protocol Buffer Definition

**File**: `custom_components/venstar_translator/protobuf/sensor_message.proto`

```protobuf
syntax = "proto2";

package venstar;

message SensorMessage {
  enum Commands {
    SETSENSORNAME = 41;
    SENSORDATA = 42;
    SENSORPAIR = 43;
    WIFICONFIG = 44;
    WIFISCANRESULTS = 45;
    FIRMWARECHUNK = 46;
    FIRMWARECOMPLETE = 47;
    SUCCESS = 126;
    FAILURE = 127;
  }

  required Commands Command = 1;
  optional SENSORDATA SensorData = 42;
}

message SENSORDATA {
  required INFO Info = 1;
  required string Signature = 2;
}

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

  required uint32 Sequence = 1;
  required uint32 SensorId = 2;
  required string Mac = 3;
  required uint32 FwMajor = 4;
  required uint32 FwMinor = 5;
  required SensorModel Model = 6;
  required PowerSource Power = 7;
  optional string Name = 8;
  optional SensorType Type = 9;
  optional uint32 Temperature = 10;
  optional uint32 Battery = 11;
  optional uint32 Humidity = 12;
}
```

**Compile with**: `protoc --python_out=. sensor_message.proto`

## Implementation Phases

### Phase 1: Foundation (Week 1-2)
**Goal**: Basic integration structure with protobuf support

**Tasks**:
1. Create directory structure and boilerplate files
2. Define `manifest.json` with dependencies (`protobuf>=4.0.0`)
3. Port protobuf definitions to `.proto` file and compile
4. Implement temperature lookup tables and MAC generation logic
5. Write basic config flow (accept installation, generate MAC prefix)
6. Implement storage layer (Store API)
7. Write unit tests for protobuf serialization

**Deliverable**: Integration installs in HA, generates MAC prefix, saves to storage

---

### Phase 2: Core Logic (Week 3-4)
**Goal**: Packet building and UDP broadcasting functional

**Tasks**:
1. Port `VenstarSensor` class with packet building methods
2. Implement HMAC-SHA256 signature generation
3. Create UDP broadcast function (send 5 times to 255.255.255.255:5001)
4. Add pairing packet support
5. Write comprehensive unit tests for packet structure
6. Validate against existing C# implementation (packet hex comparison)

**Deliverable**: Can build and broadcast valid Venstar packets

---

### Phase 3: Sensor Management UI (Week 5-6)
**Goal**: Users can add/edit/delete sensors via HA UI

**Tasks**:
1. Implement options flow with sensor list view
2. Add "Add Sensor" form with entity selector
3. Add "Edit Sensor" functionality
4. Add "Delete Sensor" with confirmation
5. Enforce 20-sensor limit and unique name validation
6. Create `strings.json` and `translations/en.json`
7. Test all validation scenarios

**Deliverable**: Full CRUD interface for sensors in HA config

---

### Phase 4: Broadcast Scheduling (Week 7)
**Goal**: Time-based broadcasts respecting rate limits

**Tasks**:
1. Implement `VenstarSensorCoordinator` with asyncio scheduler
2. Set up 60-second interval for Remote/Return/Supply sensors
3. Set up 300-second interval for Outdoor sensors
4. Fetch current HA entity state at broadcast time
5. Add error handling and logging
6. Test with real HA temperature entities
7. Monitor performance and memory usage

**Deliverable**: Sensors broadcast on schedule with current temperature data

---

### Phase 5: Services & Polish (Week 8)
**Goal**: Add pairing service and finalize UX

**Tasks**:
1. Create `venstar_translator.pair_sensor` service
2. Add service definition in `services.yaml`
3. Implement diagnostic sensors (last broadcast time, status)
4. Add comprehensive logging (debug, info, error levels)
5. Create README with setup instructions
6. Write integration documentation
7. Add `hacs.json` for HACS compatibility

**Deliverable**: Fully functional integration ready for HACS submission

---

### Phase 6: Testing & Release (Week 9-10)
**Goal**: Production-ready release

**Tasks**:
1. End-to-end testing with real Venstar thermostat
2. Test all sensor types (Outdoor, Remote, Return, Supply)
3. Test pairing flow with thermostat
4. Stress test with 20 sensors
5. Document edge cases and limitations
6. Create demo video/screenshots
7. Submit to HACS (if public release)
8. Create GitHub repository with proper licensing

**Deliverable**: v1.0.0 release on GitHub/HACS

## Key Technical Considerations

### Temperature Scale Detection
**Challenge**: HA entities don't always specify unit_of_measurement consistently.

**Solution**:
```python
def detect_temperature_scale(state) -> str:
    """Auto-detect F vs C from entity attributes."""
    unit = state.attributes.get("unit_of_measurement", "").upper()
    if "°F" in unit or "F" in unit:
        return "F"
    elif "°C" in unit or "C" in unit:
        return "C"
    else:
        # Default to user setting or integration default
        return "F"
```

### MAC Prefix Uniqueness
**Issue**: Multiple instances would conflict if using same MAC prefix.

**Solution**:
- Generate **random 10-character hex prefix** on first setup
- Warn users in docs about running multiple instances (requires manual MAC editing)
- Consider adding "Instance ID" field in advanced config

### Sequence Number Persistence
**Challenge**: Sequence numbers must persist across HA restarts to avoid duplicate sequences.

**Solution**:
- Store in HA's storage layer (JSON file in `.storage/`)
- Update after every broadcast
- Reset to 1 on sequence >= 65000

### Broadcast Performance
**Concern**: Broadcasting 20 sensors could overwhelm network.

**Mitigation**:
- Use executor job for UDP broadcasts (non-blocking)
- Stagger initial broadcast times (0-10 second random delay on startup)
- Each sensor runs on independent scheduler

### Error Tracking
**Source**: `VenstarTranslator/Filters/BroadcastTrackingFilter.cs:61-112`

**Port to Python**:
```python
class SensorBroadcastError:
    """Track broadcast failures for diagnostics."""
    def __init__(self):
        self.consecutive_failures = 0
        self.last_error = None
        self.last_success = None

    def record_success(self):
        self.consecutive_failures = 0
        self.last_error = None
        self.last_success = datetime.now()

    def record_failure(self, error: str):
        self.consecutive_failures += 1
        self.last_error = error

    @property
    def has_problem(self) -> bool:
        """5 consecutive failures = problem."""
        return self.consecutive_failures >= 5
```

## Migration Path for Existing Users

For users currently running the Docker container:

1. **Export current sensors.json**
2. **Install HA integration**
3. **Manually configure sensors** in HA UI (one-time setup)
4. **Pair sensors** with thermostat (use same names to avoid re-pairing)
5. **Shut down Docker container** after verifying broadcasts

**Note**: MAC addresses will change unless users manually set the same `mac_prefix` (advanced).

## Dependencies

**File**: `manifest.json`

```json
{
  "domain": "venstar_translator",
  "name": "Venstar Translator",
  "version": "1.0.0",
  "documentation": "https://github.com/[username]/venstar-translator-hacs",
  "issue_tracker": "https://github.com/[username]/venstar-translator-hacs/issues",
  "requirements": ["protobuf>=4.25.0"],
  "codeowners": ["@[username]"],
  "config_flow": true,
  "iot_class": "local_push"
}
```

## Testing Strategy

### Unit Tests
- Protobuf serialization (compare hex output to C# version)
- MAC address generation
- Signature generation (HMAC-SHA256)
- Temperature lookup table indexing
- Sequence number wrapping

### Integration Tests
- Config flow (add/edit/delete sensors)
- Broadcast scheduler timing
- UDP broadcast execution
- Storage persistence

### Hardware Tests
- Pairing with real Venstar thermostat
- Data packet reception and display on thermostat
- Outdoor vs Remote sensor behavior
- 20-sensor stress test

## Future Enhancements (Post-v1.0)

1. **AC Power Mode** (3x per minute broadcasts)
   - Add `ac_powered` boolean to sensor config
   - Set `Power=WIRED` in protobuf
   - Reduce interval to 20 seconds

2. **Humidity Support** (if Venstar protocol supports it)
   - Monitor humidity entities
   - Include in protobuf packets

3. **Battery Simulation** (randomize 95-100% for realism)

4. **Diagnostic Entities**
   - Last broadcast timestamp
   - Consecutive failure count
   - Current temperature being broadcast

5. **Blueprint Automation** (alert on sensor failures)

6. **Multi-Instance Support** (automatic MAC prefix management)

7. **Import from sensors.json** (ease Docker migration)

## Reference Files from C# Codebase

**Core Logic**:
- `VenstarTranslator/Models/Db/TranslatedVenstarSensor.cs` (lines 22-201)
- `VenstarTranslator/Models/Protobuf/ProtobufNetModel.cs` (lines 10-140)
- `VenstarTranslator/Services/UdpBroadcaster.cs` (lines 9-18)
- `VenstarTranslator/Tasks/Tasks.cs` (lines 26-36)

**Validation**:
- `VenstarTranslator/Models/Validation/ValidJsonPathAttribute.cs`
- `VenstarTranslator/Models/Validation/ValidAbsoluteUrlAttribute.cs`

**Error Tracking**:
- `VenstarTranslator/Filters/BroadcastTrackingFilter.cs` (lines 61-112)

## Conclusion

This plan provides a comprehensive roadmap to port VenstarTranslator to a native Home Assistant integration. The core protocol logic (protobuf serialization, HMAC signatures, UDP broadcasting) can be directly ported from C# to Python. The main architectural changes involve:

- Replacing HTTP/JSONPath with HA entity state monitoring
- Replacing Hangfire with Python asyncio time-based scheduler
- Replacing JSON file storage with HA's Store API
- Adding a native HA config flow UI

**Estimated Timeline**: 10 weeks (solo developer, part-time)
**Complexity**: Medium-High (requires protobuf knowledge, HA architecture familiarity, network programming)
**Benefit**: Seamless integration with Home Assistant, no Docker required, native UI, responsive updates (60s latency)
