using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using ProtoBuf;

namespace VenstarTranslator.Models.Protobuf
{

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

        public byte[] Serialize()
        {
            using (var stream = new System.IO.MemoryStream())
            {
                Serializer.Serialize(stream, this);
                return stream.ToArray();
            }
        }

        // Convert byte array to hex string with spaces
        // useful to debug
        public string ToHexString()
        {
            return BitConverter.ToString(Serialize()).Replace("-", " ");
        }
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
        [RegularExpression("^[0-9a-f]{12}$", ErrorMessage = "Must be exactly 12 characters long and contain only 0-9 and a-f. Do not include dashes or colons in the MAC.")]
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

        public byte[] Serialize()
        {
            using (var stream = new System.IO.MemoryStream())
            {
                Serializer.Serialize(stream, this);
                return stream.ToArray();
            }
        }
    }

    [ProtoContract]
    public class SENSORNAME
    {
        [ProtoMember(8, IsRequired = true)]
        [MaxLength(14)]
        public string Name { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class WIFICONFIG
    {
        [ProtoMember(1, IsRequired = true)]
        public string SSID { get; set; } = string.Empty;

        [ProtoMember(2, IsRequired = true)]
        public byte SecurityType { get; set; } // 0 = none, 1 = WEP, 2 = WPA, 3 = WPA2

        [ProtoMember(3, IsRequired = true)]
        public byte DHCP { get; set; } // 0 = Static IP, 1 = DHCP, 2 = RFC3927 Link Local IP

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

        public void SetIPv4_Address(string addr)
        {
            IPv4_Address = IPv4ToUInt32(addr);
        }

        public void SetIPv4_Address(IPAddress addr)
        {
            IPv4_Address = IPAddressToUInt32(addr);
        }

        public IPAddress GetIPv4_Address()
        {
            if (IPv4_Address == null)
            {
                return null;
            }
            return UInt32ToIPAddress(IPv4_Address.Value);
        }

        public void SetMask(string addr)
        {
            Mask = IPv4ToUInt32(addr);
        }

        public void SetMask(IPAddress addr)
        {
            Mask = IPAddressToUInt32(addr);
        }

        public IPAddress GetMask()
        {
            if (Mask == null)
            {
                return null;
            }
            return UInt32ToIPAddress(Mask.Value);
        }

        public void SetGateway(string addr)
        {
            Gateway = IPv4ToUInt32(addr);
        }

        public void SetGateway(IPAddress addr)
        {
            Gateway = IPAddressToUInt32(addr);
        }

        public IPAddress GetGateway()
        {
            if (Gateway == null)
            {
                return null;
            }
            return UInt32ToIPAddress(Gateway.Value);
        }
        public void SetDnsAddress(string addr)
        {
            DnsAddress = IPv4ToUInt32(addr);
        }

        public void SetDnsAddress(IPAddress addr)
        {
            DnsAddress = IPAddressToUInt32(addr);
        }

        public IPAddress GetDnsAddress()
        {
            if (DnsAddress == null)
            {
                return null;
            }
            return UInt32ToIPAddress(DnsAddress.Value);
        }


        // Convert IPv4 string to uint
        public static uint IPv4ToUInt32(string ipv4Address)
        {
            if (!IPAddress.TryParse(ipv4Address, out var ip))
            {
                throw new FormatException("Invalid IP address string format.");
            }

            return IPAddressToUInt32(ip);
        }

        // Convert uint to IPv4 string
        public static string UInt32ToIPv4(uint ipAddress)
        {
            return new IPAddress(ipAddress).ToString();
        }

        // Convert IPAddress to uint
        public static uint IPAddressToUInt32(IPAddress ip)
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 addresses are supported.", nameof(ip));

            byte[] bytes = ip.GetAddressBytes();
            return ((uint)bytes[0] << 24) |
                   ((uint)bytes[1] << 16) |
                   ((uint)bytes[2] << 8) |
                   bytes[3];
        }

        // Convert uint to IPAddress
        public static IPAddress UInt32ToIPAddress(uint ipAddress)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)((ipAddress >> 24) & 0xFF);
            bytes[1] = (byte)((ipAddress >> 16) & 0xFF);
            bytes[2] = (byte)((ipAddress >> 8) & 0xFF);
            bytes[3] = (byte)(ipAddress & 0xFF);

            return new IPAddress(bytes);
        }
    }

    [ProtoContract]
    public class WIFISCANITEM
    {
        [ProtoMember(1, IsRequired = true)]
        public string SSID { get; set; } = string.Empty;

        [ProtoMember(2, IsRequired = true)]
        public byte SecurityType { get; set; } // 0 = none, 1 = WEP, 2 = WPA, 3 = WPA2

        [ProtoMember(3, IsRequired = true)]
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

        [ProtoMember(1, IsRequired = true)]
        public ushort Sequence { get; set; }

        [ProtoMember(2, IsRequired = true)]
        public FirmwareType Type { get; set; }

        [ProtoMember(3, IsRequired = true)]
        public byte[] Data { get; set; } = new byte[0];
    }

    [ProtoContract]
    public class FIRMWARECOMPLETE
    {
        [ProtoMember(1, IsRequired = true)]
        public ushort Sequence { get; set; }

        [ProtoMember(2, IsRequired = true)]
        public uint ModuleChecksum { get; set; }

        [ProtoMember(3)]
        public byte[] ServicePackSignature { get; set; }
    }

}