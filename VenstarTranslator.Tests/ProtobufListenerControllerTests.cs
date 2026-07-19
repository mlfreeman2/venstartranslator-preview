using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;

using Newtonsoft.Json;

using VenstarTranslator.Controllers;
using VenstarTranslator.Models;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Models.ProtobufCapture;
using VenstarTranslator.Services;

using Xunit;

namespace VenstarTranslator.Tests;

public class ProtobufListenerControllerTests
{
    private static ProtobufListenerController NewController(IProtobufCaptureService svc)
    {
        return new ProtobufListenerController(svc, Mock.Of<ILogger<ProtobufListenerController>>());
    }

    // ---- start / stop / status / messages happy paths ----

    [Fact]
    public void Start_Success_ReturnsStatus()
    {
        var mock = new Mock<IProtobufCaptureService>();
        mock.Setup(s => s.Start()).Returns(new CaptureStatus { Running = true, Port = 5001, LastId = 7 });

        var result = NewController(mock.Object).Start() as JsonResult;

        var status = Assert.IsType<CaptureStatus>(result.Value);
        Assert.True(status.Running);
        Assert.Equal(7, status.LastId);
    }

    [Fact]
    public void Start_BindFailure_Returns409()
    {
        var mock = new Mock<IProtobufCaptureService>();
        mock.Setup(s => s.Start()).Returns(new CaptureStatus { Failed = true, Error = "port in use" });

        var result = NewController(mock.Object).Start() as ObjectResult;

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("port in use", Assert.IsType<MessageResponse>(result.Value).Message);
    }

    [Fact]
    public void Stop_ReturnsStatus()
    {
        var mock = new Mock<IProtobufCaptureService>();
        mock.Setup(s => s.Stop()).Returns(new CaptureStatus { Running = false, CapturedCount = 3 });

        var result = NewController(mock.Object).Stop() as JsonResult;

        Assert.Equal(3, Assert.IsType<CaptureStatus>(result.Value).CapturedCount);
    }

    [Fact]
    public void Status_ReturnsStatus()
    {
        var mock = new Mock<IProtobufCaptureService>();
        mock.Setup(s => s.GetStatus()).Returns(new CaptureStatus { Running = true, Port = 5001 });

        var result = NewController(mock.Object).Status() as JsonResult;

        Assert.True(Assert.IsType<CaptureStatus>(result.Value).Running);
    }

    [Fact]
    public void Messages_ReturnsPage()
    {
        var mock = new Mock<IProtobufCaptureService>();
        mock.Setup(s => s.GetMessagesAfter(5, 500))
            .Returns(new MessagesPage { Running = true, LastId = 9, Messages = Array.Empty<CapturedMessage>() });

        var result = NewController(mock.Object).Messages(5, 500) as JsonResult;

        Assert.Equal(9, Assert.IsType<MessagesPage>(result.Value).LastId);
    }

    // ---- export ----

    [Fact]
    public void Export_EmptyBuffer_Returns409()
    {
        using var svc = NewRealService();
        var result = NewController(svc).Export() as ObjectResult;

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void Export_ProducesCamelCaseCaptureFile()
    {
        using var svc = NewRealService();
        svc.Capture(BuildDataPacket(72.0, out _), new IPEndPoint(IPAddress.Loopback, 5001));

        var result = NewController(svc).Export() as FileContentResult;

        Assert.NotNull(result);
        Assert.StartsWith("venstar-capture-", result.FileDownloadName);
        var json = Encoding.UTF8.GetString(result.FileContents);
        Assert.Contains("\"format\": \"venstar-protobuf-capture/1\"", json);
        Assert.Contains("\"packets\"", json);
        Assert.Contains("\"hex\"", json);
    }

    // ---- import ----

    [Fact]
    public void Import_RoundTripsToIdenticalDecode()
    {
        using var svc = NewRealService();
        var packet = BuildDataPacket(72.0, out var sensor);
        svc.Capture(packet, new IPEndPoint(IPAddress.Loopback, 5001));

        var liveSummary = svc.GetMessagesAfter(0, 100).Messages.Single().Summary;

        // Export via the controller, then feed the file straight back into import.
        var file = NewController(svc).Export() as FileContentResult;
        var captureFile = JsonConvert.DeserializeObject<CaptureExport>(Encoding.UTF8.GetString(file.FileContents));

        var result = NewController(svc).Import(captureFile) as JsonResult;
        var imported = Assert.IsType<ImportResult>(result.Value);

        Assert.Single(imported.Messages);
        Assert.Equal(0, imported.Skipped);
        var importedSummary = imported.Messages[0].Summary;
        Assert.Equal(liveSummary.Mac, importedSummary.Mac);
        Assert.Equal(liveSummary.TemperatureIndex, importedSummary.TemperatureIndex);
        Assert.Equal(liveSummary.Sequence, importedSummary.Sequence);
        // fresh per-view ids start at 1
        Assert.Equal(1, imported.Messages[0].Id);
    }

    [Fact]
    public void Import_UnknownFormatMajor_Returns400()
    {
        using var svc = NewRealService();
        var file = new CaptureExport { Format = "venstar-protobuf-capture/2", Packets = new List<CaptureExportPacket>() };

        var result = NewController(svc).Import(file) as ObjectResult;

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void Import_MalformedHexEntries_AreSkippedAndCounted()
    {
        using var svc = NewRealService();
        var goodHex = Convert.ToHexString(BuildDataPacket(70.0, out _)).ToLowerInvariant();
        var file = new CaptureExport
        {
            Format = "venstar-protobuf-capture/1",
            Packets = new List<CaptureExportPacket>
            {
                new() { Hex = goodHex, Source = "1.2.3.4:5" },
                new() { Hex = "zzzz", Source = "1.2.3.4:5" },       // not hex
                new() { Hex = "", Source = "1.2.3.4:5" },           // empty
            },
        };

        var result = NewController(svc).Import(file) as JsonResult;
        var imported = Assert.IsType<ImportResult>(result.Value);

        Assert.Single(imported.Messages);
        Assert.Equal(2, imported.Skipped);
    }

    [Fact]
    public void Import_OversizePacketCount_Returns400()
    {
        using var svc = NewRealService();
        var packets = Enumerable.Range(0, 5001)
            .Select(_ => new CaptureExportPacket { Hex = "082a", Source = "x" })
            .ToList();
        var file = new CaptureExport { Format = "venstar-protobuf-capture/1", Packets = packets };

        var result = NewController(svc).Import(file) as ObjectResult;

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void Import_LeavesCaptureStateUntouched()
    {
        using var svc = NewRealService();
        svc.Capture(BuildDataPacket(72.0, out _), new IPEndPoint(IPAddress.Loopback, 5001));
        var before = svc.GetStatus();

        var file = new CaptureExport
        {
            Format = "venstar-protobuf-capture/1",
            Packets = new List<CaptureExportPacket> { new() { Hex = "082a", Source = "x" } },
        };
        NewController(svc).Import(file);

        var after = svc.GetStatus();
        Assert.Equal(before.CapturedCount, after.CapturedCount);
        Assert.Equal(before.LastId, after.LastId);
    }

    // ---- helpers ----

    private static ProtobufCaptureService NewRealService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { { "ProtobufListenerPort", "0" } }!)
            .Build();
        return new ProtobufCaptureService(config, Mock.Of<ILogger<ProtobufCaptureService>>());
    }

    private static byte[] BuildDataPacket(double temp, out TranslatedVenstarSensor sensor)
    {
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Import Test",
            Enabled = true,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.F,
            URL = "http://example.com",
            JSONPath = "$.t",
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 10,
        };
        return sensor.BuildDataPacket(temp);
    }
}
