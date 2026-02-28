using System.Diagnostics.CodeAnalysis;

namespace VenstarTranslator.Models;

[ExcludeFromCodeCoverage]
public class SettingsDTO
{
    public string InstanceName { get; set; }
    public string HealthChecksBaseUrl { get; set; }
    public string HealthChecksApiKey { get; set; }
}
