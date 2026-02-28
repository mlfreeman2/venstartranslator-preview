using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using VenstarTranslator.Exceptions;
using VenstarTranslator.Models;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Services;

namespace VenstarTranslator.Controllers;

[ApiController]
public class API : ControllerBase
{
    private readonly ILogger<API> _logger;

    private readonly VenstarTranslatorDataCache _db;

    private readonly IConfiguration _config;

    private readonly ISensorOperations _sensorOperations;

    private readonly IHangfireJobManager _jobManager;

    private readonly ISettingsService _settingsService;

    private readonly IHealthChecksClient _healthChecksClient;

    public API(ILogger<API> logger, VenstarTranslatorDataCache db, IConfiguration config, ISensorOperations sensorOperations, IHangfireJobManager jobManager, ISettingsService settingsService, IHealthChecksClient healthChecksClient)
    {
        _logger = logger;
        _db = db;
        _config = config;
        _sensorOperations = sensorOperations;
        _jobManager = jobManager;
        _settingsService = settingsService;
        _healthChecksClient = healthChecksClient;
    }

    [HttpGet]
    [Route("/api/sensors/{id}/pair")]
    public ActionResult SendPairingPacket(uint id)
    {
        var sensor = _db.Sensors.Include(a => a.Headers).FirstOrDefault(a => a.SensorID == id);
        if (sensor == null)
        {
            return StatusCode(404, new MessageResponse { Message = "Sensor not found." });
        }

        try
        {
            _sensorOperations.SendPairingPacket(sensor);
            _db.SaveChanges();

            return new JsonResult(new MessageResponse { Message = "Pairing packet sent." });
        }
        catch (VenstarTranslatorException e)
        {
            return StatusCode(400, new MessageResponse { Message = e.Message });
        }
    }

    [HttpPost]
    [Route("/api/sensors/{id}/resend")]
    public ActionResult ResendLastPacket(uint id)
    {
        var sensor = _db.Sensors.FirstOrDefault(a => a.SensorID == id);
        if (sensor == null)
        {
            return StatusCode(404, new MessageResponse { Message = "Sensor not found." });
        }

        try
        {
            _sensorOperations.ResendLastPacket(sensor);
            return new JsonResult(new MessageResponse { Message = "Last packet resent successfully." });
        }
        catch (InvalidOperationException e)
        {
            return StatusCode(400, new MessageResponse { Message = e.Message });
        }
        catch (VenstarTranslatorException e)
        {
            return StatusCode(400, new MessageResponse { Message = e.Message });
        }
    }



    [HttpGet]
    [Route("/api/sensors/{id}/latest")]
    public ActionResult GetReading(uint id)
    {
        var sensor = _db.Sensors.Include(a => a.Headers).FirstOrDefault(a => a.SensorID == id);
        if (sensor == null)
        {
            return StatusCode(404, new MessageResponse { Message = "Sensor not found." });
        }
        try
        {
            var reading = _sensorOperations.GetLatestReading(sensor);
            return new JsonResult(new TemperatureResponse { Temperature = reading, Scale = sensor.Scale });
        }
        catch (VenstarTranslatorException e)
        {
            return StatusCode(400, new MessageResponse { Message = e.Message });
        }
    }

    [HttpGet]
    [Route("/api/sensors")]
    public ActionResult ListSensors()
    {
        var sensors = _db.Sensors.Include(a => a.Headers).ToList();
        var sensorDTOs = sensors.Select(s => SensorWebDTO.FromSensor(s)).ToList();
        return new JsonResult(sensorDTOs);
    }

    [HttpPut]
    [Route("/api/sensors")]
    public ActionResult UpdateSensor(SensorJsonDTO updatedDTO)
    {
        if (!_db.Sensors.Any(a => a.SensorID == updatedDTO.SensorID))
        {
            return StatusCode(404, new MessageResponse { Message = "Sensor not found." });
        }

        // update working db
        var current = _db.Sensors.Include(a => a.Headers).Single(a => a.SensorID == updatedDTO.SensorID);

        // Capture old state for healthchecks.io management
        string oldName = current.Name;
        SensorPurpose oldPurpose = current.Purpose;

        // Track if enabled state is changing to reset problem tracking
        bool enabledStateChanged = current.Enabled != updatedDTO.Enabled;

        current.Name = updatedDTO.Name;
        current.Enabled = updatedDTO.Enabled;
        current.URL = updatedDTO.URL;
        current.Purpose = updatedDTO.Purpose;
        current.JSONPath = updatedDTO.JSONPath;
        current.Scale = updatedDTO.Scale;
        current.IgnoreSSLErrors = updatedDTO.IgnoreSSLErrors;
        current.HealthCheckUuid = updatedDTO.HealthCheckUuid;

        // Clear LastSuccessfulBroadcast when enabled state changes to start fresh
        if (enabledStateChanged)
        {
            current.LastSuccessfulBroadcast = null;
        }

        current.Headers.Clear();
        _db.SaveChanges();

        if (updatedDTO.Headers != null && updatedDTO.Headers.Any())
        {
            current.Headers.AddRange(updatedDTO.Headers.Select(h => h.ToHeader()));
        }
        _db.SaveChanges();

        SensorOperations.SyncToJsonFile(_config, _db);

        if (current.Enabled)
        {
            _jobManager.AddOrUpdateRecurringJob(current.HangfireJobName, current.CronExpression, current.SensorID);
        }
        else
        {
            _jobManager.RemoveRecurringJob(current.HangfireJobName);
        }

        // healthchecks.io management API side effects
        var (apiBase, apiKey) = GetManagementApiConfig();
        if (apiBase != null && !string.IsNullOrEmpty(current.HealthCheckUuid))
        {
            var uuid = current.HealthCheckUuid;
            var instanceName = _settingsService.GetSettings().InstanceName;

            // Pause/unpause when enabled state changed
            if (enabledStateChanged)
            {
                if (current.Enabled)
                {
                    _ = Task.Run(() => _healthChecksClient.UnpauseCheckAsync(apiBase, apiKey, uuid));
                }
                else
                {
                    _ = Task.Run(() => _healthChecksClient.PauseCheckAsync(apiBase, apiKey, uuid));
                }
            }

            // Smart rename: only rename if check still has the convention name
            if (current.Name != oldName)
            {
                _ = Task.Run(async () =>
                {
                    var expectedOldName = BuildCheckName(instanceName, current.SensorID, oldName);
                    var actualName = await _healthChecksClient.GetCheckNameAsync(apiBase, apiKey, uuid);
                    if (actualName == expectedOldName)
                    {
                        var newConventionName = BuildCheckName(instanceName, current.SensorID, current.Name);
                        await _healthChecksClient.RenameCheckAsync(apiBase, apiKey, uuid, newConventionName);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Skipping rename for sensor {SensorID}: check name '{ActualName}' doesn't match convention '{ExpectedName}'",
                            current.SensorID, actualName, expectedOldName);
                    }
                });
            }

            // Update schedule if purpose changed
            if (current.Purpose != oldPurpose)
            {
                var (timeout, grace) = GetCheckSchedule(current.Purpose);
                _ = Task.Run(() => _healthChecksClient.UpdateCheckScheduleAsync(apiBase, apiKey, uuid, timeout, grace));
            }
        }

        return Ok(new MessageResponse { Message = "Successful!" });
    }

    [HttpPost]
    [Route("/api/sensors")]
    public async Task<ActionResult> AddSensor(SensorJsonDTO sensorDTO)
    {
        byte sensorID = 20;
        for (byte i = 0; i <= 19; i++)
        {
            if (!_db.Sensors.Any(a => a.SensorID == i))
            {
                sensorID = i;
                break;
            }
        }

        if (sensorID > 19)
        {
            return StatusCode(400, new MessageResponse { Message = "No sensor IDs available. Delete some sensors first." });
        }

        sensorDTO.SensorID = sensorID;
        var sensor = sensorDTO.ToSensor();

        _db.Sensors.Add(sensor);
        _db.SaveChanges();
        SensorOperations.SyncToJsonFile(_config, _db);

        if (sensor.Enabled)
        {
            _jobManager.AddOrUpdateRecurringJob(sensor.HangfireJobName, sensor.CronExpression, sensor.SensorID);
        }

        // Auto-create healthchecks.io check if management API is configured
        var (apiBase, apiKey) = GetManagementApiConfig();
        if (apiBase != null && string.IsNullOrEmpty(sensor.HealthCheckUuid))
        {
            var instanceName = _settingsService.GetSettings().InstanceName;
            var checkName = BuildCheckName(instanceName, sensor.SensorID, sensor.Name);
            var (timeout, grace) = GetCheckSchedule(sensor.Purpose);
            var uuid = await _healthChecksClient.CreateCheckAsync(apiBase, apiKey, checkName, timeout, grace);
            if (uuid != null)
            {
                sensor.HealthCheckUuid = uuid;
                _db.SaveChanges();
                SensorOperations.SyncToJsonFile(_config, _db);

                if (!sensor.Enabled)
                {
                    _ = Task.Run(() => _healthChecksClient.PauseCheckAsync(apiBase, apiKey, uuid));
                }
            }
            else
            {
                _logger.LogWarning("Failed to create healthchecks.io check for new sensor {SensorID} ({Name})", sensor.SensorID, sensor.Name);
            }
        }

        return Ok(new MessageResponse { Message = "Successful!" });
    }

    [HttpDelete]
    [Route("/api/sensors/{id}")]
    public ActionResult DeleteSensor(int id)
    {
        if (!_db.Sensors.Any(a => a.SensorID == id))
        {
            return StatusCode(404, new MessageResponse { Message = "Sensor not found." });
        }

        var sensor = _db.Sensors.Include(a => a.Headers).Single(a => a.SensorID == id);
        var uuid = sensor.HealthCheckUuid;

        _jobManager.RemoveRecurringJob(sensor.HangfireJobName);
        _db.Sensors.Remove(sensor);
        _db.SaveChanges();

        SensorOperations.SyncToJsonFile(_config, _db);

        // Delete the healthchecks.io check
        var (apiBase, apiKey) = GetManagementApiConfig();
        if (apiBase != null && !string.IsNullOrEmpty(uuid))
        {
            _ = Task.Run(() => _healthChecksClient.DeleteCheckAsync(apiBase, apiKey, uuid));
        }

        return Ok(new MessageResponse { Message = "Successful!" });
    }

    [HttpGet]
    [Route("/api/settings")]
    public ActionResult GetSettings()
    {
        const string defaultName = "Venstar Sensor Emulator";

        var settings = _settingsService.GetSettings();

        if (string.IsNullOrWhiteSpace(settings.InstanceName))
        {
            settings.InstanceName = defaultName;
        }

        return new JsonResult(settings);
    }

    [HttpPut]
    [Route("/api/settings")]
    public async Task<ActionResult> UpdateSettings(SettingsDTO settings)
    {
        var oldSettings = _settingsService.GetSettings();
        var oldInstanceName = oldSettings.InstanceName;

        var toSave = new SettingsDTO
        {
            InstanceName = string.IsNullOrWhiteSpace(settings.InstanceName) ? null : settings.InstanceName.Trim(),
            HealthChecksBaseUrl = string.IsNullOrWhiteSpace(settings.HealthChecksBaseUrl) ? null : settings.HealthChecksBaseUrl.Trim(),
            HealthChecksApiKey = string.IsNullOrWhiteSpace(settings.HealthChecksApiKey) ? null : settings.HealthChecksApiKey.Trim()
        };

        _settingsService.SaveSettings(toSave);

        // Backfill checks for sensors without UUIDs when API key is configured
        if (!string.IsNullOrWhiteSpace(toSave.HealthChecksApiKey) && !string.IsNullOrWhiteSpace(toSave.HealthChecksBaseUrl))
        {
            var apiBase = HealthChecksClient.GetManagementApiBaseUrl(toSave.HealthChecksBaseUrl);
            await BackfillHealthChecksAsync(apiBase, toSave.HealthChecksApiKey, toSave.InstanceName);

            // Rename existing checks if instance name changed
            if (oldInstanceName != toSave.InstanceName)
            {
                await RenameChecksForInstanceNameChangeAsync(apiBase, toSave.HealthChecksApiKey, oldInstanceName, toSave.InstanceName);
            }
        }

        return Ok(new MessageResponse { Message = "Settings saved." });
    }

    [HttpPost]
    [Route("/api/testjsonpath")]
    public ActionResult TestJsonPath(JSONPathTest test)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(test?.JSONDocument))
            {
                return StatusCode(400, new MessageResponse { Message = "JSON Document is required." });
            }
            if (string.IsNullOrWhiteSpace(test?.Query))
            {
                return StatusCode(400, new MessageResponse { Message = "JSON Path Query is required." });
            }
            var doc = JObject.Parse(test.JSONDocument);
            var result = doc.SelectTokens(test.Query);
            if (!result.Any())
            {
                return Ok(new MessageResponse { Message = "No results found." });
            }
            if (result.Count() > 1)
            {
                return Ok(new MessageResponse { Message = $"Multiple results found. Only the first one would be used.\n====\n{JsonConvert.SerializeObject(result, Formatting.Indented)}" });
            }
            return Ok(new MessageResponse { Message = $"This is what would be transmitted as the temperature:\n====\n{result.First()}" });
        }
        catch (JsonReaderException e)
        {
            return StatusCode(400, new MessageResponse { Message = $"JSON Document Error:\n====\n{e.Message}" });
        }
        catch (JsonException e)
        {
            return StatusCode(400, new MessageResponse { Message = $"JSON Path Error:\n====\n{e.Message}" });
        }
        catch (Exception e)
        {
            return StatusCode(400, new MessageResponse { Message = $"System Error:\n====\n{e.Message}" });
        }
    }

    [HttpPost]
    [Route("/api/fetchurl")]
    public ActionResult FetchUrl(FetchUrlRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Url))
            {
                return StatusCode(400, new MessageResponse { Message = "URL is required." });
            }

            // Find a sensor with this URL to get the headers and SSL settings
            var sensor = _db.Sensors.Include(a => a.Headers).FirstOrDefault(a => a.URL == request.Url);

            if (sensor == null)
            {
                return StatusCode(400, new MessageResponse { Message = "URL not configured" });
            }

            var responseBody = _sensorOperations.GetDocument(sensor);

            return Content(responseBody, "application/json");
        }
        catch (VenstarTranslatorException e)
        {
            return StatusCode(400, new MessageResponse { Message = e.Message });
        }
        catch (Exception e)
        {
            return StatusCode(500, new MessageResponse { Message = $"Unexpected error: {e.Message}" });
        }
    }

    private (string apiBaseUrl, string apiKey) GetManagementApiConfig()
    {
        var settings = _settingsService.GetSettings();
        if (string.IsNullOrWhiteSpace(settings.HealthChecksApiKey))
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(settings.HealthChecksBaseUrl))
        {
            return (null, null);
        }

        var apiBase = HealthChecksClient.GetManagementApiBaseUrl(settings.HealthChecksBaseUrl);
        return (apiBase, settings.HealthChecksApiKey);
    }

    private async Task RenameChecksForInstanceNameChangeAsync(string apiBaseUrl, string apiKey, string oldInstanceName, string newInstanceName)
    {
        var sensorsWithUuid = _db.Sensors.Where(s => s.HealthCheckUuid != null && s.HealthCheckUuid != "").ToList();
        foreach (var sensor in sensorsWithUuid)
        {
            var expectedOldName = BuildCheckName(oldInstanceName, sensor.SensorID, sensor.Name);
            var actualName = await _healthChecksClient.GetCheckNameAsync(apiBaseUrl, apiKey, sensor.HealthCheckUuid);
            if (actualName == expectedOldName)
            {
                var newConventionName = BuildCheckName(newInstanceName, sensor.SensorID, sensor.Name);
                var renamed = await _healthChecksClient.RenameCheckAsync(apiBaseUrl, apiKey, sensor.HealthCheckUuid, newConventionName);
                if (renamed)
                {
                    _logger.LogInformation(
                        "Renamed healthchecks.io check for sensor {SensorID} ({Name}): '{OldName}' -> '{NewName}'",
                        sensor.SensorID, sensor.Name, expectedOldName, newConventionName);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Skipping instance name rename for sensor {SensorID}: check name '{ActualName}' doesn't match convention '{ExpectedName}'",
                    sensor.SensorID, actualName, expectedOldName);
            }
        }
    }

    private async Task BackfillHealthChecksAsync(string apiBaseUrl, string apiKey, string instanceName)
    {
        var sensorsWithoutUuid = _db.Sensors.Where(s => s.HealthCheckUuid == null || s.HealthCheckUuid == "").ToList();
        foreach (var sensor in sensorsWithoutUuid)
        {
            var name = BuildCheckName(instanceName, sensor.SensorID, sensor.Name);
            var (timeout, grace) = GetCheckSchedule(sensor.Purpose);
            var uuid = await _healthChecksClient.CreateCheckAsync(apiBaseUrl, apiKey, name, timeout, grace);
            if (uuid != null)
            {
                sensor.HealthCheckUuid = uuid;
                _db.SaveChanges();
                SensorOperations.SyncToJsonFile(_config, _db);
                _logger.LogInformation(
                    "Created healthchecks.io check for sensor {SensorID} ({Name}): {Uuid}",
                    sensor.SensorID, sensor.Name, uuid);

                if (!sensor.Enabled)
                {
                    _ = Task.Run(() => _healthChecksClient.PauseCheckAsync(apiBaseUrl, apiKey, uuid));
                }
            }
            else
            {
                _logger.LogWarning(
                    "Failed to create healthchecks.io check for sensor {SensorID} ({Name}). Will retry on next startup or settings save.",
                    sensor.SensorID, sensor.Name);
            }
        }
    }

    private static string BuildCheckName(string instanceName, byte sensorID, string sensorName)
    {
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            return $"Venstar Translator - {instanceName} - [{sensorID}] {sensorName}";
        }

        return $"Venstar Translator - [{sensorID}] {sensorName}";
    }

    private static (int timeout, int grace) GetCheckSchedule(SensorPurpose purpose)
    {
        return purpose == SensorPurpose.Outdoor ? (300, 1200) : (60, 300);
    }
}
