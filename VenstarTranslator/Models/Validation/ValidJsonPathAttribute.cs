using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Newtonsoft.Json.Linq;

namespace VenstarTranslator.Models.Validation;

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
