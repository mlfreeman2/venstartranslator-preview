using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace VenstarTranslator.Models.Validation;

public class ValidHttpHeadersAttribute : ValidationAttribute
{
    public override bool IsValid(object value)
    {
        if (value == null)
        {
            return true;
        }

        var headers = value as List<DataSourceHttpHeader>;
        if (headers == null || headers.Count == 0)
        {
            return true;
        }

        // Check for null/empty names or values
        if (headers.Any(h => string.IsNullOrWhiteSpace(h.Name) || string.IsNullOrWhiteSpace(h.Value)))
        {
            ErrorMessage = "HTTP headers cannot contain entries with null, blank, or white space names or values.";
            return false;
        }

        // Check for duplicate header names
        if (headers.Select(h => h.Name).Distinct().Count() < headers.Count)
        {
            ErrorMessage = "HTTP headers cannot contain duplicate header names.";
            return false;
        }

        return true;
    }
}
