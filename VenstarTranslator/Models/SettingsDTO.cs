using System.Diagnostics.CodeAnalysis;

namespace VenstarTranslator.Models;

[ExcludeFromCodeCoverage]
public class SettingsDTO
{
    public string InstanceName { get; set; }
    public string HealthChecksMode { get; set; }           // "none", "saas", or "selfhosted"
    public string HealthChecksSelfHostedUrl { get; set; }  // only used when mode = "selfhosted"
    public string HealthChecksApiKey { get; set; }
}
