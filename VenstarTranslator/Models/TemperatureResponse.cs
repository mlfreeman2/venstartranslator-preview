using System.Diagnostics.CodeAnalysis;

using VenstarTranslator.Models.Enums;

namespace VenstarTranslator.Models;

[ExcludeFromCodeCoverage]
public class TemperatureResponse
{
    public double Temperature { get; set; }
    public TemperatureScale Scale { get; set; }
}
