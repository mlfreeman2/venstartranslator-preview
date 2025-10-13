# Unit Testing Example

VenstarTranslator now has comprehensive unit test coverage using xUnit and Moq. The architecture has been refactored to use dependency injection, making all components easily testable.

## Test Coverage Summary

- **Overall Coverage**: 64%
- **Total Tests**: 83 passing tests
- **Test Files**: 4 test files covering different layers

### Coverage by Component

| Component | Coverage | Tests |
|-----------|----------|-------|
| **Validation Attributes** | 100% | 32 tests |
| **TranslatedVenstarSensor** | 94% | 7 tests |
| **VenstarTranslatorDataCache** | 94% | (via integration tests) |
| **HttpDocumentFetcher** | 89% | 18 tests |
| **APIController** | 84% | 26 tests |
| **SensorOperations** | 77% | (via integration tests) |

## Example Test Setup

```csharp
using Moq;
using Xunit;
using VenstarTranslator.Models;
using VenstarTranslator.Services;

public class SensorOperationsTests
{
    [Fact]
    public void GetDocument_ShouldCallDocumentFetcherWithCorrectParameters()
    {
        // Arrange
        var mockFetcher = new Mock<IHttpDocumentFetcher>();
        var mockBroadcaster = new Mock<IUdpBroadcaster>();

        mockFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"temperature\": 72.5}");

        var sensorOps = new SensorOperations(mockFetcher.Object, mockBroadcaster.Object);

        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Living Room",
            URL = "http://172.17.21.11/get_livedata_info",
            IgnoreSSLErrors = false,
            JSONPath = "$.ch_aisle[?(@.name=='Living Room')].temp",
            Headers = new List<DataSourceHttpHeader>()
        };

        // Act
        var result = sensorOps.GetDocument(sensor);

        // Assert
        Assert.Equal("{\"temperature\": 72.5}", result);
        mockFetcher.Verify(f => f.FetchDocument("http://172.17.21.11/get_livedata_info", false, It.IsAny<List<DataSourceHttpHeader>>()), Times.Once);
    }

    [Fact]
    public void SendDataPacket_ShouldBroadcastUdpPacket()
    {
        // Arrange
        var mockFetcher = new Mock<IHttpDocumentFetcher>();
        var mockBroadcaster = new Mock<IUdpBroadcaster>();

        mockFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"temperature\": 72}");

        var sensorOps = new SensorOperations(mockFetcher.Object, mockBroadcaster.Object);

        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Living Room",
            URL = "http://172.17.21.11/get_livedata_info",
            IgnoreSSLErrors = false,
            JSONPath = "$.ch_aisle[?(@.name=='Living Room')].temp",
            Scale = TemperatureScale.F,
            Purpose = SensorPurpose.Remote,
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 1
        };

        // Act
        sensorOps.SendDataPacket(sensor);

        // Assert
        mockBroadcaster.Verify(b => b.Broadcast(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void GetLatestReading_ShouldFetchAndExtractValue()
    {
        // Arrange
        var mockFetcher = new Mock<IHttpDocumentFetcher>();
        var mockBroadcaster = new Mock<IUdpBroadcaster>();

        mockFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"ch_temp\": [{\"name\": \"Fridge\", \"temp\": \"35.8\"}]}");

        var sensorOps = new SensorOperations(mockFetcher.Object, mockBroadcaster.Object);

        var sensor = new TranslatedVenstarSensor
        {
            URL = "http://172.17.21.11/get_livedata_info",
            JSONPath = "$.ch_temp[?(@.name=='Fridge')].temp",
            Headers = new List<DataSourceHttpHeader>()
        };

        // Act
        var result = sensorOps.GetLatestReading(sensor);

        // Assert
        Assert.Equal(35.8, result);
    }
}

public class TranslatedVenstarSensorTests
{
    [Fact]
    public void ExtractValue_ShouldParseTemperatureFromJson()
    {
        // Arrange
        var sensor = new TranslatedVenstarSensor
        {
            JSONPath = "$.common_list[?(@.id=='0x02')].val"
        };
        var json = "{\"common_list\": [{\"id\": \"0x02\", \"val\": \"45.3\"}]}";

        // Act
        var result = sensor.ExtractValue(json);

        // Assert
        Assert.Equal(45.3, result);
    }

    [Fact]
    public void BuildDataPacket_ShouldReturnSerializedBytes()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Living Room",
            Scale = TemperatureScale.F,
            Purpose = SensorPurpose.Remote,
            Sequence = 1
        };

        // Act
        var result = sensor.BuildDataPacket(71.6);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void BuildPairingPacket_ShouldReturnSerializedBytes()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Outside",
            Scale = TemperatureScale.F,
            Purpose = SensorPurpose.Outdoor,
            Sequence = 0
        };

        // Act
        var result = sensor.BuildPairingPacket(45.3);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal(1, sensor.Sequence); // Pairing should reset sequence to 1
    }

    [Fact]
    public void SendPairingPacket_ShouldFetchTemperatureAndBroadcast()
    {
        // Arrange
        var mockFetcher = new Mock<IHttpDocumentFetcher>();
        var mockBroadcaster = new Mock<IUdpBroadcaster>();

        mockFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"wh25\": [{\"intemp\": \"72.3\"}]}");

        var sensorOps = new SensorOperations(mockFetcher.Object, mockBroadcaster.Object);

        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "GW2000 Onboard",
            URL = "http://172.17.21.11/get_livedata_info",
            JSONPath = "$.wh25[0].intemp",
            Scale = TemperatureScale.F,
            Purpose = SensorPurpose.Remote,
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 0
        };

        // Act
        sensorOps.SendPairingPacket(sensor);

        // Assert
        mockFetcher.Verify(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()), Times.Once);
        mockBroadcaster.Verify(b => b.Broadcast(It.IsAny<byte[]>()), Times.Once);
        Assert.Equal(1, sensor.Sequence); // Pairing should reset sequence to 1
    }
}
```

## Test Project Structure

The `VenstarTranslator.Tests` project contains:

### Test Files

1. **APIControllerTests.cs** (26 tests)
   - Tests all REST API endpoints (CRUD operations, pairing, reading)
   - Uses in-memory SQLite database for isolation
   - Mocks IHttpDocumentFetcher and IUdpBroadcaster
   - Uses real SensorOperations for integration testing

2. **TranslatedVenstarSensorTests.cs** (7 tests)
   - Tests protobuf packet building (data packets and pairing packets)
   - Tests JSON value extraction with JSONPath
   - Verifies temperature lookup table conversions
   - Validates packet determinism and sequence management

3. **HttpDocumentFetcherTests.cs** (18 tests)
   - Comprehensive error handling tests
   - HTTP status code scenarios (400, 401, 403, 404, 405, 500, 502, 503)
   - Network errors (timeouts, connection refused, SSL failures)
   - Invalid response handling
   - Custom header support

4. **ValidationAttributeTests.cs** (32 tests)
   - ValidAbsoluteUrlAttribute (10 tests)
   - ValidHttpHeadersAttribute (13 tests)
   - ValidJsonPathAttribute (9 tests)

### Dependencies

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
  <PackageReference Include="Moq" Version="4.20.72" />
  <PackageReference Include="xunit" Version="2.9.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  <PackageReference Include="coverlet.collector" Version="6.0.2" />
</ItemGroup>
```

### Running Tests

```bash
# Run all tests
dotnet test VenstarTranslator.Tests.csproj

# Run with coverage
dotnet test VenstarTranslator.Tests.csproj --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ValidationAttributeTests"
```

## Architecture Benefits

1. **Separation of Concerns**:
   - `TranslatedVenstarSensor` is a data model with domain logic (packet building, value extraction)
   - `SensorOperations` orchestrates HTTP fetching, value extraction, and UDP broadcasting
   - `HttpDocumentFetcher` handles HTTP requests with comprehensive error handling
   - `IHttpDocumentFetcher` and `IUdpBroadcaster` provide clean abstractions for external dependencies

2. **Testability**:
   - No network calls in tests - mock HTTP and UDP operations
   - Test sensor logic independently from I/O operations
   - Integration testing approach - APIController tests use real SensorOperations with mocked dependencies
   - Easy assertions on method calls and parameters using Moq

3. **Maintainability**:
   - Clear dependencies visible through constructor injection
   - Validation logic centralized in custom attributes (ValidAbsoluteUrlAttribute, ValidHttpHeadersAttribute, ValidJsonPathAttribute)
   - Static methods excluded from coverage where appropriate (Program, UdpBroadcaster, Protobuf DTOs)
   - Comprehensive error messages guide users to fix configuration issues

4. **Flexibility**:
   - Can swap implementations (e.g., different HTTP client, TCP instead of UDP)
   - Can add logging, retry logic, or metrics at the service level
   - BuildDataPacket accepts temperature as parameter for better testability

## Code Coverage Exclusions

The following classes are marked with `[ExcludeFromCodeCoverage]`:
- **Program.cs** - Application entry point
- **UdpBroadcaster** - Network I/O (requires actual UDP sockets)
- **FetchUrlRequest** - Simple DTO with no logic
- **Protobuf Models** - Auto-generated protocol buffer classes (SensorMessage, SENSORDATA, INFO, etc.)
- **HangfireJobManager** - Thin wrapper around Hangfire static API
- **SensorOperations.SyncToJsonFile()** - File I/O operation

## CI/CD Integration

GitHub Actions workflow runs tests and generates coverage reports:
- Tests run on every push and pull request
- Coverage reports posted as PR comments
- Builds multi-platform Docker images (amd64, arm64)
- Current coverage target: 60-80% (industry standard for well-tested code)
