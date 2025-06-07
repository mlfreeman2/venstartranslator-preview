using System;
using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using VenstarTranslator.DB;
using VenstarTranslator.Models.Protobuf;

namespace VenstarTranslator.Controllers
{
    [ApiController]
    public class API : ControllerBase
    {
        private readonly ILogger<API> _logger;

        private readonly VenstarTranslatorDataCache _db;

        public API(ILogger<API> logger, VenstarTranslatorDataCache db)
        {
            _logger = logger;
            _db = db;
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
            
            var tempIndex = Tasks.ConvertTemperatureToIndex(Tasks.GetLatestReading(sensor), sensor.Scale);

            var dataPacket = new SensorMessage {
                Command = SensorMessage.Commands.SENSORPAIR,
                SensorData = new SENSORDATA {
                    Info = new INFO {
                        Sequence = 0,
                        SensorId = Convert.ToByte(sensor.SensorID),
                        Mac = sensor.MacAddress,
                        Type = Tasks.TranslateType(sensor),
                        Name = sensor.Name,
                        Temperature = Convert.ToByte(tempIndex)
                    },
                    Signature = sensor.Signature_Key
                }
            };

            sensor.Sequence = 1;
            _db.SaveChanges();

            Tasks.UdpBroadcast(SensorMessageSerializer.Serialize(dataPacket));

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
           return new JsonResult(new { Temperature = Tasks.GetLatestReading(sensor) , sensor.Scale});
        } 

        [HttpGet]
        [Route("/api/sensors")]
        public ActionResult ListSensors()
        {
            return new JsonResult(_db.Sensors.ToList());
        } 
    }
}
