using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Models;

/// <summary>
/// Data Transfer Object for HTTP headers used in JSON serialization
/// </summary>
public class DataSourceHttpHeaderDTO
{
    [JsonProperty(Order = 1)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Header name is required.")]
    public string Name { get; set; }

    [JsonProperty(Order = 2)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Header value is required.")]
    public string Value { get; set; }

    /// <summary>
    /// Converts a DataSourceHttpHeader entity to a DTO
    /// </summary>
    public static DataSourceHttpHeaderDTO FromHeader(DataSourceHttpHeader header)
    {
        return new DataSourceHttpHeaderDTO
        {
            Name = header.Name,
            Value = header.Value
        };
    }

    /// <summary>
    /// Converts this DTO to a DataSourceHttpHeader entity
    /// </summary>
    public DataSourceHttpHeader ToHeader()
    {
        return new DataSourceHttpHeader
        {
            Name = Name,
            Value = Value
        };
    }
}
