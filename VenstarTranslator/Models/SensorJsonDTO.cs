using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Models.Validation;

namespace VenstarTranslator.Models;

/// <summary>
/// Data Transfer Object for sensors.json file persistence
/// Contains only the fields that should be persisted to the configuration file
/// </summary>
public class SensorJsonDTO
{
    [JsonProperty(Order = 1)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Sensor ID is required.")]
    [Range(0, 19, ErrorMessage = "Sensor ID must be between 0 and 19.")]
    public byte SensorID { get; set; }

    [JsonProperty(Order = 2)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Sensor name is required and cannot be empty.")]
    [MaxLength(14, ErrorMessage = "Sensor name cannot exceed 14 characters.")]
    public string Name { get; set; }

    [JsonProperty(Order = 3)]
    public bool Enabled { get; set; }

    [JsonProperty(Order = 4)]
    [JsonConverter(typeof(StringEnumConverter))]
    [Required(ErrorMessage = "Sensor purpose is required.")]
    public SensorPurpose Purpose { get; set; }

    [JsonProperty(Order = 5)]
    [JsonConverter(typeof(StringEnumConverter))]
    [Required(ErrorMessage = "Temperature scale is required.")]
    public TemperatureScale Scale { get; set; }

    [JsonProperty(Order = 6)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "URL is required and cannot be empty.")]
    [ValidAbsoluteUrl]
    public string URL { get; set; }

    [JsonProperty(Order = 7)]
    public bool IgnoreSSLErrors { get; set; }

    [JsonProperty(Order = 8)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "JSONPath is required.")]
    [ValidJsonPath]
    public string JSONPath { get; set; }

    [JsonProperty(Order = 9)]
    [ValidHttpHeaders]
    public List<DataSourceHttpHeaderDTO> Headers { get; set; }

    /// <summary>
    /// Converts a TranslatedVenstarSensor entity to a SensorJsonDTO
    /// </summary>
    public static SensorJsonDTO FromSensor(TranslatedVenstarSensor sensor)
    {
        return new SensorJsonDTO
        {
            SensorID = sensor.SensorID,
            Name = sensor.Name,
            Enabled = sensor.Enabled,
            Purpose = sensor.Purpose,
            Scale = sensor.Scale,
            URL = sensor.URL,
            IgnoreSSLErrors = sensor.IgnoreSSLErrors,
            JSONPath = sensor.JSONPath,
            Headers = sensor.Headers?.Select(h => DataSourceHttpHeaderDTO.FromHeader(h)).ToList()
        };
    }

    /// <summary>
    /// Converts this DTO to a TranslatedVenstarSensor entity
    /// </summary>
    public TranslatedVenstarSensor ToSensor()
    {
        return new TranslatedVenstarSensor
        {
            SensorID = SensorID,
            Name = Name,
            Enabled = Enabled,
            Purpose = Purpose,
            Scale = Scale,
            URL = URL,
            IgnoreSSLErrors = IgnoreSSLErrors,
            JSONPath = JSONPath,
            Headers = Headers?.Select(h => h.ToHeader()).ToList()
        };
    }
}
