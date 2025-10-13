using System;
using System.ComponentModel.DataAnnotations;

namespace VenstarTranslator.Models.Validation;

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
