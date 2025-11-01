using System;
using System.Collections.Generic;

namespace VenstarTranslator.Models;

/// <summary>
/// Data Transfer Object for sensor API responses that includes runtime status
/// </summary>
public class SensorDTO
{
    public byte SensorID { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public SensorPurpose Purpose { get; set; }
    public TemperatureScale Scale { get; set; }
    public string URL { get; set; }
    public bool IgnoreSSLErrors { get; set; }
    public string JSONPath { get; set; }
    public List<DataSourceHttpHeader> Headers { get; set; }
    public DateTime? LastSuccessfulBroadcast { get; set; }
    public bool HasProblem { get; set; }

    public static SensorDTO FromSensor(TranslatedVenstarSensor sensor)
    {
        return new SensorDTO
        {
            SensorID = sensor.SensorID,
            Name = sensor.Name,
            Enabled = sensor.Enabled,
            Purpose = sensor.Purpose,
            Scale = sensor.Scale,
            URL = sensor.URL,
            IgnoreSSLErrors = sensor.IgnoreSSLErrors,
            JSONPath = sensor.JSONPath,
            Headers = sensor.Headers,
            LastSuccessfulBroadcast = sensor.LastSuccessfulBroadcast,
            HasProblem = sensor.HasProblem
        };
    }
}
