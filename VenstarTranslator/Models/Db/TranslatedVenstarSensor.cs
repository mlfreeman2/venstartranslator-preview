using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Hangfire;

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

    // round the temperature and look the temperature up here and send that array index in the protobuf packet
    private static string[] Temperatures_Farenheit = ["-40", "-39", "-38", "-37", "-36", "-36", "-35", "-34", "-33", "-32", "-31", "-30", "-29", "-28", "-27", "-27", "-26", "-25", "-24", "-23", "-22", "-21", "-20", "-19", "-18", "-18", "-17", "-16", "-15", "-14", "-13", "-12", "-11", "-10", "-9", "-9", "-8", "-7", "-6", "-5", "-4", "-3", "-2", "-1", "0", "1", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "28", "29", "30", "31", "32", "33", "34", "35", "36", "37", "37", "38", "39", "40", "41", "42", "43", "44", "45", "46", "46", "47", "48", "49", "50", "51", "52", "53", "54", "55", "55", "56", "57", "58", "59", "60", "61", "62", "63", "64", "64", "65", "66", "67", "68", "69", "70", "71", "72", "73", "73", "74", "75", "76", "77", "78", "79", "80", "81", "82", "82", "83", "84", "85", "86", "87", "88", "89", "90", "91", "91", "92", "93", "94", "95", "96", "97", "98", "99", "100", "100", "101", "102", "103", "104", "105", "106", "107", "108", "109", "109", "110", "111", "112", "113", "114", "115", "116", "117", "118", "118", "119", "120", "121", "122", "123", "124", "125", "126", "127", "127", "128", "129", "130", "131", "132", "133", "134", "135", "136", "136", "137", "138", "139", "140", "141", "142", "143", "144", "145", "145", "146", "147", "148", "149", "150", "151", "152", "153", "154", "154", "155", "156", "157", "158", "159", "160", "161", "162", "163", "163", "164", "165", "166", "167", "168", "169", "170", "171", "172", "172", "173", "174", "175", "176", "177", "178", "179", "180", "181", "181", "182", "183", "184", "185", "186", "187", "188"];

    // round the temperature and look the temperature up here and send that array index in the protobuf packet
    private static string[] Temperatures_Celsius = ["-40.0", "-39.5", "-39.0", "-38.5", "-38.0", "-37.5", "-37.0", "-36.5", "-36.0", "-35.5", "-35.0", "-34.5", "-34.0", "-33.5", "-33.0", "-32.5", "-32.0", "-31.5", "-31.0", "-30.5", "-30.0", "-29.5", "-29.0", "-28.5", "-28.0", "-27.5", "-27.0", "-26.5", "-26.0", "-25.5", "-25.0", "-24.5", "-24.0", "-23.5", "-23.0", "-22.5", "-22.0", "-21.5", "-21.0", "-20.5", "-20.0", "-19.5", "-19.0", "-18.5", "-18.0", "-17.5", "-17.0", "-16.5", "-16.0", "-15.5", "-15.0", "-14.5", "-14.0", "-13.5", "-13.0", "-12.5", "-12.0", "-11.5", "-11.0", "-10.5", "-10.0", "-9.5", "-9.0", "-8.5", "-8.0", "-7.5", "-7.0", "-6.5", "-6.0", "-5.5", "-5.0", "-4.5", "-4.0", "-3.5", "-3.0", "-2.5", "-2.0", "-1.5", "-1.0", "-0.5", "0.0", "0.5", "1.0", "1.5", "2.0", "2.5", "3.0", "3.5", "4.0", "4.5", "5.0", "5.5", "6.0", "6.5", "7.0", "7.5", "8.0", "8.5", "9.0", "9.5", "10.0", "10.5", "11.0", "11.5", "12.0", "12.5", "13.0", "13.5", "14.0", "14.5", "15.0", "15.5", "16.0", "16.5", "17.0", "17.5", "18.0", "18.5", "19.0", "19.5", "20.0", "20.5", "21.0", "21.5", "22.0", "22.5", "23.0", "23.5", "24.0", "24.5", "25.0", "25.5", "26.0", "26.5", "27.0", "27.5", "28.0", "28.5", "29.0", "29.5", "30.0", "30.5", "31.0", "31.5", "32.0", "32.5", "33.0", "33.5", "34.0", "34.5", "35.0", "35.5", "36.0", "36.5", "37.0", "37.5", "38.0", "38.5", "39.0", "39.5", "40.0", "40.5", "41.0", "41.5", "42.0", "42.5", "43.0", "43.5", "44.0", "44.5", "45.0", "45.5", "46.0", "46.5", "47.0", "47.5", "48.0", "48.5", "49.0", "49.5", "50.0", "50.5", "51.0", "51.5", "52.0", "52.5", "53.0", "53.5", "54.0", "54.5", "55.0", "55.5", "56.0", "56.5", "57.0", "57.5", "58.0", "58.5", "59.0", "59.5", "60.0", "60.5", "61.0", "61.5", "62.0", "62.5", "63.0", "63.5", "64.0", "64.5", "65.0", "65.5", "66.0", "66.5", "67.0", "67.5", "68.0", "68.5", "69.0", "69.5", "70.0", "70.5", "71.0", "71.5", "72.0", "72.5", "73.0", "73.5", "74.0", "74.5", "75.0", "75.5", "76.0", "76.5", "77.0", "77.5", "78.0", "78.5", "79.0", "79.5", "80.0", "80.5", "81.0", "81.5", "82.0", "82.5", "83.0", "83.5", "84.0", "84.5", "85.0", "85.5", "86.0", "86.5"];

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
    /// The Venstar temperature index space (0-253) represents Celsius values from -40.0°C to 86.5°C
    /// in 0.5°C increments. Fahrenheit values are derived from Celsius using standard conversion.
    /// </summary>
    private static byte GetTemperatureIndexCalculated(double temperature, TemperatureScale scale)
    {
        double celsiusTemp;

        if (scale == TemperatureScale.F)
        {
            // Convert Fahrenheit to Celsius: C = (F - 32) × 5/9
            celsiusTemp = (temperature - 32.0) * 5.0 / 9.0;
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
        // Get temperature index using current array-based approach
        byte arrayBasedIndex = Scale switch
        {
            TemperatureScale.F => Convert.ToByte(Array.IndexOf(Temperatures_Farenheit, Math.Round(Convert.ToDecimal(latestReading), MidpointRounding.AwayFromZero).ToString())),
            TemperatureScale.C => Convert.ToByte(Array.IndexOf(Temperatures_Celsius, (Math.Round(Convert.ToDecimal(latestReading) * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.0"))),
            _ => throw new InvalidOperationException(),
        };

        // VALIDATION: Compare with calculation-based approach
        try
        {
            byte calculatedIndex = GetTemperatureIndexCalculated(latestReading, Scale);

            if (arrayBasedIndex != calculatedIndex)
            {
                // Check if this is due to Fahrenheit array duplicates
                bool isDuplicate = false;
                if (Scale == TemperatureScale.F)
                {
                    var roundedTemp = Math.Round(Convert.ToDecimal(latestReading), MidpointRounding.AwayFromZero).ToString();
                    var firstIndex = Array.IndexOf(Temperatures_Farenheit, roundedTemp);
                    var lastIndex = Array.LastIndexOf(Temperatures_Farenheit, roundedTemp);
                    isDuplicate = firstIndex != lastIndex;
                }

                if (isDuplicate)
                {
                    Console.WriteLine($"[TEMPERATURE INDEX MISMATCH - ARRAY DUPLICATE] Sensor #{SensorID} ({Name}): Temp={latestReading}°{Scale}, Array={arrayBasedIndex} (first occurrence), Calculated={calculatedIndex} (from Celsius conversion)");
                }
                else
                {
                    Console.WriteLine($"[TEMPERATURE INDEX MISMATCH] Sensor #{SensorID} ({Name}): Temp={latestReading}°{Scale}, Array={arrayBasedIndex}, Calculated={calculatedIndex}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TEMPERATURE CALCULATION ERROR] Sensor #{SensorID} ({Name}): Temp={latestReading}°{Scale}, Error={ex.Message}");
        }

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
                    Temperature = arrayBasedIndex  // Use array-based value for actual packet
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
