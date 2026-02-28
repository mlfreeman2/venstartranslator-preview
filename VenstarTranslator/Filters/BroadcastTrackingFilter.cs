using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VenstarTranslator.Exceptions;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Services;

namespace VenstarTranslator.Filters;

/// <summary>
/// Hangfire filter attribute that tracks broadcast success/failure for sensors
/// Apply this attribute to broadcast methods to automatically track their execution status
/// </summary>
[ExcludeFromCodeCoverage]
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
        var healthChecks = scope.ServiceProvider.GetRequiredService<IHealthChecksClient>();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        var sensor = dbContext.Sensors.FirstOrDefault(s => s.SensorID == (byte)sensorID);
        if (sensor == null)
        {
            return;
        }

        // Build the healthchecks.io ping URL from global base URL + per-sensor UUID
        string healthCheckPingUrl = null;
        if (!string.IsNullOrWhiteSpace(sensor.HealthCheckUuid))
        {
            var baseUrl = settingsService.GetSettings().HealthChecksBaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                healthCheckPingUrl = $"{baseUrl.TrimEnd('/')}/{sensor.HealthCheckUuid}";
            }
        }

        if (context.Exception == null)
        {
            // Success - update last successful broadcast time and reset failure counter
            sensor.LastSuccessfulBroadcast = DateTime.UtcNow;
            sensor.LastErrorMessage = null;
            sensor.ConsecutiveFailures = 0;
            dbContext.SaveChanges();

            logger.LogDebug("Broadcast succeeded for sensor {SensorID} ({SensorName})", sensor.SensorID, sensor.Name);

            // Fire-and-forget healthchecks.io success ping
            if (healthCheckPingUrl != null)
            {
                var url = healthCheckPingUrl;
                _ = Task.Run(() => healthChecks.PingSuccessAsync(url));
            }
        }
        else
        {
            // Hangfire wraps exceptions in JobPerformanceException, so check the inner exception
            var actualException = context.Exception.InnerException ?? context.Exception;

            // Increment consecutive failure counter
            sensor.ConsecutiveFailures++;

            string failureBody;

            // Check if this is a VenstarTranslatorException (user-friendly message)
            if (actualException is VenstarTranslatorException vtEx)
            {
                // Store the user-friendly error message
                sensor.LastErrorMessage = vtEx.Message;
                dbContext.SaveChanges();

                // Log without exception stack trace since this is a user-friendly message
                logger.LogError(
                    "Broadcast failed for sensor {SensorID} ({SensorName}): {ErrorMessage}. Consecutive failures: {ConsecutiveFailures}. Last successful broadcast: {LastSuccessfulBroadcast}",
                    sensor.SensorID,
                    sensor.Name,
                    vtEx.Message,
                    sensor.ConsecutiveFailures,
                    sensor.LastSuccessfulBroadcast?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never"
                );

                failureBody = vtEx.Message;
            }
            else
            {
                // System exception - don't store the message, just log it
                dbContext.SaveChanges();

                logger.LogError(
                    context.Exception,
                    "Broadcast failed for sensor {SensorID} ({SensorName}) with unexpected error. Consecutive failures: {ConsecutiveFailures}. Last successful broadcast: {LastSuccessfulBroadcast}",
                    sensor.SensorID,
                    sensor.Name,
                    sensor.ConsecutiveFailures,
                    sensor.LastSuccessfulBroadcast?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never"
                );

                failureBody = HealthChecksClient.BuildFailureBody(actualException);
            }

            // Fire-and-forget healthchecks.io failure ping
            if (healthCheckPingUrl != null)
            {
                var url = healthCheckPingUrl;
                var body = failureBody;
                _ = Task.Run(() => healthChecks.PingFailureAsync(url, body));
            }

            // Check if broadcasts have become stale (thermostat would show error at this point)
            if (sensor.HasProblem)
            {
                logger.LogWarning(
                    "Sensor {SensorID} ({SensorName}) has reached failure threshold ({ConsecutiveFailures} consecutive failures). Last successful broadcast: {LastSuccessfulBroadcast}",
                    sensor.SensorID,
                    sensor.Name,
                    sensor.ConsecutiveFailures,
                    sensor.LastSuccessfulBroadcast?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never"
                );
            }
        }
    }
}
