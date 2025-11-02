using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace VenstarTranslator.Models.Db;

[Owned]
public class DataSourceHttpHeader
{
    [Key]
    public int ID { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Header name is required.")]
    public string Name { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Header value is required.")]
    public string Value { get; set; }
}
