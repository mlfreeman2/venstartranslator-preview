using System.Threading.Tasks;

namespace VenstarTranslator.Services;

public interface IHealthChecksClient
{
    Task PingSuccessAsync(string pingUrl);
    Task PingFailureAsync(string pingUrl, string body);

    Task<string> CreateCheckAsync(string apiBaseUrl, string apiKey, string name, int timeout, int grace);
    Task<string> GetCheckNameAsync(string apiBaseUrl, string apiKey, string uuid);
    Task<bool> RenameCheckAsync(string apiBaseUrl, string apiKey, string uuid, string newName);
    Task PauseCheckAsync(string apiBaseUrl, string apiKey, string uuid);
    Task UnpauseCheckAsync(string apiBaseUrl, string apiKey, string uuid);
    Task UpdateCheckScheduleAsync(string apiBaseUrl, string apiKey, string uuid, int timeout, int grace);
    Task DeleteCheckAsync(string apiBaseUrl, string apiKey, string uuid);
}
