using System;
using System.Linq;
using System.Net.Sockets;

using Hangfire;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using VenstarTranslator.DB;

namespace VenstarTranslator
{
    // TODO: Coravel instead of Hangfire?
    public class Tasks
    {
        private IServiceProvider _serviceProvider;

        public Tasks(IServiceProvider sp)
        {
            _serviceProvider = sp;
        }

        [JobDisplayName("Send a Venstar data packet for sensor #{0}")]
        [AutomaticRetry(Attempts = 0)]
        public void SendDataPacket(uint sensorID)
        {
            using (IServiceScope scope = _serviceProvider.CreateScope())
            using (var dbContext = scope.ServiceProvider.GetRequiredService<VenstarTranslatorDataCache>())
            {
                var sensor = dbContext.Sensors.Include(a => a.Headers).Single(a => a.SensorID == sensorID);
                sensor.SendDataPacket();
                dbContext.SaveChanges();
            }
        }
    }
}