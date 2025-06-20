using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
    }
}
