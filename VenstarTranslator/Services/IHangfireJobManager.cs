namespace VenstarTranslator.Services;

public interface IHangfireJobManager
{
    void AddOrUpdateRecurringJob(string jobId, string cronExpression, uint sensorID);
    void RemoveRecurringJob(string jobId);
}
