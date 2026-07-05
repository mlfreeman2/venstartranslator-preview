using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Hangfire;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VenstarTranslator.Filters;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Services;

namespace VenstarTranslator;

[ExcludeFromCodeCoverage]
public class Tasks
{
    private IServiceProvider _serviceProvider;

    public Tasks(IServiceProvider sp)
    {
        _serviceProvider = sp;
    }

    [JobDisplayName("Send a Venstar data packet for sensor #{0}")]
    [BroadcastTrackingFilter]
    public void SendDataPacket(uint sensorID)
    {
        using (IServiceScope scope = _serviceProvider.CreateScope())
        using (var dbContext = scope.ServiceProvider.GetRequiredService<VenstarTranslatorDataCache>())
        {
            var sensor = dbContext.Sensors.Include(a => a.Headers).Single(a => a.SensorID == sensorID);
            var sensorOperations = scope.ServiceProvider.GetRequiredService<ISensorOperations>();
            sensorOperations.SendDataPacket(sensor);
            dbContext.SaveChanges();
        }
    }
}
