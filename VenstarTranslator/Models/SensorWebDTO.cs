using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Models;

/// <summary>
/// Data Transfer Object for web UI/API responses that includes runtime status
/// Extends SensorJsonDTO with additional runtime fields not persisted to sensors.json
/// </summary>
[ExcludeFromCodeCoverage]
public class SensorWebDTO : SensorJsonDTO
{
    public DateTime? LastSuccessfulBroadcast { get; set; }
    public bool HasProblem { get; set; }
    public string LastErrorMessage { get; set; }

    public new static SensorWebDTO FromSensor(TranslatedVenstarSensor sensor)
    {
        return new SensorWebDTO
        {
            SensorID = sensor.SensorID,
            Name = sensor.Name,
            Enabled = sensor.Enabled,
            Purpose = sensor.Purpose,
            Scale = sensor.Scale,
            URL = sensor.URL,
            IgnoreSSLErrors = sensor.IgnoreSSLErrors,
            JSONPath = sensor.JSONPath,
            Headers = sensor.Headers?.Select(h => DataSourceHttpHeaderDTO.FromHeader(h)).ToList(),
            LastSuccessfulBroadcast = sensor.LastSuccessfulBroadcast,
            HasProblem = sensor.HasProblem,
            LastErrorMessage = sensor.LastErrorMessage
        };
    }
}
