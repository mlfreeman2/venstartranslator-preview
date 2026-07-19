namespace VenstarTranslator.Models.ProtobufCapture;

// Flat projection of a SENSORDATA/SENSORPAIR packet for the table columns (§4b step 4).
public class SensorSummary
{
    public byte? SensorId { get; set; }

    public string Mac { get; set; }

    public string Name { get; set; }

    // Purpose (Type) rendered by name, e.g. "OUTDOOR"; null when the field is absent.
    public string Purpose { get; set; }

    public ushort? Sequence { get; set; }

    public byte? Battery { get; set; }

    public byte? Humidity { get; set; }

    // Raw temperature byte index (0-255) exactly as it appeared on the wire.
    public byte TemperatureIndex { get; set; }

    // The emitter omits the Temperature field at exactly index 0 (a valid -40.0 C reading),
    // so absent is NOT an error. False here means the field was not on the wire.
    public bool TemperaturePresent { get; set; }

    // Reversed temperature. Null when the index signals a fault (254/255).
    public double? Celsius { get; set; }

    public double? Fahrenheit { get; set; }

    // "shorted" (index 254) or "open" (index 255); null otherwise.
    public string Fault { get; set; }
}
