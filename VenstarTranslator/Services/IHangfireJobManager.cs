using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

public interface IHangfireJobManager
{
    void AddOrUpdateRecurringJob(string jobId, SensorPurpose purpose, uint sensorID);
    void RemoveRecurringJob(string jobId);
}
