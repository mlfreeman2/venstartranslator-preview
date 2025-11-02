using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Models.Validation;

public class ValidHttpHeadersAttribute : ValidationAttribute
{
    public override bool IsValid(object value)
    {
        if (value == null)
        {
            return true;
        }

        // Support both entity and DTO types
        var entityHeaders = value as List<DataSourceHttpHeader>;
        var dtoHeaders = value as List<DataSourceHttpHeaderDTO>;

        if (entityHeaders != null)
        {
            if (entityHeaders.Count == 0)
            {
                return true;
            }

            // Check for null/empty names or values
            if (entityHeaders.Any(h => string.IsNullOrWhiteSpace(h.Name) || string.IsNullOrWhiteSpace(h.Value)))
            {
                ErrorMessage = "HTTP headers cannot contain entries with null, blank, or white space names or values.";
                return false;
            }

            // Check for duplicate header names
            if (entityHeaders.Select(h => h.Name).Distinct().Count() < entityHeaders.Count)
            {
                ErrorMessage = "HTTP headers cannot contain duplicate header names.";
                return false;
            }

            return true;
        }

        if (dtoHeaders != null)
        {
            if (dtoHeaders.Count == 0)
            {
                return true;
            }

            // Check for null/empty names or values
            if (dtoHeaders.Any(h => string.IsNullOrWhiteSpace(h.Name) || string.IsNullOrWhiteSpace(h.Value)))
            {
                ErrorMessage = "HTTP headers cannot contain entries with null, blank, or white space names or values.";
                return false;
            }

            // Check for duplicate header names
            if (dtoHeaders.Select(h => h.Name).Distinct().Count() < dtoHeaders.Count)
            {
                ErrorMessage = "HTTP headers cannot contain duplicate header names.";
                return false;
            }

            return true;
        }

        return true;
    }
}
