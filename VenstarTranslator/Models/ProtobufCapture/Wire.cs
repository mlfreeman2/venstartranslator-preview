using System.Diagnostics.CodeAnalysis;

using ProtoBuf;

namespace VenstarTranslator.Models.ProtobufCapture.Wire;

// Decode-only mirror of Models/Protobuf/ProtobufNetModel.cs.
//
// Why a separate model instead of reusing the emit model:
// the emit model initializes Battery = 100 (and Fw/Model/Power), so a packet WITHOUT a
// Battery field would deserialize as battery-full — "absent" becomes indistinguishable
// from a real value. A diagnostic must never invent readings. Adding *Specified /
// ShouldSerialize* members to the shared model is not an option either: protobuf-net
// consults those when serializing too, which would silently strip fields from outbound
// packets and break emitter parity.
//
// So every member here is nullable with NO initializers and NO IsRequired. protobuf-net
// maps absent fields to null on nullable members, so null == "not on the wire" — the C#
// equivalent of the HACS listener's HasField discipline. The [ProtoMember] numbers and
// class names mirror the emit model exactly so the same bytes decode here.

[ExcludeFromCodeCoverage]
[ProtoContract]
public class SensorMessage
{
    public enum Commands
    {
        SETSENSORNAME = 41,
        SENSORDATA = 42,
        SENSORPAIR = 43,
        WIFICONFIG = 44,
        WIFISCANRESULTS = 45,
        FIRMWARECHUNK = 46,
        FIRMWARECOMPLETE = 47,
        SUCCESS = 126,
        FAILURE = 127
    }

    [ProtoMember(1)]
    public Commands? Command { get; set; }

    [ProtoMember(41)]
    public SENSORNAME SensorName { get; set; }

    [ProtoMember(42)]
    public SENSORDATA SensorData { get; set; }

    [ProtoMember(44)]
    public WIFICONFIG WifiConfig { get; set; }

    [ProtoMember(45)]
    public WIFISCANRESULTS WifiScanResults { get; set; }

    [ProtoMember(46)]
    public FIRMWARECHUNK FirmwareChunk { get; set; }

    [ProtoMember(47)]
    public FIRMWARECOMPLETE FirmwareComplete { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class SENSORDATA
{
    [ProtoMember(1)]
    public INFO Info { get; set; }

    [ProtoMember(2)]
    public string Signature { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class INFO
{
    public enum SensorType
    {
        OUTDOOR = 1,
        RETURN = 2,
        REMOTE = 3,
        SUPPLY = 4
    }

    public enum PowerSource
    {
        BATTERY = 1,
        WIRED = 2
    }

    public enum SensorModel
    {
        TEMPSENSOR = 1
    }

    [ProtoMember(1)]
    public ushort? Sequence { get; set; }

    [ProtoMember(2)]
    public byte? SensorId { get; set; }

    [ProtoMember(3)]
    public string Mac { get; set; }

    [ProtoMember(4)]
    public byte? FwMajor { get; set; }

    [ProtoMember(5)]
    public byte? FwMinor { get; set; }

    [ProtoMember(6)]
    public SensorModel? Model { get; set; }

    [ProtoMember(7)]
    public PowerSource? Power { get; set; }

    [ProtoMember(8)]
    public string Name { get; set; }

    [ProtoMember(9)]
    public SensorType? Type { get; set; }

    [ProtoMember(10)]
    public byte? Temperature { get; set; }

    [ProtoMember(11)]
    public byte? Battery { get; set; }

    [ProtoMember(12)]
    public byte? Humidity { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class SENSORNAME
{
    [ProtoMember(8)]
    public string Name { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class WIFICONFIG
{
    [ProtoMember(1)]
    public string SSID { get; set; }

    [ProtoMember(2)]
    public byte? SecurityType { get; set; } // 0 = none, 1 = WEP, 2 = WPA, 3 = WPA2

    [ProtoMember(3)]
    public byte? DHCP { get; set; } // 0 = Static IP, 1 = DHCP, 2 = RFC3927 Link Local IP

    [ProtoMember(4)]
    public string Password { get; set; }

    [ProtoMember(5)]
    public uint? IPv4_Address { get; set; }

    [ProtoMember(6)]
    public uint? Mask { get; set; }

    [ProtoMember(7)]
    public uint? Gateway { get; set; }

    [ProtoMember(8)]
    public uint? DnsAddress { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class WIFISCANITEM
{
    [ProtoMember(1)]
    public string SSID { get; set; }

    [ProtoMember(2)]
    public byte? SecurityType { get; set; } // 0 = none, 1 = WEP, 2 = WPA, 3 = WPA2

    [ProtoMember(3)]
    public byte? SignalStrength { get; set; } // Signal strength % (0-100)
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class WIFISCANRESULTS
{
    [ProtoMember(1)]
    public WIFISCANITEM[] WifiScanResults { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class FIRMWARECHUNK
{
    public enum FirmwareType
    {
        MODULE = 2,
        SERVICEPACK = 3
    }

    [ProtoMember(1)]
    public ushort? Sequence { get; set; }

    [ProtoMember(2)]
    public FirmwareType? Type { get; set; }

    [ProtoMember(3)]
    public byte[] Data { get; set; }
}

[ExcludeFromCodeCoverage]
[ProtoContract]
public class FIRMWARECOMPLETE
{
    [ProtoMember(1)]
    public ushort? Sequence { get; set; }

    [ProtoMember(2)]
    public uint? ModuleChecksum { get; set; }

    [ProtoMember(3)]
    public byte[] ServicePackSignature { get; set; }
}
