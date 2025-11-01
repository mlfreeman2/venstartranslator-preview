using System;
using System.Linq;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VenstarTranslator.Models;

namespace VenstarTranslator.Filters;

/// <summary>
/// Hangfire filter attribute that tracks broadcast success/failure for sensors
/// Apply this attribute to broadcast methods to automatically track their execution status
/// </summary>
public class BroadcastTrackingFilterAttribute : JobFilterAttribute, IServerFilter
{
    // Static service provider to be set during application startup
    internal static IServiceProvider ServiceProvider { get; set; }

    public void OnPerforming(PerformingContext context)
    {
        // Nothing to do before job execution
    }

    public void OnPerformed(PerformedContext context)
    {
        // Get the sensor ID from the job arguments
        if (context.BackgroundJob.Job.Args.Count == 0 || context.BackgroundJob.Job.Args[0] is not uint sensorID)
        {
            return;
        }

        if (ServiceProvider == null)
        {
            return;
        }

        using var scope = ServiceProvider.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<VenstarTranslatorDataCache>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BroadcastTrackingFilterAttribute>>();

        var sensor = dbContext.Sensors.FirstOrDefault(s => s.SensorID == (byte)sensorID);
        if (sensor == null)
        {
            return;
        }

        if (context.Exception == null)
        {
            // Success - update last successful broadcast time
            sensor.LastSuccessfulBroadcast = DateTime.UtcNow;
            dbContext.SaveChanges();

            logger.LogDebug("Broadcast succeeded for sensor {SensorID} ({SensorName})", sensor.SensorID, sensor.Name);
        }
        else
        {
            // Failure - log the exception
            logger.LogError(
                context.Exception,
                "Broadcast failed for sensor {SensorID} ({SensorName}). Last successful broadcast: {LastSuccessfulBroadcast}",
                sensor.SensorID,
                sensor.Name,
                sensor.LastSuccessfulBroadcast?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never"
            );

            // Check if broadcasts have become stale (thermostat would show error at this point)
            if (sensor.HasProblem)
            {
                logger.LogWarning(
                    "Sensor {SensorID} ({SensorName}) broadcasts are stale (no successful broadcast in {StaleThresholdMinutes} minutes). Last successful broadcast: {LastSuccessfulBroadcast}",
                    sensor.SensorID,
                    sensor.Name,
                    sensor.StaleThresholdMinutes,
                    sensor.LastSuccessfulBroadcast?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never"
                );
            }
        }
    }
}
