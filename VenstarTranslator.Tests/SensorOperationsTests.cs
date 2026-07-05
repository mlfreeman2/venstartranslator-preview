using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Services;
using Xunit;

namespace VenstarTranslator.Tests;

public class SensorOperationsTests
{
    private readonly Mock<IHttpDocumentFetcher> _mockFetcher;
    private readonly Mock<IUdpBroadcaster> _mockBroadcaster;
    private readonly SensorOperations _ops;

    public SensorOperationsTests()
    {
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        _mockFetcher = new Mock<IHttpDocumentFetcher>();
        _mockBroadcaster = new Mock<IUdpBroadcaster>();
        _ops = new SensorOperations(_mockFetcher.Object, _mockBroadcaster.Object);
    }

    private static TranslatedVenstarSensor CreateSensor()
    {
        return new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Test Sensor",
            Enabled = true,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.F,
            URL = "http://example.com/api",
            JSONPath = "$.temperature",
            Headers = new List<DataSourceHttpHeader>()
        };
    }

    [Fact]
    public void GetLatestReading_FetchesAndExtractsValue()
    {
        var sensor = CreateSensor();
        _mockFetcher
            .Setup(f => f.FetchDocument(sensor.URL, false, sensor.Headers))
            .Returns("{\"temperature\": 72.5}");

        var reading = _ops.GetLatestReading(sensor);

        Assert.Equal(72.5, reading);
    }

    [Fact]
    public void SendDataPacket_BroadcastsAndCachesPacketBytes()
    {
        var sensor = CreateSensor();
        _mockFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"temperature\": 72.5}");
        byte[] broadcasted = null;
        _mockBroadcaster.Setup(b => b.Broadcast(It.IsAny<byte[]>())).Callback<byte[]>(b => broadcasted = b);

        _ops.SendDataPacket(sensor);

        Assert.NotNull(broadcasted);
        Assert.NotEmpty(broadcasted);
        Assert.Same(broadcasted, sensor.LastPacketBytes);
    }

    [Fact]
    public void SendPairingPacket_Broadcasts()
    {
        var sensor = CreateSensor();
        _mockFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"temperature\": 72.5}");

        _ops.SendPairingPacket(sensor);

        _mockBroadcaster.Verify(b => b.Broadcast(It.Is<byte[]>(bytes => bytes.Length > 0)), Times.Once);
        Assert.Null(sensor.LastPacketBytes);
    }

    [Fact]
    public void ResendLastPacket_NoCachedPacket_Throws()
    {
        var sensor = CreateSensor();

        var ex = Assert.Throws<InvalidOperationException>(() => _ops.ResendLastPacket(sensor));

        Assert.Contains("has not broadcast", ex.Message);
        _mockBroadcaster.Verify(b => b.Broadcast(It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ResendLastPacket_EmptyCachedPacket_Throws()
    {
        var sensor = CreateSensor();
        sensor.LastPacketBytes = Array.Empty<byte>();

        Assert.Throws<InvalidOperationException>(() => _ops.ResendLastPacket(sensor));
    }

    [Fact]
    public void ResendLastPacket_WithCachedPacket_BroadcastsExactBytes()
    {
        var sensor = CreateSensor();
        sensor.LastPacketBytes = new byte[] { 9, 8, 7 };

        _ops.ResendLastPacket(sensor);

        _mockBroadcaster.Verify(
            b => b.Broadcast(It.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 9, 8, 7 }))),
            Times.Once);
    }
}
