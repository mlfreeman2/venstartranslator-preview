using System;
using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using VenstarTranslator.Models;

namespace VenstarTranslator.Controllers;

[ApiController]
public class API : ControllerBase
{
    private readonly ILogger<API> _logger;

    private readonly VenstarTranslatorDataCache _db;

    private readonly IConfiguration _config;

    public API(ILogger<API> logger, VenstarTranslatorDataCache db, IConfiguration config)
    {
        _logger = logger;
        _db = db;
        _config = config;
    }

    [HttpGet]
    [Route("/api/sensors/{id}/pair")]
    public ActionResult SendPairingPacket(uint id)
    {
        var sensor = _db.Sensors.Include(a => a.Headers).FirstOrDefault(a => a.SensorID == id);
        if (sensor == null)
        {
            return StatusCode(404, new { message = "Sensor not found." });
        }

        sensor.SendPairingPacket();
        _db.SaveChanges();

        return new JsonResult(new { Message = "Pairing packet sent." });
    }



    [HttpGet]
    [Route("/api/sensors/{id}/latest")]
    public ActionResult GetReading(uint id)
    {
        var sensor = _db.Sensors.Include(a => a.Headers).FirstOrDefault(a => a.SensorID == id);
        if (sensor == null)
        {
            return StatusCode(404, new { message = "Sensor not found." });
        }
        try
        {
            var reading = sensor.GetLatestReading();
            return new JsonResult(new { Temperature = reading, sensor.Scale });
        }
        catch (InvalidOperationException e)
        {
            return StatusCode(400, new { e.Message });
        }

    }

    [HttpGet]
    [Route("/api/sensors")]
    public ActionResult ListSensors()
    {
        return new JsonResult(_db.Sensors.Include(a => a.Headers).ToList());
    }

    [HttpPut]
    [Route("/api/sensors")]
    public ActionResult UpdateSensor(TranslatedVenstarSensor updated)
    {
        if (!_db.Sensors.Any(a => a.SensorID == updated.SensorID))
        {
            return StatusCode(404, new { message = "Sensor not found." });
        }

        // update working db
        var current = _db.Sensors.Include(a => a.Headers).Single(a => a.SensorID == updated.SensorID);
        current.Name = updated.Name;
        current.Enabled = updated.Enabled;
        current.URL = updated.URL;
        current.Purpose = updated.Purpose;
        current.JSONPath = updated.JSONPath;
        current.Scale = updated.Scale;
        current.IgnoreSSLErrors = updated.IgnoreSSLErrors;
        current.Headers.Clear();
        _db.SaveChanges();

        if (updated.Headers != null && updated.Headers.Any())
        {
            current.Headers.AddRange(updated.Headers);
        }
        _db.SaveChanges();

        SyncToSensorsJson(_config, _db);
        current.SyncHangfire();

        return Ok(new { message = "Successful!" });
    }

    [HttpPost]
    [Route("/api/sensors")]
    public ActionResult AddSensor(TranslatedVenstarSensor sensor)
    {
        sensor.SensorID = 20;
        for (byte i = 0; i <= 19; i++)
        {
            if (!_db.Sensors.Any(a => a.SensorID == i))
            {
                sensor.SensorID = i;
                break;
            }
        }

        if (sensor.SensorID > 19)
        {
            return StatusCode(400, new { message = "No sensor IDs available. Delete some sensors first." });
        }

        _db.Sensors.Add(sensor);
        _db.SaveChanges();
        SyncToSensorsJson(_config, _db);
        sensor.SyncHangfire();

        return Ok(new { message = "Successful!" });
    }

    [HttpDelete]
    [Route("/api/sensors/{id}")]
    public ActionResult DeleteSensor(int id)
    {
        if (!_db.Sensors.Any(a => a.SensorID == id))
        {
            return StatusCode(404, new { message = "Sensor not found." });
        }

        var sensor = _db.Sensors.Include(a => a.Headers).Single(a => a.SensorID == id);
        sensor.Enabled = false;
        sensor.SyncHangfire();
        _db.Sensors.Remove(sensor);
        _db.SaveChanges();

        SyncToSensorsJson(_config, _db);

        return Ok(new { message = "Successful!" });
    }

    private static void SyncToSensorsJson(IConfiguration _config, VenstarTranslatorDataCache _db)
    {
        // update sensors.json
        var sensorFilePath = _config.GetValue<string>("SensorFilePath");
        var dbDump = _db.Sensors.Include(a => a.Headers).AsNoTracking().ToList();
        System.IO.File.WriteAllText(sensorFilePath, JsonConvert.SerializeObject(dbDump, Formatting.Indented, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));
    }

    [HttpPost]
    [Route("/api/testjsonpath")]
    public ActionResult TestJsonPath(JSONPathTest test)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(test?.JSONDocument))
            {
                return StatusCode(400, "Error: JSON Document is required.");
            }
            if (string.IsNullOrWhiteSpace(test?.Query))
            {
                return StatusCode(400, "Error: JSON Path Query is required.");
            }
            var doc = JObject.Parse(test.JSONDocument);
            var result = doc.SelectTokens(test.Query);
            if (!result.Any())
            {
                return Ok("No results found.");
            }
            if (result.Count() > 1)
            {
                return Ok($"Multiple results found. Only the first one would be used.\n====\n{JsonConvert.SerializeObject(result, Formatting.Indented)}");
            }
            return Ok($"This is what would be transmitted as the temperature:\n====\n{result.First()}");
        }
        catch (JsonReaderException e)
        {
            return StatusCode(400, $"JSON Document Error:\n====\n({e.Message})");
        }
        catch (JsonException e)
        {
            return StatusCode(400, $"JSON Path Error:\n====\n{e.Message}");
        }
        catch (Exception e)
        {
            return StatusCode(400, $"System Error:\n====\n{e.Message}");
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
                return StatusCode(400, new { message = "URL is required." });
            }

            // Find a sensor with this URL to get the headers and SSL settings
            var sensor = _db.Sensors.Include(a => a.Headers).FirstOrDefault(a => a.URL == request.Url);

            if (sensor == null)
            {
                return StatusCode(400, new { message = "URL not configured" });
            }

            var responseBody = sensor.GetDocument();

            return Content(responseBody, "application/json");
        }
        catch (System.Net.Http.HttpRequestException e)
        {
            return StatusCode(400, new { message = $"HTTP Error: {e.Message}" });
        }
        catch (Exception e)
        {
            return StatusCode(400, new { message = $"Error fetching URL: {e.Message}" });
        }
    }
}
