using System.Diagnostics.CodeAnalysis;

using Hangfire;

namespace VenstarTranslator.Services;

[ExcludeFromCodeCoverage]
public class HangfireJobManager : IHangfireJobManager
{
    private readonly IRecurringJobManager _recurringJobManager;

    public HangfireJobManager(IRecurringJobManager recurringJobManager)
    {
        _recurringJobManager = recurringJobManager;
    }

    public void AddOrUpdateRecurringJob(string jobId, string cronExpression, uint sensorID)
    {
        _recurringJobManager.AddOrUpdate<Tasks>(jobId, tasks => tasks.SendDataPacket(sensorID), cronExpression);
    }

    public void RemoveRecurringJob(string jobId)
    {
        _recurringJobManager.RemoveIfExists(jobId);
    }
}
