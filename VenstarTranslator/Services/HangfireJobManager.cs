using Hangfire;
using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

public class HangfireJobManager : IHangfireJobManager
{
    public void AddOrUpdateRecurringJob(string jobId, SensorPurpose purpose, uint sensorID)
    {
        string cronExpression = purpose == SensorPurpose.Outdoor ? "*/5 * * * *" : "* * * * *";
        RecurringJob.AddOrUpdate<VenstarTranslator.Tasks>(jobId, tasks => tasks.SendDataPacket(sensorID), cronExpression);
    }

    public void RemoveRecurringJob(string jobId)
    {
        RecurringJob.RemoveIfExists(jobId);
    }
}
