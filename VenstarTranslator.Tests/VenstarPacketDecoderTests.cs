using System;
using System.IO;

using Newtonsoft.Json.Linq;

using ProtoBuf;

using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Enums;
using VenstarTranslator.Services;

using Emit = VenstarTranslator.Models.Protobuf;
using Wire = VenstarTranslator.Models.ProtobufCapture.Wire;

using Xunit;

namespace VenstarTranslator.Tests;

public class VenstarPacketDecoderTests
{
    // ---- Decoder parity: the emitter's own packets must round-trip through the decoder ----

    [Theory]
    [InlineData(TemperatureScale.F, 72.0, SensorPurpose.Remote)]
    [InlineData(TemperatureScale.F, -40.0, SensorPurpose.Outdoor)]
    [InlineData(TemperatureScale.F, 188.0, SensorPurpose.Return)]
    [InlineData(TemperatureScale.C, 22.0, SensorPurpose.Supply)]
    [InlineData(TemperatureScale.C, -10.5, SensorPurpose.Remote)]
    [InlineData(TemperatureScale.C, 86.5, SensorPurpose.Outdoor)]
    public void Decode_DataPacket_RoundTripsProjectedFields(TemperatureScale scale, double temp, SensorPurpose purpose)
    {
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = BuildSensor(scale, purpose);
        var bytes = sensor.BuildDataPacket(temp);

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.True(decoded.Decoded);
        Assert.Equal("SENSORDATA", decoded.Command);
        Assert.NotNull(decoded.Summary);
        Assert.Equal(sensor.MacAddress, decoded.Summary.Mac);
        Assert.Equal(sensor.SensorID, decoded.Summary.SensorId);
        Assert.Equal("Round Trip", decoded.Summary.Name);
        Assert.Equal(purpose.ToString().ToUpperInvariant(), decoded.Summary.Purpose);

        // index -> C/F must reverse the emitter's forward mapping (within 0.5 C resolution).
        if (decoded.Summary.Fault == null)
        {
            double expectedC = decoded.Summary.TemperatureIndex / 2.0 - 40.0;
            Assert.Equal(expectedC, decoded.Summary.Celsius.Value, 3);
            Assert.Equal(expectedC * 9.0 / 5.0 + 32.0, decoded.Summary.Fahrenheit.Value, 3);
        }
    }

    [Fact]
    public void Decode_PairingPacket_IsRecognizedAndProjected()
    {
        TranslatedVenstarSensor.macPrefix = "428e0486d8";
        var sensor = BuildSensor(TemperatureScale.F, SensorPurpose.Remote);
        var bytes = sensor.BuildPairingPacket(68.0);

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.True(decoded.Decoded);
        Assert.Equal("SENSORPAIR", decoded.Command);
        Assert.NotNull(decoded.Summary);
        Assert.Equal((ushort)1, decoded.Summary.Sequence);
    }

    // ---- Golden fixtures (shared across the three repos) ----

    [Fact]
    public void Decode_GoldenFixtures_RecoverExpectedValues()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "csharp_golden_packets.json");
        var doc = JObject.Parse(File.ReadAllText(path));

        foreach (var packet in doc["packets"])
        {
            var hex = packet["hex"].Value<string>();
            var expected = packet["expected"];
            var bytes = Convert.FromHexString(hex);

            var decoded = VenstarPacketDecoder.Decode(bytes);

            Assert.True(decoded.Decoded, $"Fixture failed to decode: {packet["description"]}");
            Assert.Equal(packet["command"].Value<string>(), decoded.Command);
            Assert.NotNull(decoded.Summary);

            var s = decoded.Summary;
            Assert.Equal(expected["mac"].Value<string>(), s.Mac);
            Assert.Equal(expected["sensor_id"].Value<byte>(), s.SensorId);
            Assert.Equal(expected["name"].Value<string>(), s.Name);
            // Fixture "purpose" is the SensorPurpose name; summary carries the INFO.SensorType name.
            Assert.Equal(expected["purpose"].Value<string>(), s.Purpose, ignoreCase: true);
            Assert.Equal(expected["sequence"].Value<ushort>(), s.Sequence);
            Assert.Equal(expected["temp_index"].Value<byte>(), s.TemperatureIndex);

            // temperature_field_present maps to the wire model's Temperature != null.
            Assert.Equal(expected["temperature_field_present"].Value<bool>(), s.TemperaturePresent);
            Assert.Equal(expected["temperature_field_present"].Value<bool>(), decoded.Body.SensorData.Info.Temperature.HasValue);

            Assert.Equal(expected["temp_c"].Value<double>(), s.Celsius.Value, 3);
            Assert.Equal(expected["battery"].Value<byte>(), s.Battery.Value);

            // humidity is never sent by the C# app -> must decode absent, never 0.
            Assert.Null(s.Humidity);
        }
    }

    // ---- Presence honesty: absent scalars must be null, never the emit-model defaults ----

    [Fact]
    public void Decode_InfoWithoutOptionalScalars_DecodesToNullNotDefaults()
    {
        // A hand-built INFO with only the gate-required fields — no Battery, Humidity, or Type.
        var wire = new Wire.SensorMessage
        {
            Command = Wire.SensorMessage.Commands.SENSORDATA,
            SensorData = new Wire.SENSORDATA
            {
                Info = new Wire.INFO
                {
                    Sequence = 5,
                    SensorId = 1,
                    Mac = "428e0486d801",
                },
            },
        };
        var bytes = SerializeWire(wire);

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.True(decoded.Decoded);
        Assert.Null(decoded.Body.SensorData.Info.Battery);   // never 100 (emit-model trap)
        Assert.Null(decoded.Body.SensorData.Info.Humidity);  // never 0
        Assert.Null(decoded.Body.SensorData.Info.Type);      // never OUTDOOR
        Assert.Null(decoded.Summary.Battery);
        Assert.Null(decoded.Summary.Humidity);
        Assert.Null(decoded.Summary.Purpose);
    }

    // ---- Validity gate ----

    [Fact]
    public void Decode_EmptyBuffer_IsNotDecodable()
    {
        var decoded = VenstarPacketDecoder.Decode(Array.Empty<byte>());
        Assert.False(decoded.Decoded);
        Assert.NotNull(decoded.DecodeError);
    }

    [Fact]
    public void Decode_DefinedCommandMissingBody_IsNotDecodable()
    {
        // SENSORDATA command with no SensorData body.
        var wire = new Wire.SensorMessage { Command = Wire.SensorMessage.Commands.SENSORDATA };
        var bytes = SerializeWire(wire);

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.False(decoded.Decoded);
        Assert.NotNull(decoded.DecodeError);
    }

    [Fact]
    public void Decode_UndefinedCommand_IsNotDecodable()
    {
        // Field 1 (Command) = varint 99, which is not a defined Commands value.
        var bytes = new byte[] { 0x08, 0x63 };
        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.False(decoded.Decoded);
    }

    [Fact]
    public void Decode_GarbageBytes_NotDecodableButHexPreserved()
    {
        var bytes = new byte[] { 0x4d, 0x2d, 0x53, 0x45, 0x41, 0x52, 0x43, 0x48 }; // "M-SEARCH"
        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.False(decoded.Decoded);
        Assert.Equal("4d2d534541524348", decoded.Hex);
        Assert.Equal(8, decoded.Length);
    }

    [Fact]
    public void Decode_SuccessCommand_IsDecodableWithoutBody()
    {
        var wire = new Wire.SensorMessage { Command = Wire.SensorMessage.Commands.SUCCESS };
        var bytes = SerializeWire(wire);

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.True(decoded.Decoded);
        Assert.Equal("SUCCESS", decoded.Command);
        Assert.Null(decoded.Summary); // body-less command has no sensor summary
    }

    // ---- Full-tree decode: build with the emit model, decode with the wire model ----

    [Fact]
    public void Decode_WifiScanResults_RoundTripsTree()
    {
        var emit = new Emit.SensorMessage
        {
            Command = Emit.SensorMessage.Commands.WIFISCANRESULTS,
            WifiScanResults = new Emit.WIFISCANRESULTS
            {
                WifiScanResults = new[]
                {
                    new Emit.WIFISCANITEM { SSID = "HomeNet", SecurityType = 3, SignalStrength = 72 },
                    new Emit.WIFISCANITEM { SSID = "IoT-2G", SecurityType = 2, SignalStrength = 55 },
                },
            },
        };
        var bytes = emit.Serialize();

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.True(decoded.Decoded);
        Assert.Equal("WIFISCANRESULTS", decoded.Command);
        Assert.Equal(2, decoded.Body.WifiScanResults.WifiScanResults.Length);
        Assert.Equal("HomeNet", decoded.Body.WifiScanResults.WifiScanResults[0].SSID);
        Assert.Equal((byte)72, decoded.Body.WifiScanResults.WifiScanResults[0].SignalStrength);
    }

    [Fact]
    public void Decode_FirmwareChunk_RoundTripsTree()
    {
        var emit = new Emit.SensorMessage
        {
            Command = Emit.SensorMessage.Commands.FIRMWARECHUNK,
            FirmwareChunk = new Emit.FIRMWARECHUNK
            {
                Sequence = 17,
                Type = Emit.FIRMWARECHUNK.FirmwareType.MODULE,
                Data = new byte[] { 0xde, 0xad, 0xbe, 0xef },
            },
        };
        var bytes = emit.Serialize();

        var decoded = VenstarPacketDecoder.Decode(bytes);

        Assert.True(decoded.Decoded);
        Assert.Equal("FIRMWARECHUNK", decoded.Command);
        Assert.Equal((ushort)17, decoded.Body.FirmwareChunk.Sequence);
        Assert.Equal(Wire.FIRMWARECHUNK.FirmwareType.MODULE, decoded.Body.FirmwareChunk.Type);
        Assert.Equal(new byte[] { 0xde, 0xad, 0xbe, 0xef }, decoded.Body.FirmwareChunk.Data);
    }

    // ---- Fault / edge decode ----

    [Fact]
    public void Decode_ShortedSensor_ReportsFault()
    {
        var decoded = DecodeWithTemperature(254);
        Assert.Equal("shorted", decoded.Summary.Fault);
        Assert.Null(decoded.Summary.Celsius);
        Assert.Null(decoded.Summary.Fahrenheit);
    }

    [Fact]
    public void Decode_OpenSensor_ReportsFault()
    {
        var decoded = DecodeWithTemperature(255);
        Assert.Equal("open", decoded.Summary.Fault);
        Assert.Null(decoded.Summary.Celsius);
    }

    [Fact]
    public void Decode_AbsentTemperature_IsValidMinus40NotError()
    {
        // Temperature omitted (index 0) is a valid -40.0 C reading, flagged not-present.
        var wire = new Wire.SensorMessage
        {
            Command = Wire.SensorMessage.Commands.SENSORDATA,
            SensorData = new Wire.SENSORDATA { Info = new Wire.INFO { Sequence = 1, SensorId = 0, Mac = "428e0486d800" } },
        };
        var decoded = VenstarPacketDecoder.Decode(SerializeWire(wire));

        Assert.True(decoded.Decoded);
        Assert.False(decoded.Summary.TemperaturePresent);
        Assert.Equal((byte)0, decoded.Summary.TemperatureIndex);
        Assert.Equal(-40.0, decoded.Summary.Celsius.Value, 3);
        Assert.Null(decoded.Summary.Fault);
    }

    // ---- helpers ----

    private static TranslatedVenstarSensor BuildSensor(TemperatureScale scale, SensorPurpose purpose)
    {
        return new TranslatedVenstarSensor
        {
            SensorID = 2,
            Name = "Round Trip",
            Enabled = true,
            Purpose = purpose,
            Scale = scale,
            URL = "http://example.com",
            JSONPath = "$.t",
            Headers = new System.Collections.Generic.List<DataSourceHttpHeader>(),
            Sequence = 41,
        };
    }

    private static Models.ProtobufCapture.CapturedMessage DecodeWithTemperature(byte index)
    {
        var wire = new Wire.SensorMessage
        {
            Command = Wire.SensorMessage.Commands.SENSORDATA,
            SensorData = new Wire.SENSORDATA
            {
                Info = new Wire.INFO { Sequence = 1, SensorId = 0, Mac = "428e0486d800", Temperature = index },
            },
        };
        return VenstarPacketDecoder.Decode(SerializeWire(wire));
    }

    private static byte[] SerializeWire(Wire.SensorMessage message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return stream.ToArray();
    }
}
