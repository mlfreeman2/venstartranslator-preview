using System;
using System.IO;

using ProtoBuf;

using VenstarTranslator.Models.ProtobufCapture;

using Wire = VenstarTranslator.Models.ProtobufCapture.Wire;

namespace VenstarTranslator.Services;

// Pure, stateless decode of a single UDP datagram into a CapturedMessage.
// No socket, no buffer — just bytes in, decoded message out. The receive loop and the
// import endpoint share this exact pipeline so live and imported captures decode identically.
public static class VenstarPacketDecoder
{
    // Builds a CapturedMessage from raw bytes. Never throws: a decode failure comes back as
    // Decoded=false with the raw hex still populated so the UI can show timestamp/source/hex.
    public static CapturedMessage Decode(byte[] data)
    {
        var message = new CapturedMessage
        {
            Length = data?.Length ?? 0,
            Hex = data == null ? string.Empty : Convert.ToHexString(data).ToLowerInvariant(),
            Decoded = false,
        };

        if (data == null || data.Length == 0)
        {
            // protobuf-net parses an empty buffer into a default instance without throwing;
            // short-circuit so it doesn't masquerade as a decoded message.
            message.DecodeError = "Empty datagram — not decodable as a Venstar message.";
            return message;
        }

        Wire.SensorMessage parsed;
        try
        {
            using var stream = new MemoryStream(data);
            parsed = Serializer.Deserialize<Wire.SensorMessage>(stream);
        }
        catch (Exception ex)
        {
            message.DecodeError = $"Not decodable as a Venstar message: {ex.Message}";
            return message;
        }

        var gateError = ValidateVenstar(parsed);
        if (gateError != null)
        {
            message.DecodeError = gateError;
            return message;
        }

        message.Decoded = true;
        message.Command = parsed.Command?.ToString();
        message.Body = parsed;

        if (parsed.Command == Wire.SensorMessage.Commands.SENSORDATA ||
            parsed.Command == Wire.SensorMessage.Commands.SENSORPAIR)
        {
            message.Summary = BuildSummary(parsed.SensorData.Info);
        }

        return message;
    }

    // Gate: protobuf-net returns a default instance for an empty buffer (already handled),
    // tolerates unknown fields, and does not enforce proto2 required on read — so garbage can
    // "parse." Require a defined Command plus, for body-bearing commands, the expected body.
    // Returns null when the message is a valid Venstar message, or an error string otherwise.
    private static string ValidateVenstar(Wire.SensorMessage message)
    {
        if (message.Command == null || !Enum.IsDefined(message.Command.Value))
        {
            return "Not decodable as a Venstar message: no recognized Command field.";
        }

        switch (message.Command.Value)
        {
            case Wire.SensorMessage.Commands.SENSORDATA:
            case Wire.SensorMessage.Commands.SENSORPAIR:
                if (message.SensorData?.Info == null || string.IsNullOrEmpty(message.SensorData.Info.Mac))
                {
                    return "Not decodable as a Venstar message: sensor packet missing Info/Mac.";
                }
                break;

            case Wire.SensorMessage.Commands.SETSENSORNAME:
                if (message.SensorName == null)
                {
                    return "Not decodable as a Venstar message: SETSENSORNAME missing body.";
                }
                break;

            case Wire.SensorMessage.Commands.WIFICONFIG:
                if (message.WifiConfig == null)
                {
                    return "Not decodable as a Venstar message: WIFICONFIG missing body.";
                }
                break;

            case Wire.SensorMessage.Commands.WIFISCANRESULTS:
                if (message.WifiScanResults == null)
                {
                    return "Not decodable as a Venstar message: WIFISCANRESULTS missing body.";
                }
                break;

            case Wire.SensorMessage.Commands.FIRMWARECHUNK:
                if (message.FirmwareChunk == null)
                {
                    return "Not decodable as a Venstar message: FIRMWARECHUNK missing body.";
                }
                break;

            case Wire.SensorMessage.Commands.FIRMWARECOMPLETE:
                if (message.FirmwareComplete == null)
                {
                    return "Not decodable as a Venstar message: FIRMWARECOMPLETE missing body.";
                }
                break;

            // SUCCESS / FAILURE are body-less; a defined Command alone suffices.
            case Wire.SensorMessage.Commands.SUCCESS:
            case Wire.SensorMessage.Commands.FAILURE:
                break;
        }

        return null;
    }

    private static SensorSummary BuildSummary(Wire.INFO info)
    {
        var summary = new SensorSummary
        {
            SensorId = info.SensorId,
            Mac = info.Mac,
            Name = info.Name,
            Purpose = info.Type?.ToString(),
            Sequence = info.Sequence,
            Battery = info.Battery,
            Humidity = info.Humidity,
            TemperaturePresent = info.Temperature.HasValue,
            // Absent Temperature => index 0 (the emitter omits the field at exactly index 0,
            // a valid -40.0 C reading), so treat absent as index 0 rather than an error.
            TemperatureIndex = info.Temperature ?? 0,
        };

        switch (summary.TemperatureIndex)
        {
            case 254:
                summary.Fault = "shorted";
                break;
            case 255:
                summary.Fault = "open";
                break;
            default:
                // What the thermostat sees at 0.5 C resolution. The original F source reading
                // isn't recoverable from the index.
                double celsius = summary.TemperatureIndex / 2.0 - 40.0;
                summary.Celsius = celsius;
                summary.Fahrenheit = celsius * 9.0 / 5.0 + 32.0;
                break;
        }

        return summary;
    }
}
