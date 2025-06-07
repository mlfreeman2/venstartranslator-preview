using ProtoBuf;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Sensor
{

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

        [ProtoMember(1, IsRequired = true)]
        public ushort Sequence { get; set; }

        [ProtoMember(2, IsRequired = true)]
        public byte SensorId { get; set; }

        [ProtoMember(3, IsRequired = true)]
        public string Mac { get; set; } = string.Empty;

        [ProtoMember(4, IsRequired = true)]
        public byte FwMajor { get; set; } = 4;

        [ProtoMember(5, IsRequired = true)]
        public byte FwMinor { get; set; } = 2;

        [ProtoMember(6, IsRequired = true)]
        public SensorModel Model { get; set; } = SensorModel.TEMPSENSOR;

        [ProtoMember(7, IsRequired = true)]
        public PowerSource Power { get; set; } = PowerSource.BATTERY;

        [MaxLength(14)]
        [ProtoMember(8)]
        public string Name { get; set; }

        [ProtoMember(9)]
        public SensorType Type { get; set; }

        [ProtoMember(10)]
        public byte Temperature { get; set; }

        [ProtoMember(11)]
        public byte Battery { get; set; } = 100;

        [ProtoMember(12)]
        public byte Humidity { get; set; }
    }

    [ProtoContract]
    public class SENSORNAME
    {
        [ProtoMember(8)]
        public string Name { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class SENSORDATA
    {
        [ProtoMember(1)]
        public INFO Info { get; set; } = new INFO();

        [ProtoMember(2)]
        public string Signature { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class WIFICONFIG
    {
        [ProtoMember(1)]
        public string SSID { get; set; } = string.Empty;

        [ProtoMember(2)]
        public byte SecurityType { get; set; } // 0 = none, 1 = WEP, 2 = WPA, 3 = WPA2

        [ProtoMember(3)]
        public byte DHCP { get; set; } // 0 = Static IP, 1 = DHCP, 2 = RFC3927 Link Local IP

        [ProtoMember(4)]
        public string? Password { get; set; }

        [ProtoMember(5)]
        public uint? IPv4Address { get; set; }

        [ProtoMember(6)]
        public uint? Mask { get; set; }

        [ProtoMember(7)]
        public uint? Gateway { get; set; }

        [ProtoMember(8)]
        public uint? DnsAddress { get; set; }
    }

    [ProtoContract]
    public class WIFISCANITEM
    {
        [ProtoMember(1)]
        public string SSID { get; set; } = string.Empty;

        [ProtoMember(2)]
        public byte SecurityType { get; set; } // 0 = none, 1 = WEP, 2 = WPA, 3 = WPA2

        [ProtoMember(3)]
        public byte SignalStrength { get; set; } // Signal strength % (0-100)
    }

    [ProtoContract]
    public class WIFISCANRESULTS
    {
        [ProtoMember(1)]
        public WIFISCANITEM[] WifiScanResults { get; set; } = new WIFISCANITEM[0];
    }

    [ProtoContract]
    public class FIRMWARECHUNK
    {
        public enum FirmwareType
        {
            MODULE = 2,
            SERVICEPACK = 3
        }

        [ProtoMember(1)]
        public ushort Sequence { get; set; }

        [ProtoMember(2)]
        public FirmwareType Type { get; set; }

        [ProtoMember(3)]
        public byte[] Data { get; set; } = new byte[0];
    }

    [ProtoContract]
    public class FIRMWARECOMPLETE
    {
        [ProtoMember(1)]
        public ushort Sequence { get; set; }

        [ProtoMember(2)]
        public uint ModuleChecksum { get; set; }

        [ProtoMember(3)]
        public byte[]? ServicePackSignature { get; set; }
    }

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
        public Commands Command { get; set; }

        [ProtoMember(41)]
        public SENSORNAME? SensorName { get; set; }

        [ProtoMember(42)]
        public SENSORDATA? SensorData { get; set; }

        [ProtoMember(44)]
        public WIFICONFIG? WifiConfig { get; set; }

        [ProtoMember(45)]
        public WIFISCANRESULTS? WifiScanResults { get; set; }

        [ProtoMember(46)]
        public FIRMWARECHUNK? FirmwareChunk { get; set; }

        [ProtoMember(47)]
        public FIRMWARECOMPLETE? FirmwareComplete { get; set; }
    }

    // Helper class for working with IP addresses
    public static class IPHelper
    {
        public static uint PackIPAddress(byte a, byte b, byte c, byte d)
        {
            return (uint)((a << 24) | (b << 16) | (c << 8) | d);
        }

        public static (byte a, byte b, byte c, byte d) UnpackIPAddress(uint packed)
        {
            return (
                (byte)((packed >> 24) & 0xFF),
                (byte)((packed >> 16) & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)(packed & 0xFF)
            );
        }
    }

    // Example usage and serialization helpers
    public static class SensorMessageSerializer
    {
        public static byte[] Serialize<T>(T obj) where T : class
        {
            using (var stream = new System.IO.MemoryStream())
            {
                Serializer.Serialize(stream, obj);
                return stream.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] data) where T : class
        {
            using (var stream = new System.IO.MemoryStream(data))
            {
                return Serializer.Deserialize<T>(stream);
            }
        }

        // Convert byte array to hex string with spaces
        public static string ToHexString(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        // Alternative hex conversion methods
        public static string ToHexStringLower(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("x2")));
        }

        public static string ToHexStringUpper(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        // More efficient version for large arrays
        public static string ToHexStringFast(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        // Create a sensor data message
        public static SensorMessage CreateSensorDataMessage(INFO sensorInfo, string signature)
        {
            return new SensorMessage
            {
                Command = SensorMessage.Commands.SENSORDATA,
                SensorData = new SENSORDATA
                {
                    Info = sensorInfo,
                    Signature = signature
                }
            };
        }

        // Create a WiFi config message
        public static SensorMessage CreateWifiConfigMessage(string ssid, byte securityType, 
            byte dhcp, string? password = null)
        {
            return new SensorMessage
            {
                Command = SensorMessage.Commands.WIFICONFIG,
                WifiConfig = new WIFICONFIG
                {
                    SSID = ssid,
                    SecurityType = securityType,
                    DHCP = dhcp,
                    Password = password
                }
            };
        }

        // Create a sensor name setting message
        public static SensorMessage CreateSetSensorNameMessage(string name)
        {
            return new SensorMessage
            {
                Command = SensorMessage.Commands.SETSENSORNAME,
                SensorName = new SENSORNAME
                {
                    Name = name
                }
            };
        }

        // Print serialized data as hex
        public static void PrintAsHex<T>(T obj, string? label = null) where T : class
        {
            byte[] data = Serialize(obj);
            string hex = ToHexString(data);
            
            if (!string.IsNullOrEmpty(label))
                Console.WriteLine($"{label}:");
            
            Console.WriteLine($"Length: {data.Length} bytes");
            Console.WriteLine($"Hex: {hex}");
        }
    }
}