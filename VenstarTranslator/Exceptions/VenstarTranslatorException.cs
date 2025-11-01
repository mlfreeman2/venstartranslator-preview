using System;

namespace VenstarTranslator.Exceptions;

/// <summary>
/// Base exception for all VenstarTranslator-specific errors.
/// These exceptions contain user-friendly messages that can be displayed in the UI.
/// </summary>
public class VenstarTranslatorException : Exception
{
    public VenstarTranslatorException(string message) : base(message) { }

    public VenstarTranslatorException(string message, Exception innerException)
        : base(message, innerException) { }
}
