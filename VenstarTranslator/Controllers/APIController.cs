using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VenstarTranslator.DB;

namespace VenstarTranslator.Controllers
{
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
        [Route("/api/pair/{id}")]
        public ActionResult SendPairingPacket(uint id)
        {
            var sensor = _db.Sensors.FirstOrDefault(a => a.SensorID == id);
            if (sensor == null)
            {
                return new JsonResult(new { Message = "Sensor not found." });
            }
            if (!sensor.Enabled)
            {
                return new JsonResult(new { Message = "Sensor not enabled." });
            }

            sensor.SendPairingPacket();
            _db.SaveChanges();

            return new JsonResult(new { Message = "Pairing packet sent." });
        }

        [HttpGet]
        [Route("/api/sensors/{id}/latest")]
        public ActionResult GetReading(uint id)
        {
            var sensor = _db.Sensors.FirstOrDefault(a => a.SensorID == id);
            if (sensor == null)
            {
                return new JsonResult(new { Message = "Sensor not found." });
            }
            if (!sensor.Enabled)
            {
                return new JsonResult(new { Message = "Sensor not enabled." });
            }
            return new JsonResult(new { Temperature = sensor.GetLatestReading(), sensor.Scale });
        }

        [HttpGet]
        [Route("/api/sensors")]
        public ActionResult ListSensors()
        {
            return new JsonResult(_db.Sensors.ToList());
        }

        [HttpPut]
        [Route("/api/sensors")]
        public ActionResult UpdateSensor(TranslatedVenstarSensor proposedSensor)
        {
            if (!_db.Sensors.Any(a => a.SensorID == proposedSensor.SensorID))
            {
                return StatusCode(404, new { message = "Sensor not found to update." });
            }

            // update working db
            var currentDbSensor = _db.Sensors.Include(a => a.Headers).Single(a => a.SensorID == proposedSensor.SensorID);
            currentDbSensor.Name = proposedSensor.Name;
            currentDbSensor.Enabled = proposedSensor.Enabled;
            currentDbSensor.URL = proposedSensor.URL;
            currentDbSensor.Purpose = proposedSensor.Purpose;
            currentDbSensor.JSONPath = proposedSensor.JSONPath;
            currentDbSensor.Scale = proposedSensor.Scale;
            currentDbSensor.IgnoreSSLErrors = proposedSensor.IgnoreSSLErrors;

            currentDbSensor.Headers.Clear();
            _db.SaveChanges();

            if (proposedSensor.Headers != null && proposedSensor.Headers.Any())
            {
                currentDbSensor.Headers.AddRange(proposedSensor.Headers);
            }
            _db.SaveChanges();

            // update sensors.json
            var sensorIdOffset = _config.GetValue<int>("SensorIdOffset");
            var sensorFilePath = _config.GetValue<string>("SensorFilePath");
            var fileSensors = JsonConvert.DeserializeObject<List<TranslatedVenstarSensor>>(System.IO.File.ReadAllText(sensorFilePath));
            var currentFileSensor = fileSensors[proposedSensor.SensorID - sensorIdOffset];
            currentFileSensor.Name = proposedSensor.Name;
            currentFileSensor.Enabled = proposedSensor.Enabled;
            currentFileSensor.URL = proposedSensor.URL;
            currentFileSensor.Purpose = proposedSensor.Purpose;
            currentFileSensor.JSONPath = proposedSensor.JSONPath;
            currentFileSensor.Scale = proposedSensor.Scale;
            currentFileSensor.IgnoreSSLErrors = proposedSensor.IgnoreSSLErrors;

            if (proposedSensor.Headers != null && proposedSensor.Headers.Any())
            {
                if (currentFileSensor.Headers != null)
                {
                    currentFileSensor.Headers.Clear();
                }
                else
                {
                    currentFileSensor.Headers = [];
                }
                currentFileSensor.Headers.AddRange(proposedSensor.Headers);
            }
            System.IO.File.WriteAllText(sensorFilePath, JsonConvert.SerializeObject(fileSensors, Formatting.Indented, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

            if (currentDbSensor.Enabled)
            {
                var rjo = new RecurringJobOptions() { TimeZone = TimeZoneInfo.Local };
                var cronString = currentDbSensor.Purpose != SensorPurpose.Outdoor ? "* * * * *" : "*/5 * * * *";
                RecurringJob.AddOrUpdate<Tasks>($"Sensor #{currentDbSensor.SensorID}: {currentDbSensor.Name}", a => a.SendDataPacket(currentDbSensor.SensorID), cronString, rjo);
            }
            else
            {
                RecurringJob.RemoveIfExists($"Sensor #{currentDbSensor.SensorID}: {currentDbSensor.Name}");
            }

            return Ok(new { message = "Successful!" });
        }

        [HttpPost]
        [Route("/api/sensors")]
        public ActionResult AddSensor(TranslatedVenstarSensor proposedSensor)
        {
            if (_db.Sensors.Max(a => a.SensorID) >= 19)
            {
                return StatusCode(400, new { message = "Too many sensors." });
            }

            proposedSensor.SensorID = Convert.ToByte(_db.Sensors.Max(a => a.SensorID) + 1);
            _db.Sensors.Add(proposedSensor);
            _db.SaveChanges();

            var sensorFilePath = _config.GetValue<string>("SensorFilePath");
            var fileSensors = JsonConvert.DeserializeObject<List<TranslatedVenstarSensor>>(System.IO.File.ReadAllText(sensorFilePath));
            fileSensors.Add(proposedSensor);

            System.IO.File.WriteAllText(sensorFilePath, JsonConvert.SerializeObject(fileSensors, Formatting.Indented, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

            if (proposedSensor.Enabled)
            {
                var rjo = new RecurringJobOptions() { TimeZone = TimeZoneInfo.Local };
                var cronString = proposedSensor.Purpose != SensorPurpose.Outdoor ? "* * * * *" : "*/5 * * * *";
                RecurringJob.AddOrUpdate<Tasks>($"Sensor #{proposedSensor.SensorID}: {proposedSensor.Name}", a => a.SendDataPacket(proposedSensor.SensorID), cronString, rjo);
            }
            else
            {
                RecurringJob.RemoveIfExists($"Sensor #{proposedSensor.SensorID}: {proposedSensor.Name}");
            }

            return Ok(new { message = "Successful!" });
        }
    }
}
