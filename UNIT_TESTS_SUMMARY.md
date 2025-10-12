# Unit Test Summary

## Overview

Created comprehensive unit tests for `TranslatedVenstarSensor` that validate the protobuf packet generation with deterministic hash verification.

## Test Project

- **Location**: `VenstarTranslator.Tests/`
- **Framework**: xUnit with .NET 8.0
- **Tests**: 8 passing tests

## Key Tests

### 1. `BuildDataPacket_WithKnownInputs_ProducesDeterministicHash`
Validates that a sensor with specific known inputs produces a data packet with an exact SHA256 hash:
- **Sensor**: Living Room (71.6°F) with JSONPath `$.ch_aisle[?(@.name=='Living Room')].temp`
- **URL**: `http://172.17.21.11/get_livedata_info` (Ecowitt GW2000 gateway)
- **Expected Hash**: `A3AE86334FF2787A249419F654F6317D009D3AE56F204C773E55714159E6048E`
- **Packet Length**: 98 bytes
- **Purpose**: Ensures protobuf serialization remains consistent across builds

### 2. `BuildPairingPacket_WithKnownInputs_ProducesDeterministicHash`
Validates pairing packet generation with real temperature data:
- **Sensor**: Outside (45.3°F) with JSONPath `$.common_list[?(@.id=='0x02')].val`
- **URL**: `http://172.17.21.11/get_livedata_info` (Ecowitt GW2000 gateway)
- **Packet Length**: 94 bytes
- **Verifies**: Sequence resets to 1 after pairing

### 3. `BuildDataPacket_SameInputs_ProducesSameOutput`
Confirms deterministic behavior - identical inputs always produce identical outputs:
- **Test Temperature**: 71.6°F (Fridge sensor)

### 4. `BuildDataPacket_DifferentTemperatures_ProducesDifferentOutputs`
Validates that different temperatures produce different packet hashes:
- **Test Temperatures**: 71.6°F vs 45.3°F (Living Room vs Outside)

### 5. `BuildDataPacket_SequenceIncrementsCorrectly`
Tests sequence number behavior:
- Increments by 1 for each packet
- Wraps to 1 when reaching 65000

### 6. `BuildDataPacket_WithVariousTemperatures_ProducesValidPackets`
Theory test with multiple realistic Ecowitt sensor scenarios:
- 71.6°F Living Room sensor (`$.ch_aisle[?(@.name=='Living Room')].temp`)
- 35.8°F Fridge sensor (`$.ch_temp[?(@.name=='Fridge')].temp`)
- 72.3°F GW2000 Onboard sensor (`$.wh25[0].intemp`)

## Running Tests

```bash
# Build test project
dotnet build VenstarTranslator.Tests/VenstarTranslator.Tests.csproj

# Run all tests
dotnet test VenstarTranslator.Tests/VenstarTranslator.Tests.csproj

# Run with detailed output
dotnet test VenstarTranslator.Tests/VenstarTranslator.Tests.csproj --logger "console;verbosity=detailed"
```

## Hash Verification Purpose

The hash-based tests serve multiple purposes:

1. **Regression Detection**: Any unintended changes to protobuf structure will immediately fail tests
2. **Cross-Platform Consistency**: Ensures packets are identical across different environments
3. **Protocol Validation**: Confirms the exact bytes being sent to Venstar thermostats
4. **Documentation**: The expected hash serves as a reference for the exact packet format

## Updating Expected Hash

If you intentionally modify the protobuf structure:

1. Run the tests and note the new hash from the test output
2. Update the `expectedHash` constant in the test
3. Document the reason for the change in commit message

## Test Coverage

The tests cover:
- ✅ Protobuf packet building with temperature data
- ✅ Data packet generation and serialization
- ✅ Pairing packet generation with real temperatures
- ✅ Sequence number increment and wraparound behavior
- ✅ Deterministic output verification
- ✅ Hash-based regression detection
- ✅ Various temperature scales and sensor types

## Notes

- Tests use `macPrefix = "428e0486d8"` for consistency
- Sequence wraparound occurs at 65000 (not 65001)
- Pairing packets reset sequence to 1
- Temperature values must be within valid range for the lookup tables
