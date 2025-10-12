# Unit Testing Example

With the refactored architecture, `GetDocument()` and `Send()` have been moved out of `TranslatedVenstarSensor` into the `SensorOperations` service. You can now write unit tests using mocks. Here's an example using Moq:

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

## Dependencies

Add these to your test project:

```xml
<ItemGroup>
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="xunit" Version="2.6.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
</ItemGroup>
```

## Architecture Benefits

1. **Separation of Concerns**:
   - `TranslatedVenstarSensor` is now a pure data model with packet building logic
   - `SensorOperations` handles orchestration of HTTP fetching, value extraction, and UDP broadcasting
   - `IHttpDocumentFetcher` and `IUdpBroadcaster` provide clean abstractions for external dependencies

2. **Testability**:
   - No network calls in tests - mock HTTP and UDP operations
   - Test sensor logic independently from I/O operations
   - Easy assertions on method calls and parameters

3. **Maintainability**:
   - Clear dependencies visible through constructor injection
   - Easy to add new operations or modify existing ones
   - Centralized service for all sensor operations

4. **Flexibility**:
   - Can swap implementations (e.g., different HTTP client, TCP instead of UDP)
   - Can add logging, retry logic, or metrics at the service level
   - BuildDataPacket accepts temperature as parameter for better testability
