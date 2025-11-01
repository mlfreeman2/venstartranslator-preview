using System;
using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using VenstarTranslator.Exceptions;
using VenstarTranslator.Models;
using VenstarTranslator.Models.Db;
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

    public API(ILogger<API> logger, VenstarTranslatorDataCache db, IConfiguration config, ISensorOperations sensorOperations, IHangfireJobManager jobManager)
    {
        _logger = logger;
        _db = db;
        _config = config;
        _sensorOperations = sensorOperations;
        _jobManager = jobManager;
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

        // Track if enabled state is changing to reset problem tracking
        bool enabledStateChanged = current.Enabled != updatedDTO.Enabled;

        current.Name = updatedDTO.Name;
        current.Enabled = updatedDTO.Enabled;
        current.URL = updatedDTO.URL;
        current.Purpose = updatedDTO.Purpose;
        current.JSONPath = updatedDTO.JSONPath;
        current.Scale = updatedDTO.Scale;
        current.IgnoreSSLErrors = updatedDTO.IgnoreSSLErrors;

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

        return Ok(new MessageResponse { Message = "Successful!" });
    }

    [HttpPost]
    [Route("/api/sensors")]
    public ActionResult AddSensor(SensorJsonDTO sensorDTO)
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
        _jobManager.RemoveRecurringJob(sensor.HangfireJobName);
        _db.Sensors.Remove(sensor);
        _db.SaveChanges();

        SensorOperations.SyncToJsonFile(_config, _db);

        return Ok(new MessageResponse { Message = "Successful!" });
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
}
