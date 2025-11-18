using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using VenstarTranslator.Exceptions;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Models.Protobuf;
using VenstarTranslator.Models.Validation;

namespace VenstarTranslator.Models.Db;

public class TranslatedVenstarSensor
{
    public static string macPrefix = "";

    [Key]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Sensor ID is required.")]
    [Range(0, 19, ErrorMessage = "Sensor ID must be between 0 and 19.")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public byte SensorID { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Sensor name is required and cannot be empty.")]
    [MaxLength(14, ErrorMessage = "Sensor name cannot exceed 14 characters.")]
    public string Name { get; set; }

    public bool Enabled { get; set; }

    public ushort Sequence { get; set; }

    public string MacAddress => (macPrefix + SensorID.ToString("X2")).ToLower();

    public string Signature_Key
    {
        get
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(MacAddress);
                return Convert.ToBase64String(sha256.ComputeHash(bytes));
            }
        }
    }

    public string HangfireJobName => $"Sensor #{SensorID}: {Name}";

    public string CronExpression => Purpose == SensorPurpose.Outdoor ? "*/5 * * * *" : "* * * * *";

    [Required(ErrorMessage = "Sensor purpose is required.")]
    public SensorPurpose Purpose { get; set; }

    [Required(ErrorMessage = "Temperature scale is required.")]
    public TemperatureScale Scale { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "URL is required and cannot be empty.")]
    [ValidAbsoluteUrl]
    public string URL { get; set; }

    public bool IgnoreSSLErrors { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "JSONPath is required.")]
    [ValidJsonPath]
    public string JSONPath { get; set; }

    [ValidHttpHeaders]
    public List<DataSourceHttpHeader> Headers { get; set; }

    public DateTime? LastSuccessfulBroadcast { get; set; }

    public string LastErrorMessage { get; set; }

    public int ConsecutiveFailures { get; set; }

    [NotMapped]
    public int FailureThreshold => 5;

    [NotMapped]
    public bool HasProblem
    {
        get
        {
            if (!Enabled)
            {
                return false;
            }

            // Check if we have too many consecutive failures
            // 5 consecutive failures means:
            //   - Outdoor sensors: 5 × 5 min = 25 minutes of failures (exceeds 20 min threshold)
            //   - Other sensors: 5 × 1 min = 5 minutes of failures (meets 5 min threshold)
            return ConsecutiveFailures >= FailureThreshold;
        }
    }

    /// <summary>
    /// Calculate temperature index directly from temperature value without using lookup arrays.
    /// This is a NEW experimental method being validated against the production array-based approach.
    ///
    /// <para><b>Temperature Index Space:</b></para>
    /// The Venstar protocol uses byte values (0-255) to represent the current temperature.
    /// Valid temperatures will have a byte value from 0 through 253.
    /// 254 means shorted sensor and 255 means open sensor.
    /// The values 0 through 253 map to Celsius temperatures in 0.5°C increments from -40.0°C to 86.5°C
    /// or Farenheit temperatures in 1°F increments from -40°F to 188°F.
    ///
    /// <para><b>Our Mapping Process</b></para>
    /// 1. If Farenheit, round to nearest whole degree (half up) and convert to Celsius.
    /// 2. Round to nearest half-degree increment.
    /// 3. Add 40.
    /// 4. Multiply by 2
    ///
    /// <para><b>Special Note:</b></para>
    /// When using this with Farenheit temperatures, some values between 0 and 253 will never come up.
    /// To understand what's going on, imagine the Celsius side as an array with 254 temperatures in it.
    /// The corresponding Farenheit array comes from the following process:
    /// 1. Convert C to F.
    /// 2A. If F is less than 0, round [half-up] the absolute value Farenheit rather than the converted value.
    /// 2A1. Multiply the result by -1 to kick it back to negative.
    /// 2B. If F is greater than or equal to zero, round [half-up] the Farenheit value
    ///
    /// There are a few cases where that results in a Farenheit value appearing twice in a row.
    /// For example: -38°C converts to -36.4°F which rounds to -36°F
    ///              -37.5°C converts to -35.5°F which rounds also to -36°F
    /// For example: 22.5°C converts to 72.5°F which rounds also to 73°F
    ///              23°C converts to  73.4°F which rounds also to 73°F
    /// </summary>
    private static byte GetTemperatureIndexCalculated(double temperature, TemperatureScale scale)
    {
        double celsiusTemp;

        if (scale == TemperatureScale.F)
        {
            // Round Fahrenheit to whole degrees first
            decimal roundedFahrenheit;
            if (temperature < 0)
            {
                roundedFahrenheit = -1 * Math.Round(Convert.ToDecimal(Math.Abs(temperature)), MidpointRounding.AwayFromZero);
            }
            else
            {
                roundedFahrenheit = Math.Round(Convert.ToDecimal(temperature), MidpointRounding.AwayFromZero);
            }
            // Convert rounded Fahrenheit to Celsius: C = (F - 32) × 5/9
            celsiusTemp = (Convert.ToDouble(roundedFahrenheit) - 32.0) * 5.0 / 9.0;
        }
        else
        {
            celsiusTemp = temperature;
        }

        // Round to nearest 0.5°C (multiply by 2, round, divide by 2)
        var roundedCelsius = Math.Round(Convert.ToDecimal(celsiusTemp) * 2, MidpointRounding.AwayFromZero) / 2;

        // Check bounds (-40.0°C to 86.5°C)
        if (roundedCelsius < -40.0m || roundedCelsius > 86.5m)
        {
            throw new OverflowException($"Temperature {temperature}°{scale} (={roundedCelsius}°C) is outside the valid range of -40.0°C to 86.5°C");
        }

        // Calculate index: index = (celsius + 40) × 2
        // -40.0°C → index 0
        // -39.5°C → index 1
        // 0.0°C → index 80
        // 22.0°C → index 124
        // 86.5°C → index 253
        var index = (roundedCelsius + 40.0m) * 2;

        return Convert.ToByte(index);
    }

    private SensorMessage BuildProtobufPacket(double latestReading)
    {
        byte temperatureIndex = GetTemperatureIndexCalculated(latestReading, Scale);

        return new SensorMessage
        {
            Command = SensorMessage.Commands.SENSORDATA,
            SensorData = new SENSORDATA
            {
                Info = new INFO
                {
                    Sequence = Sequence,
                    SensorId = SensorID,
                    Mac = MacAddress,
                    Type = Purpose switch
                    {
                        SensorPurpose.Outdoor => INFO.SensorType.OUTDOOR,
                        SensorPurpose.Remote => INFO.SensorType.REMOTE,
                        SensorPurpose.Return => INFO.SensorType.RETURN,
                        SensorPurpose.Supply => INFO.SensorType.SUPPLY,
                        _ => throw new InvalidOperationException(),
                    },
                    Name = Name,
                    Temperature = temperatureIndex
                }
            }
        };
    }

    public byte[] BuildPairingPacket(double latestReading)
    {
        var dataPacket = BuildProtobufPacket(latestReading);
        dataPacket.Command = SensorMessage.Commands.SENSORPAIR;
        dataPacket.SensorData.Signature = Signature_Key;
        dataPacket.SensorData.Info.Sequence = 1;
        Sequence = 1;
        return dataPacket.Serialize();
    }

    public byte[] BuildDataPacket(double latestReading)
    {
        var dataPacket = BuildProtobufPacket(latestReading);

        using (HMACSHA256 hmac = new(Convert.FromBase64String(Signature_Key)))
        using (MemoryStream ms = new())
        {
            var bytes = dataPacket.SensorData.Info.Serialize();
            dataPacket.SensorData.Signature = Convert.ToBase64String(hmac.ComputeHash(bytes));
        }

        Sequence += 1;
        if (Sequence >= 65000)
        {
            Sequence = 1;
        }

        return dataPacket.Serialize();
    }

    public double ExtractValue(string jsonDocument)
    {
        try
        {
            var jToken = JToken.Parse(jsonDocument);
            var field = jToken.SelectToken(JSONPath)?.Value<string>();

            if (string.IsNullOrWhiteSpace(field))
            {
                throw new VenstarTranslatorException("The specified JSON Path failed to find anything.");
            }

            // extract a positive or negative (brrr) number from the field regardless of other crap in it too
            var target = Regex.Match(field, @"(-?\d+(.\d+)?)").Value;
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new VenstarTranslatorException("The specified JSON Path found a non-numeric value.");
            }
            return Convert.ToDouble(target);
        }
        catch (JsonReaderException ex)
        {
            throw new VenstarTranslatorException($"Invalid JSON document: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new VenstarTranslatorException($"JSON Path error: {ex.Message}", ex);
        }
    }
}
