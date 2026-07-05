using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using VenstarTranslator.Controllers;
using VenstarTranslator.Exceptions;
using VenstarTranslator.Models;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Services;
using Xunit;

namespace VenstarTranslator.Tests;

/// <summary>
/// Covers the settings endpoints, resend/fetchurl endpoints, and the
/// healthchecks.io check lifecycle side effects of sensor CRUD.
/// </summary>
public class APIControllerHealthChecksTests : IDisposable
{
    private readonly VenstarTranslatorDataCache _db;
    private readonly IConfiguration _config;
    private readonly Mock<IHttpDocumentFetcher> _mockDocumentFetcher;
    private readonly Mock<IUdpBroadcaster> _mockUdpBroadcaster;
    private readonly Mock<IHangfireJobManager> _mockJobManager;
    private readonly Mock<IHealthChecksClient> _mockHealthChecksClient;
    private readonly Mock<ISettingsService> _mockSettingsService;

    private const string ApiBase = "https://healthchecks.io/api/v3";

    public APIControllerHealthChecksTests()
    {
        var options = new DbContextOptionsBuilder<VenstarTranslatorDataCache>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new VenstarTranslatorDataCache(options);

        _mockDocumentFetcher = new Mock<IHttpDocumentFetcher>();
        _mockUdpBroadcaster = new Mock<IUdpBroadcaster>();
        _mockJobManager = new Mock<IHangfireJobManager>();
        _mockHealthChecksClient = new Mock<IHealthChecksClient>();
        _mockSettingsService = new Mock<ISettingsService>();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "SensorFilePath", System.IO.Path.GetTempFileName() }
            }!)
            .Build();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private API CreateController(SettingsDTO settings = null)
    {
        _mockSettingsService.Setup(s => s.GetSettings()).Returns(settings ?? new SettingsDTO());
        var sensorOps = new SensorOperations(_mockDocumentFetcher.Object, _mockUdpBroadcaster.Object);
        return new API(new Mock<ILogger<API>>().Object, _db, _config, sensorOps,
            _mockJobManager.Object, _mockSettingsService.Object, _mockHealthChecksClient.Object);
    }

    private static SettingsDTO SaasWithApiKey(string instanceName = "Attic") => new()
    {
        InstanceName = instanceName,
        HealthChecksMode = "saas",
        HealthChecksApiKey = "key-1"
    };

    private TranslatedVenstarSensor AddDbSensor(byte id = 0, string name = "Test Sensor", bool enabled = true, string uuid = null)
    {
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = id,
            Name = name,
            Enabled = enabled,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.F,
            URL = "http://example.com/api",
            JSONPath = "$.temperature",
            Headers = new List<DataSourceHttpHeader>(),
            HealthCheckUuid = uuid
        };
        _db.Sensors.Add(sensor);
        _db.SaveChanges();
        return sensor;
    }

    private static SensorJsonDTO BuildDTO(byte id = 0, string name = "Test Sensor", bool enabled = true, string uuid = null,
        SensorPurpose purpose = SensorPurpose.Remote)
    {
        return new SensorJsonDTO
        {
            SensorID = id,
            Name = name,
            Enabled = enabled,
            Purpose = purpose,
            Scale = TemperatureScale.F,
            URL = "http://example.com/api",
            JSONPath = "$.temperature",
            Headers = new List<DataSourceHttpHeaderDTO>(),
            HealthCheckUuid = uuid
        };
    }

    /// <summary>
    /// Polls a Moq Verify until it passes or times out, for the controller's
    /// fire-and-forget Task.Run healthchecks calls.
    /// </summary>
    private static void VerifyEventually(Action verify, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                verify();
                return;
            }
            catch (MockException) when (sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(25);
            }
        }
    }

    #region GetSettings

    [Fact]
    public void GetSettings_NoInstanceName_ReturnsDefaultName()
    {
        var controller = CreateController(new SettingsDTO());

        var result = controller.GetSettings();

        var json = Assert.IsType<JsonResult>(result);
        var settings = Assert.IsType<SettingsDTO>(json.Value);
        Assert.Equal("Venstar Sensor Emulator", settings.InstanceName);
    }

    [Fact]
    public void GetSettings_WithInstanceName_ReturnsConfiguredName()
    {
        var controller = CreateController(new SettingsDTO { InstanceName = "Attic" });

        var result = controller.GetSettings();

        var json = Assert.IsType<JsonResult>(result);
        var settings = Assert.IsType<SettingsDTO>(json.Value);
        Assert.Equal("Attic", settings.InstanceName);
    }

    #endregion

    #region UpdateSettings

    [Fact]
    public async Task UpdateSettings_SelfHostedWithoutUrl_Returns400()
    {
        var controller = CreateController();

        var result = await controller.UpdateSettings(new SettingsDTO { HealthChecksMode = "selfhosted" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        _mockSettingsService.Verify(s => s.SaveSettings(It.IsAny<SettingsDTO>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettings_ApiKeyWithoutInstanceName_Returns400()
    {
        var controller = CreateController();

        var result = await controller.UpdateSettings(new SettingsDTO
        {
            HealthChecksMode = "saas",
            HealthChecksApiKey = "key-1"
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        _mockSettingsService.Verify(s => s.SaveSettings(It.IsAny<SettingsDTO>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettings_BlankMode_NormalizesToNone()
    {
        var controller = CreateController();
        SettingsDTO saved = null;
        _mockSettingsService.Setup(s => s.SaveSettings(It.IsAny<SettingsDTO>()))
            .Callback<SettingsDTO>(s => saved = s);

        var result = await controller.UpdateSettings(new SettingsDTO { InstanceName = "  Attic  " });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("none", saved.HealthChecksMode);
        Assert.Equal("Attic", saved.InstanceName);
    }

    [Fact]
    public async Task UpdateSettings_ModeIsTrimmedAndLowercased()
    {
        var controller = CreateController();
        SettingsDTO saved = null;
        _mockSettingsService.Setup(s => s.SaveSettings(It.IsAny<SettingsDTO>()))
            .Callback<SettingsDTO>(s => saved = s);

        await controller.UpdateSettings(new SettingsDTO
        {
            InstanceName = "Attic",
            HealthChecksMode = " SaaS "
        });

        Assert.Equal("saas", saved.HealthChecksMode);
    }

    [Fact]
    public async Task UpdateSettings_WithApiKey_BackfillsChecksForSensorsWithoutUuid()
    {
        AddDbSensor(0, "No UUID");
        AddDbSensor(1, "Has UUID", uuid: "existing-uuid");
        var controller = CreateController(new SettingsDTO { InstanceName = "Attic" });
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(ApiBase, "key-1", It.IsAny<string>(), 60, 300))
            .ReturnsAsync("fresh-uuid");

        var result = await controller.UpdateSettings(SaasWithApiKey());

        Assert.IsType<OkObjectResult>(result);
        _mockHealthChecksClient.Verify(
            c => c.CreateCheckAsync(ApiBase, "key-1", "Venstar Translator - Attic - [0] No UUID", 60, 300),
            Times.Once);
        _mockHealthChecksClient.Verify(
            c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(n => n.Contains("Has UUID")), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
        Assert.Equal("fresh-uuid", _db.Sensors.Single(s => s.SensorID == 0).HealthCheckUuid);
    }

    [Fact]
    public async Task UpdateSettings_BackfillOnDisabledSensor_PausesCheck()
    {
        AddDbSensor(0, "Disabled", enabled: false);
        var controller = CreateController(new SettingsDTO { InstanceName = "Attic" });
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync("fresh-uuid");

        await controller.UpdateSettings(SaasWithApiKey());

        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.PauseCheckAsync(ApiBase, "key-1", "fresh-uuid"), Times.Once));
    }

    [Fact]
    public async Task UpdateSettings_BackfillCreateFails_LeavesSensorWithoutUuid()
    {
        AddDbSensor(0, "No UUID");
        var controller = CreateController(new SettingsDTO { InstanceName = "Attic" });
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((string)null);

        var result = await controller.UpdateSettings(SaasWithApiKey());

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(_db.Sensors.Single(s => s.SensorID == 0).HealthCheckUuid);
    }

    [Fact]
    public async Task UpdateSettings_InstanceNameChanged_RenamesExistingChecks()
    {
        AddDbSensor(0, "Sensor A", uuid: "uuid-a");
        var controller = CreateController(new SettingsDTO { InstanceName = "Old Name" });
        _mockHealthChecksClient
            .Setup(c => c.RenameCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await controller.UpdateSettings(SaasWithApiKey("New Name"));

        _mockHealthChecksClient.Verify(
            c => c.RenameCheckAsync(ApiBase, "key-1", "uuid-a", "Venstar Translator - New Name - [0] Sensor A"),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSettings_InstanceNameUnchanged_DoesNotRename()
    {
        AddDbSensor(0, "Sensor A", uuid: "uuid-a");
        var controller = CreateController(new SettingsDTO { InstanceName = "Attic" });

        await controller.UpdateSettings(SaasWithApiKey("Attic"));

        _mockHealthChecksClient.Verify(
            c => c.RenameCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSettings_NoApiKey_SkipsBackfill()
    {
        AddDbSensor(0, "No UUID");
        var controller = CreateController();

        var result = await controller.UpdateSettings(new SettingsDTO { HealthChecksMode = "saas", InstanceName = "Attic" });

        Assert.IsType<OkObjectResult>(result);
        _mockHealthChecksClient.Verify(
            c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    #endregion

    #region AddSensor healthchecks lifecycle

    [Fact]
    public async Task AddSensor_UuidWithoutHealthChecksConfigured_Returns400()
    {
        var controller = CreateController();

        var result = await controller.AddSensor(BuildDTO(uuid: "manual-uuid"));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddSensor_WithApiKey_AutoCreatesCheck()
    {
        var controller = CreateController(SaasWithApiKey());
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(ApiBase, "key-1", "Venstar Translator - Attic - [0] Test Sensor", 60, 300))
            .ReturnsAsync("auto-uuid");

        var result = await controller.AddSensor(BuildDTO());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("auto-uuid", _db.Sensors.Single(s => s.SensorID == 0).HealthCheckUuid);
    }

    [Fact]
    public async Task AddSensor_OutdoorPurpose_UsesOutdoorSchedule()
    {
        var controller = CreateController(SaasWithApiKey());
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 300, 1200))
            .ReturnsAsync("auto-uuid");

        await controller.AddSensor(BuildDTO(purpose: SensorPurpose.Outdoor));

        _mockHealthChecksClient.Verify(
            c => c.CreateCheckAsync(ApiBase, "key-1", It.IsAny<string>(), 300, 1200), Times.Once);
    }

    [Fact]
    public async Task AddSensor_DisabledSensor_PausesAutoCreatedCheck()
    {
        var controller = CreateController(SaasWithApiKey());
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync("auto-uuid");

        await controller.AddSensor(BuildDTO(enabled: false));

        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.PauseCheckAsync(ApiBase, "key-1", "auto-uuid"), Times.Once));
    }

    [Fact]
    public async Task AddSensor_CheckCreationFails_StillSucceeds()
    {
        var controller = CreateController(SaasWithApiKey());
        _mockHealthChecksClient
            .Setup(c => c.CreateCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((string)null);

        var result = await controller.AddSensor(BuildDTO());

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(_db.Sensors.Single(s => s.SensorID == 0).HealthCheckUuid);
    }

    #endregion

    #region UpdateSensor healthchecks lifecycle

    [Fact]
    public void UpdateSensor_UuidWithoutHealthChecksConfigured_Returns400()
    {
        AddDbSensor();
        var controller = CreateController();

        var result = controller.UpdateSensor(BuildDTO(uuid: "manual-uuid"));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public void UpdateSensor_Disabling_PausesCheck()
    {
        AddDbSensor(enabled: true, uuid: "uuid-1");
        var controller = CreateController(SaasWithApiKey());

        controller.UpdateSensor(BuildDTO(enabled: false, uuid: "uuid-1"));

        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.PauseCheckAsync(ApiBase, "key-1", "uuid-1"), Times.Once));
    }

    [Fact]
    public void UpdateSensor_Enabling_UnpausesCheck()
    {
        AddDbSensor(enabled: false, uuid: "uuid-1");
        var controller = CreateController(SaasWithApiKey());

        controller.UpdateSensor(BuildDTO(enabled: true, uuid: "uuid-1"));

        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.UnpauseCheckAsync(ApiBase, "key-1", "uuid-1"), Times.Once));
    }

    [Fact]
    public void UpdateSensor_Renamed_RenamesCheck()
    {
        AddDbSensor(name: "Old Name", uuid: "uuid-1");
        var controller = CreateController(SaasWithApiKey());

        controller.UpdateSensor(BuildDTO(name: "New Name", uuid: "uuid-1"));

        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.RenameCheckAsync(ApiBase, "key-1", "uuid-1", "Venstar Translator - Attic - [0] New Name"),
            Times.Once));
    }

    [Fact]
    public void UpdateSensor_PurposeChanged_UpdatesCheckSchedule()
    {
        AddDbSensor(uuid: "uuid-1");
        var controller = CreateController(SaasWithApiKey());

        controller.UpdateSensor(BuildDTO(purpose: SensorPurpose.Outdoor, uuid: "uuid-1"));

        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.UpdateCheckScheduleAsync(ApiBase, "key-1", "uuid-1", 300, 1200), Times.Once));
    }

    [Fact]
    public void UpdateSensor_NothingRelevantChanged_NoHealthChecksCalls()
    {
        AddDbSensor(uuid: "uuid-1");
        var controller = CreateController(SaasWithApiKey());

        controller.UpdateSensor(BuildDTO(uuid: "uuid-1"));

        _mockHealthChecksClient.VerifyNoOtherCalls();
    }

    #endregion

    #region DeleteSensor healthchecks lifecycle

    [Fact]
    public void DeleteSensor_WithUuidAndApiKey_DeletesCheck()
    {
        AddDbSensor(uuid: "uuid-1");
        var controller = CreateController(SaasWithApiKey());

        var result = controller.DeleteSensor(0);

        Assert.IsType<OkObjectResult>(result);
        VerifyEventually(() => _mockHealthChecksClient.Verify(
            c => c.DeleteCheckAsync(ApiBase, "key-1", "uuid-1"), Times.Once));
    }

    [Fact]
    public void DeleteSensor_WithoutUuid_DoesNotCallDelete()
    {
        AddDbSensor();
        var controller = CreateController(SaasWithApiKey());

        controller.DeleteSensor(0);

        _mockHealthChecksClient.Verify(
            c => c.DeleteCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region ResendLastPacket

    [Fact]
    public void ResendLastPacket_SensorNotFound_Returns404()
    {
        var controller = CreateController();

        var result = controller.ResendLastPacket(99);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
    }

    [Fact]
    public void ResendLastPacket_NoCachedPacket_Returns400()
    {
        AddDbSensor();
        var controller = CreateController();

        var result = controller.ResendLastPacket(0);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        var message = Assert.IsType<MessageResponse>(objectResult.Value);
        Assert.Contains("has not broadcast", message.Message);
    }

    [Fact]
    public void ResendLastPacket_WithCachedPacket_BroadcastsExactBytes()
    {
        var sensor = AddDbSensor();
        sensor.LastPacketBytes = new byte[] { 1, 2, 3, 4 };
        _db.SaveChanges();
        var controller = CreateController();

        var result = controller.ResendLastPacket(0);

        Assert.IsType<JsonResult>(result);
        _mockUdpBroadcaster.Verify(
            b => b.Broadcast(It.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }))),
            Times.Once);
    }

    #endregion

    #region FetchUrl

    [Fact]
    public void FetchUrl_MissingUrl_Returns400()
    {
        var controller = CreateController();

        var result = controller.FetchUrl(new FetchUrlRequest { Url = " " });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public void FetchUrl_UrlNotConfiguredOnAnySensor_Returns400()
    {
        var controller = CreateController();

        var result = controller.FetchUrl(new FetchUrlRequest { Url = "http://unknown.example.com" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public void FetchUrl_ConfiguredUrl_ReturnsDocument()
    {
        AddDbSensor();
        _mockDocumentFetcher
            .Setup(f => f.FetchDocument("http://example.com/api", false, It.IsAny<List<DataSourceHttpHeader>>()))
            .Returns("{\"temperature\": 72.5}");
        var controller = CreateController();

        var result = controller.FetchUrl(new FetchUrlRequest { Url = "http://example.com/api" });

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("{\"temperature\": 72.5}", content.Content);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public void FetchUrl_FetcherThrowsVenstarTranslatorException_Returns400()
    {
        AddDbSensor();
        _mockDocumentFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Throws(new VenstarTranslatorException("Request timed out"));
        var controller = CreateController();

        var result = controller.FetchUrl(new FetchUrlRequest { Url = "http://example.com/api" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public void FetchUrl_FetcherThrowsUnexpectedException_Returns500()
    {
        AddDbSensor();
        _mockDocumentFetcher
            .Setup(f => f.FetchDocument(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<DataSourceHttpHeader>>()))
            .Throws(new InvalidOperationException("boom"));
        var controller = CreateController();

        var result = controller.FetchUrl(new FetchUrlRequest { Url = "http://example.com/api" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    #endregion
}
