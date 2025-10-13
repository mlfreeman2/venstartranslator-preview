using System.Diagnostics.CodeAnalysis;

namespace VenstarTranslator.Models;

[ExcludeFromCodeCoverage]
public class TemperatureResponse
{
    public double Temperature { get; set; }
    public TemperatureScale Scale { get; set; }
}
