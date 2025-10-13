using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

namespace VenstarTranslator.Models;

[Owned]
public class DataSourceHttpHeader
{
    [JsonIgnore]
    [Key]
    public int ID { get; set; }

    [JsonProperty(Order = 1)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Header name is required.")]
    public string Name { get; set; }

    [JsonProperty(Order = 2)]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Header value is required.")]
    public string Value { get; set; }
}
