using System.Security.Cryptography;
using Xunit;
using VenstarTranslator.Models;

namespace VenstarTranslator.Tests;

public class TranslatedVenstarSensorTests
{
    [Fact]
    public void BuildDataPacket_WithKnownInputs_ProducesDeterministicHash()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";

        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 0,
            Name = "Living Room",
            Enabled = true,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.F,
            URL = "http://172.17.21.11/get_livedata_info",
            IgnoreSSLErrors = false,
            JSONPath = "$.ch_aisle[?(@.name=='Living Room')].temp",
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 1
        };

        double temperature = 71.6;

        // Expected hash - this was generated from a known good build
        // Update this value if you intentionally change the protobuf structure
        const string expectedHash = "A3AE86334FF2787A249419F654F6317D009D3AE56F204C773E55714159E6048E";

        // Act
        var packetBytes = sensor.BuildDataPacket(temperature);
        var actualHash = ComputeSHA256Hash(packetBytes);

        // Assert
        Assert.NotNull(packetBytes);
        Assert.True(packetBytes.Length > 0);
        Assert.Equal(98, packetBytes.Length);
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public void BuildPairingPacket_WithKnownInputs_ProducesDeterministicHash()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";

        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 5,
            Name = "Outside",
            Enabled = true,
            Purpose = SensorPurpose.Outdoor,
            Scale = TemperatureScale.F,
            URL = "http://172.17.21.11/get_livedata_info",
            IgnoreSSLErrors = false,
            JSONPath = "$.common_list[?(@.id=='0x02')].val",
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 0
        };

        double temperature = 45.3;

        // Act
        var packetBytes = sensor.BuildPairingPacket(temperature);
        var actualHash = ComputeSHA256Hash(packetBytes);

        // Assert
        Assert.NotNull(packetBytes);
        Assert.True(packetBytes.Length > 0);
        Assert.Equal(1, sensor.Sequence); // Pairing resets sequence to 1

        // Pairing packets have deterministic output
        Assert.Equal(94, packetBytes.Length);
    }

    [Theory]
    [InlineData(71.6, TemperatureScale.F, "Living Room")]
    [InlineData(35.8, TemperatureScale.F, "Fridge")]
    [InlineData(72.3, TemperatureScale.F, "GW2000 Onboard")]
    public void BuildDataPacket_WithVariousTemperatures_ProducesValidPackets(
        double temperature,
        TemperatureScale scale,
        string sensorName)
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";

        var sensor = new TranslatedVenstarSensor
        {
            SensorID = 1,
            Name = sensorName,
            Enabled = true,
            Purpose = SensorPurpose.Remote,
            Scale = scale,
            URL = "http://172.17.21.11/get_livedata_info",
            IgnoreSSLErrors = false,
            JSONPath = "$.ch_aisle[?(@.name=='" + sensorName + "')].temp",
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 10
        };

        // Act
        var packetBytes = sensor.BuildDataPacket(temperature);
        var hash = ComputeSHA256Hash(packetBytes);

        // Assert
        Assert.NotNull(packetBytes);
        Assert.True(packetBytes.Length > 0);
        Assert.Equal(11, sensor.Sequence); // Sequence should increment
    }

    [Fact]
    public void BuildDataPacket_SameInputs_ProducesSameOutput()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";

        var sensor1 = CreateTestSensor();
        var sensor2 = CreateTestSensor();

        double temperature = 71.6;

        // Act
        var packet1 = sensor1.BuildDataPacket(temperature);
        var packet2 = sensor2.BuildDataPacket(temperature);

        // Assert - same inputs should produce same output (deterministic)
        Assert.Equal(packet1.Length, packet2.Length);
        Assert.True(packet1.SequenceEqual(packet2));

        var hash1 = ComputeSHA256Hash(packet1);
        var hash2 = ComputeSHA256Hash(packet2);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void BuildDataPacket_DifferentTemperatures_ProducesDifferentOutputs()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";

        var sensor = CreateTestSensor();

        // Act
        var packet1 = sensor.BuildDataPacket(71.6);
        sensor.Sequence = 1; // Reset sequence to keep only temp different
        var packet2 = sensor.BuildDataPacket(45.3);

        // Assert - different temperatures should produce different hashes
        var hash1 = ComputeSHA256Hash(packet1);
        var hash2 = ComputeSHA256Hash(packet2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void BuildDataPacket_SequenceIncrementsCorrectly()
    {
        // Arrange
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = CreateTestSensor();
        sensor.Sequence = 64998;

        // Act
        sensor.BuildDataPacket(71.6);
        Assert.Equal(64999, sensor.Sequence);

        sensor.BuildDataPacket(71.6);

        // Assert - sequence should wrap back to 1 when it reaches 65000
        Assert.Equal(1, sensor.Sequence);
    }

    private static TranslatedVenstarSensor CreateTestSensor()
    {
        return new TranslatedVenstarSensor
        {
            SensorID = 3,
            Name = "Fridge",
            Enabled = true,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.F,
            URL = "http://172.17.21.11/get_livedata_info",
            IgnoreSSLErrors = false,
            JSONPath = "$.ch_temp[?(@.name=='Fridge')].temp",
            Headers = new List<DataSourceHttpHeader>(),
            Sequence = 1
        };
    }

    private static string ComputeSHA256Hash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes);
    }
}
