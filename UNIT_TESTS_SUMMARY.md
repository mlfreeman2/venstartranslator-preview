# Unit Test Summary

## Overview

Comprehensive unit test suite covering business logic, validation, error handling, and API endpoints. Built with xUnit, Moq, and Entity Framework InMemory provider.

## Test Statistics

- **Overall Coverage**: 64%
- **Total Tests**: 83 passing tests
- **Test Files**: 4 test suites
- **Framework**: xUnit with .NET 8.0
- **Mocking**: Moq 4.20.72

## Test Suites

### 1. APIControllerTests (26 tests)

Tests all REST API endpoints with integration-style testing using real SensorOperations.

**Coverage Areas:**
- ✅ Sensor CRUD operations (Create, Read, Update, Delete)
- ✅ GetReading endpoint with error handling
- ✅ SendPairingPacket endpoint
- ✅ TestJsonPath endpoint for JSONPath validation
- ✅ HTTP error propagation (connection refused, timeouts)
- ✅ InvalidOperationException handling (JSONPath errors)
- ✅ Database interactions via in-memory SQLite

**Key Tests:**
- `GetReading_Success_ReturnsTemperatureAndScale` - Validates temperature extraction
- `SendPairingPacket_Success_ReturnsSuccessMessage` - Tests UDP broadcast verification
- `UpdateSensor_UpdatesHeaders` - Validates header configuration updates
- `AddSensor_AssignsLowestAvailableID` - Tests ID assignment logic

**Architecture:**
- Uses **real SensorOperations** instance (not mocked)
- Mocks **IHttpDocumentFetcher** and **IUdpBroadcaster** at the lowest level
- Tests actual integration between APIController → SensorOperations → dependencies

### 2. TranslatedVenstarSensorTests (7 tests)

Tests protobuf packet generation with deterministic hash verification and temperature extraction.

**Coverage Areas:**
- ✅ Protobuf data packet building
- ✅ Protobuf pairing packet building
- ✅ JSONPath temperature extraction
- ✅ Sequence number management (increment and wraparound)
- ✅ Deterministic serialization

**Key Tests:**

#### `BuildDataPacket_WithKnownInputs_ProducesDeterministicHash`
Validates that a sensor with specific known inputs produces a data packet with an exact SHA256 hash:
- **Sensor**: Living Room (71.6°F) with JSONPath `$.ch_aisle[?(@.name=='Living Room')].temp`
- **URL**: `http://172.17.21.11/get_livedata_info` (Ecowitt GW2000 gateway)
- **Expected Hash**: `A3AE86334FF2787A249419F654F6317D009D3AE56F204C773E55714159E6048E`
- **Packet Length**: 98 bytes
- **Purpose**: Ensures protobuf serialization remains consistent across builds

#### `BuildPairingPacket_WithKnownInputs_ProducesDeterministicHash`
Validates pairing packet generation with real temperature data:
- **Sensor**: Outside (45.3°F) with JSONPath `$.common_list[?(@.id=='0x02')].val`
- **URL**: `http://172.17.21.11/get_livedata_info` (Ecowitt GW2000 gateway)
- **Packet Length**: 94 bytes
- **Verifies**: Sequence resets to 1 after pairing

#### `BuildDataPacket_SequenceIncrementsCorrectly`
Tests sequence number behavior:
- Increments by 1 for each packet
- Wraps to 1 when reaching 65000

#### `BuildDataPacket_WithVariousTemperatures_ProducesValidPackets` (Theory test)
Tests multiple realistic Ecowitt sensor scenarios:
- 71.6°F Living Room sensor (`$.ch_aisle[?(@.name=='Living Room')].temp`)
- 35.8°F Fridge sensor (`$.ch_temp[?(@.name=='Fridge')].temp`)
- 72.3°F GW2000 Onboard sensor (`$.wh25[0].intemp`)

### 3. HttpDocumentFetcherTests (18 tests)

Comprehensive error handling tests for HTTP requests.

**Coverage Areas:**
- ✅ Successful HTTP requests with custom headers
- ✅ Timeout handling
- ✅ Connection errors (refused, network unreachable)
- ✅ SSL certificate validation failures
- ✅ HTTP status codes (400, 401, 403, 404, 405, 500, 502, 503)
- ✅ Invalid HTTP responses
- ✅ User-friendly error messages

**Key Tests:**
- `FetchDocument_RequestTimeout_ThrowsHttpRequestExceptionWithTimeoutMessage` - "Request timed out after 10 seconds"
- `FetchDocument_ConnectionRefused_ThrowsHttpRequestExceptionWithConnectionRefusedMessage` - Guides user to check URL/server
- `FetchDocument_SSLCertificateError_ThrowsHttpRequestExceptionWithSSLMessage` - Suggests enabling "Ignore SSL Errors"
- `FetchDocument_HttpStatusCodeError_ThrowsHttpRequestExceptionWithAppropriateMessage` (Theory with 8 status codes) - Context-specific guidance

**Error Message Examples:**
- 401: "Authentication failed. Check your headers and API keys."
- 404: "The URL does not exist. Verify the endpoint URL is correct."
- 503: "The server is temporarily unavailable. Try again later."

### 4. ValidationAttributeTests (32 tests)

Tests custom data validation attributes used for sensor configuration.

#### ValidAbsoluteUrlAttribute (10 tests)
- ✅ Valid HTTP/HTTPS URLs with ports, query strings
- ✅ Null/empty/whitespace handling (delegates to Required attribute)
- ✅ Rejects relative URLs, missing schemes, invalid formats
- ✅ Error message: "The URL must be a properly formed absolute URL."

#### ValidHttpHeadersAttribute (13 tests)
- ✅ Valid single and multiple headers
- ✅ Null/empty list handling
- ✅ Rejects null/empty/whitespace header names or values
- ✅ Detects duplicate header names
- ✅ Error messages guide user to fix specific issues

#### ValidJsonPathAttribute (9 tests)
- ✅ Valid JSONPath expressions (simple, nested, array index, filters, recursive descent)
- ✅ Null/empty/whitespace handling
- ✅ Detects invalid syntax with helpful error messages
- ✅ Special handling for double quotes: "Replace double quotes \" with single quotes '."

## Running Tests

```bash
# Run all tests
dotnet test VenstarTranslator.Tests/VenstarTranslator.Tests.csproj

# Run with coverage
dotnet test VenstarTranslator.Tests/VenstarTranslator.Tests.csproj --collect:"XPlat Code Coverage"

# Run specific test suite
dotnet test --filter "FullyQualifiedName~ValidationAttributeTests"
dotnet test --filter "FullyQualifiedName~HttpDocumentFetcherTests"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Code Coverage Details

### Well-Covered Components (>80%)

| Component | Coverage | Branch Coverage |
|-----------|----------|-----------------|
| ValidationAttributeTests | 100% | 100% |
| TranslatedVenstarSensor | 94% | 63% |
| VenstarTranslatorDataCache | 94% | 100% |
| HttpDocumentFetcher | 89% | 82% |
| APIController | 84% | 75% |

### Excluded from Coverage

These components are marked with `[ExcludeFromCodeCoverage]` as they contain no testable business logic:

- **Program.cs** - Application entry point
- **UdpBroadcaster** - Network I/O (requires actual UDP sockets)
- **FetchUrlRequest** - Simple DTO
- **Protobuf Models** - Protocol buffer DTOs (SensorMessage, SENSORDATA, INFO, WIFICONFIG, etc.)
- **HangfireJobManager** - Thin wrapper around Hangfire static API
- **SensorOperations.SyncToJsonFile()** - File I/O operation

### Not Covered (Integration-Level Testing Required)

- **Startup.cs** (0%) - Application bootstrapping, complex to unit test
- **Tasks.cs** (0%) - Hangfire background jobs, requires Hangfire test framework

## Test Architecture

### Integration Testing Approach

Rather than mocking every layer, the tests use **integration testing** where appropriate:

1. **APIController tests** use:
   - Real `SensorOperations` instance
   - Mocked `IHttpDocumentFetcher` and `IUdpBroadcaster` at the lowest level
   - In-memory SQLite database for `VenstarTranslatorDataCache`

2. **Benefits**:
   - Tests actual code paths used in production
   - Catches integration bugs between layers
   - Reduces over-mocking anti-pattern
   - Simplifies test maintenance (fewer mocks to update)

### Dependency Injection

All testable components use constructor injection:

```csharp
public SensorOperations(IHttpDocumentFetcher documentFetcher, IUdpBroadcaster udpBroadcaster)
```

This allows tests to inject mocks:

```csharp
var mockFetcher = new Mock<IHttpDocumentFetcher>();
var mockBroadcaster = new Mock<IUdpBroadcaster>();
var sensorOps = new SensorOperations(mockFetcher.Object, mockBroadcaster.Object);
```

### Test Isolation

- Each test uses a unique in-memory database (via `Guid.NewGuid()`)
- Databases are disposed after each test
- No shared state between tests
- Tests can run in parallel

## CI/CD Integration

GitHub Actions workflow (`.github/workflows/docker-build.yml`):

1. **Test Job** runs on every push and PR:
   - Restores dependencies
   - Builds solution
   - Runs tests with coverage collection
   - Generates coverage report with `irongut/CodeCoverageSummary`
   - Posts coverage summary as PR comment

2. **Build Job** runs after tests pass:
   - Builds multi-platform Docker images (amd64, arm64)
   - Pushes to GitHub Container Registry

**Current Coverage Target**: 60-80% (industry standard for well-tested applications)

## Hash Verification Purpose

The hash-based tests in `TranslatedVenstarSensorTests` serve multiple purposes:

1. **Regression Detection**: Any unintended changes to protobuf structure will immediately fail tests
2. **Cross-Platform Consistency**: Ensures packets are identical across different environments
3. **Protocol Validation**: Confirms the exact bytes being sent to Venstar thermostats
4. **Documentation**: The expected hash serves as a reference for the exact packet format

## Updating Expected Hash

If you intentionally modify the protobuf structure:

1. Run the tests and note the new hash from the test output
2. Update the `expectedHash` constant in the test
3. Document the reason for the change in commit message

## Dependencies

```xml
<ItemGroup>
  <PackageReference Include="coverlet.collector" Version="6.0.2" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  <PackageReference Include="Moq" Version="4.20.72" />
  <PackageReference Include="xunit" Version="2.9.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
</ItemGroup>
```

## Notes

- Tests use `macPrefix = "428e0486d8"` for consistency
- Sequence wraparound occurs at 65000 (not 65001)
- Pairing packets reset sequence to 1
- Temperature values must be within valid range for the lookup tables
- All tests run in milliseconds - entire suite completes in ~350ms
