using Hangfire;

namespace VenstarTranslator.Services;

public class HangfireJobManager : IHangfireJobManager
{
    public void AddOrUpdateRecurringJob(string jobId, string cronExpression, uint sensorID)
    {
        RecurringJob.AddOrUpdate<VenstarTranslator.Tasks>(jobId, tasks => tasks.SendDataPacket(sensorID), cronExpression);
    }

    public void RemoveRecurringJob(string jobId)
    {
        RecurringJob.RemoveIfExists(jobId);
    }
}
