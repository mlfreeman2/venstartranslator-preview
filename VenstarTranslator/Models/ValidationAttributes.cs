using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;

using Newtonsoft.Json.Linq;

namespace VenstarTranslator.Models;

public class ValidJsonPathAttribute : ValidationAttribute
{
    public override bool IsValid(object value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return true; // Let Required handle null/empty
        }

        try
        {
            JObject obj = [];
            obj.SelectToken(value.ToString());
            return true;
        }
        catch (JsonException e)
        {
            switch (e.Message)
            {
                case "Unexpected character while parsing path query: \"":
                    ErrorMessage = "JSONPath syntax error: Replace double quotes \" with single quotes '.";
                    break;
                default:
                    ErrorMessage = $"JSONPath syntax error: '{e.Message}'";
                    break;
            }
            return false;
        }
    }
}

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

public class ValidAbsoluteUrlAttribute : ValidationAttribute
{
    public override bool IsValid(object value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return true; // Let Required handle null/empty
        }

        if (!Uri.IsWellFormedUriString(value.ToString(), UriKind.Absolute))
        {
            ErrorMessage = "The URL must be a properly formed absolute URL.";
            return false;
        }

        return true;
    }
}

