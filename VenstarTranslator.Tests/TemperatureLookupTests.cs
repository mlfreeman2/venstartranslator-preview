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
    [InlineData(72.5, TemperatureScale.F, 126)] // 72.5°F → rounds to 73°F → 22.78°C → rounds to 23.0°C → index 126
    [InlineData(71.5, TemperatureScale.F, 124)] // 71.5°F → rounds to 72°F → 22.22°C → rounds to 22.0°C → index 124
    [InlineData(-40.0, TemperatureScale.F, 0)] // Min Fahrenheit
    [InlineData(188.0, TemperatureScale.F, 253)] // 188°F → 86.67°C → rounds to 86.5°C → index 253
    [InlineData(67.5, TemperatureScale.F, 120)] // 67.5°F → rounds to 68°F → 20.0°C → index 120
    [InlineData(72.3, TemperatureScale.F, 124)] // 72.3°F → rounds to 72°F → 22.22°C → rounds to 22.0°C → index 124
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
    public void CelsiusRounding_MatchesExpectedBehavior(double input, string expectedString)
    {
        // This test verifies the rounding logic for Celsius temperatures
        // Round to nearest 0.5°C (multiply by 2, round, divide by 2)
        var rounded = (Math.Round(Convert.ToDecimal(input) * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.0");
        Assert.Equal(expectedString, rounded);
    }

    [Theory]
    [InlineData(72, "72")]
    [InlineData(72.4, "72")]
    [InlineData(72.5, "73")] // AwayFromZero rounding
    [InlineData(-40, "-40")]
    public void FahrenheitRounding_MatchesExpectedBehavior(double input, string expectedString)
    {
        // This test verifies the rounding logic for Fahrenheit temperatures
        var rounded = Math.Round(Convert.ToDecimal(input), MidpointRounding.AwayFromZero).ToString();
        Assert.Equal(expectedString, rounded);
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
    [InlineData(67.5, TemperatureScale.F, 120)] // 67.5°F → rounds to 68°F → 20°C → index 120
    [InlineData(72.3, TemperatureScale.F, 124)] // 72.3°F → rounds to 72°F → 22.22°C → rounds to 22.0°C → index 124
    [InlineData(72.5, TemperatureScale.F, 126)] // 72.5°F → rounds to 73°F → 22.78°C → rounds to 23.0°C → index 126
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
    public void CalculatedIndex_AllValidCelsiusValues_ProduceCorrectIndices()
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Test every valid Celsius value from -40.0 to 86.5 in 0.5 increments
        for (int expectedIndex = 0; expectedIndex <= 253; expectedIndex++)
        {
            double tempValue = -40.0 + (expectedIndex * 0.5);
            var calculatedIndex = (byte)method.Invoke(null, new object[] { tempValue, TemperatureScale.C });

            Assert.Equal(expectedIndex, calculatedIndex);
        }
    }

    [Fact]
    public void CalculatedIndex_NegativeFahrenheit_HandlesCorrectly()
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Test negative Fahrenheit rounding behavior
        // -0.5°F should round to -1°F → -18.33°C → rounds to -18.5°C → index 43
        var result = (byte)method.Invoke(null, new object[] { -0.5, TemperatureScale.F });
        Assert.Equal(43, result);

        // -36.4°F should round to -36°F → -37.78°C → rounds to -38.0°C → index 4
        result = (byte)method.Invoke(null, new object[] { -36.4, TemperatureScale.F });
        Assert.Equal(4, result);

        // -35.5°F should round to -36°F → -37.78°C → rounds to -38.0°C → index 4
        result = (byte)method.Invoke(null, new object[] { -35.5, TemperatureScale.F });
        Assert.Equal(4, result);
    }

    [Fact]
    public void CalculatedIndex_BoundaryValues_ProduceCorrectIndices()
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Test boundary values for Celsius
        Assert.Equal(0, (byte)method.Invoke(null, new object[] { -40.0, TemperatureScale.C }));
        Assert.Equal(253, (byte)method.Invoke(null, new object[] { 86.5, TemperatureScale.C }));

        // Test boundary values for Fahrenheit
        Assert.Equal(0, (byte)method.Invoke(null, new object[] { -40.0, TemperatureScale.F }));
        Assert.Equal(253, (byte)method.Invoke(null, new object[] { 188.0, TemperatureScale.F }));
    }

    [Theory]
    [InlineData(-36.0, TemperatureScale.F)] // Duplicate F value - appears at both indices 4 and 5 in legacy array
    [InlineData(73.0, TemperatureScale.F)]  // Duplicate F value - appears at both indices 126 and 127 in legacy array
    public void CalculatedIndex_DuplicateFahrenheitValues_ProduceConsistentIndex(double temperature, TemperatureScale scale)
    {
        // Use reflection to access the private calculation method
        var method = typeof(TranslatedVenstarSensor).GetMethod(
            "GetTemperatureIndexCalculated",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // These values appeared multiple times in the legacy Fahrenheit array
        // The calculation-based approach always produces a consistent index
        var result = (byte)method.Invoke(null, new object[] { temperature, scale });

        // -36°F → -37.78°C → rounds to -38.0°C → index 4
        if (temperature == -36.0)
        {
            Assert.Equal(4, result);
        }
        // 73°F → 22.78°C → rounds to 23.0°C → index 126
        else if (temperature == 73.0)
        {
            Assert.Equal(126, result);
        }
    }
}
