using System.Linq;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Models.Protobuf;
using VenstarTranslator.DB;

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

            var dataPacket = new SensorMessage {
                Command = SensorMessage.Types.Commands.Sensorpair,
                Sensordata = new SENSORDATA {
                    Info = new INFO {
                        Sequence = 0,
                        SensorId = sensor.SensorID,
                        Mac = sensor.MacAddress,
                        FwMajor = 4,
                        FwMinor = 2,
                        Model = INFO.Types.SensorModel.Tempsensor,
                        Battery = 100,
                        Power = INFO.Types.PowerSource.Battery,
                        Type = Tasks.TranslateType(sensor),
                        Name = sensor.Name,
                        Temperature = Tasks.ConvertTemperatureToIndex(Tasks.GetLatestReading(sensor), sensor.Scale)
                    },
                    Signature = sensor.Signature_Key
                }
            };

            sensor.Sequence = 1;
            _db.SaveChanges();

            Tasks.UdpBroadcast(dataPacket);

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
