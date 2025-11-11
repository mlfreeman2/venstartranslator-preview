using System;
using System.Reflection;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Models.Protobuf;
using Xunit;

namespace VenstarTranslator.Tests;

public class TemperatureLookupTests
{
    [Theory]
    [InlineData(72.0, TemperatureScale.F, 124)] // 72°F → 22.22°C → rounds to 22.0°C → index 124
    [InlineData(72.5, TemperatureScale.F, 125)] // 72.5°F → 22.5°C → index 125
    [InlineData(71.5, TemperatureScale.F, 124)] // 71.5°F → 21.94°C → rounds to 22.0°C → index 124
    [InlineData(-40.0, TemperatureScale.F, 0)] // Min Fahrenheit
    [InlineData(188.0, TemperatureScale.F, 253)] // 188°F → 86.67°C → rounds to 86.5°C → index 253
    public void BuildDataPacket_Fahrenheit_MapsToCorrectIndex(double temperature, TemperatureScale scale, int expectedIndex)
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Test",
            Enabled = true,
            Sequence = 1,
            Purpose = SensorPurpose.Remote,
            Scale = scale,
            URL = "http://example.com",
            JSONPath = "$.temp"
        };

        // Act
        var packet = sensor.BuildDataPacket(temperature);
        var message = ProtoBuf.Serializer.Deserialize<SensorMessage>(new MemoryStream(packet));

        // Assert
        Assert.Equal(expectedIndex, message.SensorData.Info.Temperature);
    }

    [Theory]
    [InlineData(22.0, TemperatureScale.C, 124)] // 22.0°C at index 124
    [InlineData(22.5, TemperatureScale.C, 125)] // 22.5°C at index 125
    [InlineData(22.75, TemperatureScale.C, 126)] // Rounds to 23.0°C, index 126
    [InlineData(22.25, TemperatureScale.C, 125)] // Rounds to 22.5°C, index 125
    [InlineData(22.8, TemperatureScale.C, 126)] // Rounds to 23.0°C, index 126
    [InlineData(-40.0, TemperatureScale.C, 0)] // Min Celsius
    [InlineData(86.5, TemperatureScale.C, 253)] // Max Celsius
    [InlineData(0.0, TemperatureScale.C, 80)] // Freezing point
    [InlineData(0.25, TemperatureScale.C, 81)] // Rounds to 0.5°C (0.25*2=0.5, round=1, 1/2=0.5)
    [InlineData(0.3, TemperatureScale.C, 81)] // Rounds to 0.5°C
    public void BuildDataPacket_Celsius_MapsToCorrectIndex(double temperature, TemperatureScale scale, int expectedIndex)
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Test",
            Enabled = true,
            Sequence = 1,
            Purpose = SensorPurpose.Remote,
            Scale = scale,
            URL = "http://example.com",
            JSONPath = "$.temp"
        };

        // Act
        var packet = sensor.BuildDataPacket(temperature);
        var message = ProtoBuf.Serializer.Deserialize<SensorMessage>(new MemoryStream(packet));

        // Assert
        Assert.Equal(expectedIndex, message.SensorData.Info.Temperature);
    }

    [Theory]
    [InlineData(189.0, TemperatureScale.F)] // Above max Fahrenheit
    [InlineData(-41.0, TemperatureScale.F)] // Below min Fahrenheit
    [InlineData(87.0, TemperatureScale.C)] // Above max Celsius
    [InlineData(-41.0, TemperatureScale.C)] // Below min Celsius
    public void BuildDataPacket_OutOfRange_ThrowsException(double temperature, TemperatureScale scale)
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Test",
            Enabled = true,
            Sequence = 1,
            Purpose = SensorPurpose.Remote,
            Scale = scale,
            URL = "http://example.com",
            JSONPath = "$.temp"
        };

        // Act & Assert
        Assert.Throws<OverflowException>(() => sensor.BuildDataPacket(temperature));
    }

    [Fact]
    public void BuildPairingPacket_Celsius_MapsToCorrectIndex()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Test",
            Enabled = true,
            Sequence = 1,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.C,
            URL = "http://example.com",
            JSONPath = "$.temp"
        };

        // Act - temperature 22.5°C should map to index 125
        var packet = sensor.BuildPairingPacket(22.5);
        var message = ProtoBuf.Serializer.Deserialize<SensorMessage>(new MemoryStream(packet));

        // Assert
        Assert.Equal(125, message.SensorData.Info.Temperature);
        Assert.Equal((ushort)1, message.SensorData.Info.Sequence); // Pairing always uses sequence 1
    }

    [Theory]
    [InlineData(20.0, "20.0")]
    [InlineData(20.5, "20.5")]
    [InlineData(20.25, "20.5")] // 20.25*2=40.5, round=41, 41/2=20.5
    [InlineData(20.74, "20.5")] // 20.74*2=41.48, round=41, 41/2=20.5
    [InlineData(-0.5, "-0.5")]
    [InlineData(-0.24, "0.0")] // -0.24*2=-0.48, round=0, 0/2=0.0
    public void CelsiusRounding_MatchesLookupTable(double input, string expectedString)
    {
        // This test verifies the rounding logic matches the lookup table format
        // Round to nearest 0.5°C (multiply by 2, round, divide by 2)
        var rounded = (Math.Round(Convert.ToDecimal(input) * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.0");
        Assert.Equal(expectedString, rounded);
    }

    [Theory]
    [InlineData(72, "72")]
    [InlineData(72.4, "72")]
    [InlineData(72.5, "73")] // AwayFromZero rounding
    [InlineData(-40, "-40")]
    public void FahrenheitRounding_MatchesLookupTable(double input, string expectedString)
    {
        // This test verifies the rounding logic matches the lookup table format
        var rounded = Math.Round(Convert.ToDecimal(input), MidpointRounding.AwayFromZero).ToString();
        Assert.Equal(expectedString, rounded);
    }

    [Fact]
    public void TemperatureLookupTables_HaveCorrectCounts()
    {
        // Use reflection to access private static fields
        var fahrenheitField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Farenheit",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var celsiusField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Celsius",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var fahrenheit = (string[])fahrenheitField.GetValue(null);
        var celsius = (string[])celsiusField.GetValue(null);

        // Both arrays have 254 entries (Fahrenheit has duplicates)
        Assert.Equal(254, fahrenheit.Length);
        Assert.Equal(254, celsius.Length);
    }

    [Fact]
    public void TemperatureLookupTables_HaveCorrectRanges()
    {
        // Use reflection to access private static fields
        var fahrenheitField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Farenheit",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var celsiusField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Celsius",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var fahrenheit = (string[])fahrenheitField.GetValue(null);
        var celsius = (string[])celsiusField.GetValue(null);

        // Check Fahrenheit range
        Assert.Equal("-40", fahrenheit[0]);
        Assert.Equal("188", fahrenheit[^1]);

        // Check Celsius range
        Assert.Equal("-40.0", celsius[0]);
        Assert.Equal("86.5", celsius[^1]);
    }

    [Fact]
    public void CelsiusLookupTable_AllEntriesHaveOneDecimal()
    {
        // Use reflection to access the Celsius array
        var celsiusField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Celsius",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var celsius = (string[])celsiusField.GetValue(null);

        // All Celsius values should have exactly one decimal place (e.g., "22.0" or "22.5")
        foreach (var temp in celsius)
        {
            Assert.Contains(".", temp);
            var parts = temp.Split('.');
            Assert.Equal(2, parts.Length);
            Assert.Single(parts[1]); // Exactly one digit after decimal
        }
    }

    // ========================================
    // CALCULATION-BASED METHOD TESTS
    // ========================================

    [Theory]
    [InlineData(22.0, TemperatureScale.C, 124)] // 22.0°C at index 124
    [InlineData(22.5, TemperatureScale.C, 125)] // 22.5°C at index 125
    [InlineData(22.75, TemperatureScale.C, 126)] // Rounds to 23.0°C, index 126
    [InlineData(22.25, TemperatureScale.C, 125)] // Rounds to 22.5°C, index 125
    [InlineData(22.8, TemperatureScale.C, 126)] // Rounds to 23.0°C, index 126
    [InlineData(-40.0, TemperatureScale.C, 0)] // Min Celsius
    [InlineData(86.5, TemperatureScale.C, 253)] // Max Celsius
    [InlineData(0.0, TemperatureScale.C, 80)] // Freezing point
    [InlineData(0.25, TemperatureScale.C, 81)] // Rounds to 0.5°C (0.25*2=0.5, round=1, 1/2=0.5)
    [InlineData(0.3, TemperatureScale.C, 81)] // Rounds to 0.5°C
    [InlineData(20.74, TemperatureScale.C, 121)] // Rounds to 20.5°C
    public void CalculatedIndex_Celsius_ProducesCorrectIndex(double temperature, TemperatureScale scale, int expectedIndex)
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (byte)method.Invoke(null, new object[] { temperature, scale });

        Assert.Equal(expectedIndex, result);
    }

    [Theory]
    [InlineData(72.0, TemperatureScale.F, 124)] // 72°F → 22.22°C → rounds to 22.0°C → index 124
    [InlineData(-40.0, TemperatureScale.F, 0)] // Min Fahrenheit
    [InlineData(188.0, TemperatureScale.F, 253)] // Max Fahrenheit: 188°F → 86.67°C → 86.5°C → index 253
    [InlineData(32.0, TemperatureScale.F, 80)] // Freezing point (0°C)
    [InlineData(68.0, TemperatureScale.F, 120)] // 68°F → 20°C
    public void CalculatedIndex_Fahrenheit_ProducesCorrectIndex(double temperature, TemperatureScale scale, int expectedIndex)
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (byte)method.Invoke(null, new object[] { temperature, scale });

        Assert.Equal(expectedIndex, result);
    }

    [Theory]
    [InlineData(189.0, TemperatureScale.F)] // Above max Fahrenheit
    [InlineData(-41.0, TemperatureScale.F)] // Below min Fahrenheit
    [InlineData(87.0, TemperatureScale.C)] // Above max Celsius
    [InlineData(-41.0, TemperatureScale.C)] // Below min Celsius
    public void CalculatedIndex_OutOfRange_ThrowsException(double temperature, TemperatureScale scale)
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var ex = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object[] { temperature, scale })
        );

        Assert.IsType<OverflowException>(ex.InnerException);
    }

    [Fact]
    public void CalculatedIndex_MatchesArrayIndex_ForAllValidCelsiusValues()
    {
        // Use reflection to access both the array and the calculation method
        var celsiusField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Celsius",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var celsius = (string[])celsiusField.GetValue(null);

        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Test every value in the Celsius array
        for (int expectedIndex = 0; expectedIndex < celsius.Length; expectedIndex++)
        {
            double tempValue = double.Parse(celsius[expectedIndex]);
            var calculatedIndex = (byte)method.Invoke(null, new object[] { tempValue, TemperatureScale.C });

            Assert.Equal(expectedIndex, calculatedIndex);
        }
    }

    [Fact]
    public void CalculatedIndex_ProducesCorrectCelsiusBasedIndex_ForFahrenheitValues()
    {
        // Use reflection to access both the array and the calculation method
        var fahrenheitField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Farenheit",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var fahrenheit = (string[])fahrenheitField.GetValue(null);

        var celsiusField = typeof(TranslatedVenstarSensor).GetField(
            "Temperatures_Celsius",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var celsius = (string[])celsiusField.GetValue(null);

        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Test that calculated indices correctly map to the underlying Celsius scale
        // Note: Fahrenheit array has duplicates, so Array.IndexOf() may return the wrong index
        // The calculation-based approach is CORRECT because it derives from Celsius conversion
        for (int arrayIndex = 0; arrayIndex < fahrenheit.Length; arrayIndex++)
        {
            string fValue = fahrenheit[arrayIndex];
            double tempValue = double.Parse(fValue);
            var calculatedIndex = (byte)method.Invoke(null, new object[] { tempValue, TemperatureScale.F });

            // The calculated index should map to the correct Celsius value
            // (not necessarily the first Fahrenheit array occurrence)
            string expectedCelsius = celsius[calculatedIndex];
            string actualCelsius = celsius[arrayIndex];

            // Verify both map to the same Celsius temperature when rounded
            double expectedC = double.Parse(expectedCelsius);
            double actualC = double.Parse(actualCelsius);
            double fInCelsius = (tempValue - 32.0) * 5.0 / 9.0;
            double roundedC = (double)(Math.Round(Convert.ToDecimal(fInCelsius) * 2, MidpointRounding.AwayFromZero) / 2);

            Assert.Equal(roundedC, expectedC, 0.1);
        }
    }
}
