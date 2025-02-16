using System;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using Models.Protobuf;
using System.Security.Cryptography;
using Google.Protobuf;
using VenstarTranslator.DB;
using Hangfire;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace VenstarTranslator
{
    // TODO: Coravel instead of Hangfire?
    public class Tasks
    {
        // look the temperature up here and send that array index in the protobuf packet
        static string[] Temperatures_Farenheit = ["-40", "-39", "-38", "-37", "-36", "-36", "-35", "-34", "-33", "-32", "-31", "-30", "-29", "-28", "-27", "-27", "-26", "-25", "-24", "-23", "-22", "-21", "-20", "-19", "-18", "-18", "-17", "-16", "-15", "-14", "-13", "-12", "-11", "-10", "-9", "-9", "-8", "-7", "-6", "-5", "-4", "-3", "-2", "-1", "0", "1", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "28", "29", "30", "31", "32", "33", "34", "35", "36", "37", "37", "38", "39", "40", "41", "42", "43", "44", "45", "46", "46", "47", "48", "49", "50", "51", "52", "53", "54", "55", "55", "56", "57", "58", "59", "60", "61", "62", "63", "64", "64", "65", "66", "67", "68", "69", "70", "71", "72", "73", "73", "74", "75", "76", "77", "78", "79", "80", "81", "82", "82", "83", "84", "85", "86", "87", "88", "89", "90", "91", "91", "92", "93", "94", "95", "96", "97", "98", "99", "100", "100", "101", "102", "103", "104", "105", "106", "107", "108", "109", "109", "110", "111", "112", "113", "114", "115", "116", "117", "118", "118", "119", "120", "121", "122", "123", "124", "125", "126", "127", "127", "128", "129", "130", "131", "132", "133", "134", "135", "136", "136", "137", "138", "139", "140", "141", "142", "143", "144", "145", "145", "146", "147", "148", "149", "150", "151", "152", "153", "154", "154", "155", "156", "157", "158", "159", "160", "161", "162", "163", "163", "164", "165", "166", "167", "168", "169", "170", "171", "172", "172", "173", "174", "175", "176", "177", "178", "179", "180", "181", "181", "182", "183", "184", "185", "186", "187", "188"];

        // look the temperature up here and send that array index in the protobuf packet
        static string[] Temperatures_Celsius = ["-40.0", "-39.5", "-39.0", "-38.5", "-38.0", "-37.5", "-37.0", "-36.5", "-36.0", "-35.5", "-35.0", "-34.5", "-34.0", "-33.5", "-33.0", "-32.5", "-32.0", "-31.5", "-31.0", "-30.5", "-30.0", "-29.5", "-29.0", "-28.5", "-28.0", "-27.5", "-27.0", "-26.5", "-26.0", "-25.5", "-25.0", "-24.5", "-24.0", "-23.5", "-23.0", "-22.5", "-22.0", "-21.5", "-21.0", "-20.5", "-20.0", "-19.5", "-19.0", "-18.5", "-18.0", "-17.5", "-17.0", "-16.5", "-16.0", "-15.5", "-15.0", "-14.5", "-14.0", "-13.5", "-13.0", "-12.5", "-12.0", "-11.5", "-11.0", "-10.5", "-10.0", "-9.5", "-9.0", "-8.5", "-8.0", "-7.5", "-7.0", "-6.5", "-6.0", "-5.5", "-5.0", "-4.5", "-4.0", "-3.5", "-3.0", "-2.5", "-2.0", "-1.5", "-1.0", "-0.5", "0.0", "0.5", "1.0", "1.5", "2.0", "2.5", "3.0", "3.5", "4.0", "4.5", "5.0", "5.5", "6.0", "6.5", "7.0", "7.5", "8.0", "8.5", "9.0", "9.5", "10.0", "10.5", "11.0", "11.5", "12.0", "12.5", "13.0", "13.5", "14.0", "14.5", "15.0", "15.5", "16.0", "16.5", "17.0", "17.5", "18.0", "18.5", "19.0", "19.5", "20.0", "20.5", "21.0", "21.5", "22.0", "22.5", "23.0", "23.5", "24.0", "24.5", "25.0", "25.5", "26.0", "26.5", "27.0", "27.5", "28.0", "28.5", "29.0", "29.5", "30.0", "30.5", "31.0", "31.5", "32.0", "32.5", "33.0", "33.5", "34.0", "34.5", "35.0", "35.5", "36.0", "36.5", "37.0", "37.5", "38.0", "38.5", "39.0", "39.5", "40.0", "40.5", "41.0", "41.5", "42.0", "42.5", "43.0", "43.5", "44.0", "44.5", "45.0", "45.5", "46.0", "46.5", "47.0", "47.5", "48.0", "48.5", "49.0", "49.5", "50.0", "50.5", "51.0", "51.5", "52.0", "52.5", "53.0", "53.5", "54.0", "54.5", "55.0", "55.5", "56.0", "56.5", "57.0", "57.5", "58.0", "58.5", "59.0", "59.5", "60.0", "60.5", "61.0", "61.5", "62.0", "62.5", "63.0", "63.5", "64.0", "64.5", "65.0", "65.5", "66.0", "66.5", "67.0", "67.5", "68.0", "68.5", "69.0", "69.5", "70.0", "70.5", "71.0", "71.5", "72.0", "72.5", "73.0", "73.5", "74.0", "74.5", "75.0", "75.5", "76.0", "76.5", "77.0", "77.5", "78.0", "78.5", "79.0", "79.5", "80.0", "80.5", "81.0", "81.5", "82.0", "82.5", "83.0", "83.5", "84.0", "84.5", "85.0", "85.5", "86.0", "86.5"];

        private IServiceProvider _serviceProvider;

        private IConfiguration _config;

        public Tasks(IServiceProvider sp, IConfiguration config)
        {
            _serviceProvider = sp;
            _config = config;
        }

        [JobDisplayName("Send a Venstar data packet for sensor #{0}")]
        [AutomaticRetry(Attempts = 0)]
        public void SendPacket(uint sensorID)
        {
            using (IServiceScope scope = _serviceProvider.CreateScope())
            using (var dbContext = scope.ServiceProvider.GetRequiredService<VenstarTranslatorDataCache>())
            {
                var sensorInfo = dbContext.Sensors.Include(a => a.Headers).Single(a => a.SensorID == sensorID);

                var dataPacket = new SensorMessage {
                    Command = SensorMessage.Types.Commands.Sensordata,
                    Sensordata = new SENSORDATA {
                        Info = new INFO {
                            Sequence = sensorInfo.Sequence,
                            SensorId = sensorInfo.SensorID,
                            Mac = sensorInfo.MacAddress,
                            FwMajor = 4,
                            FwMinor = 2,
                            Model = INFO.Types.SensorModel.Tempsensor,
                            Battery = 100,
                            Power = INFO.Types.PowerSource.Battery,
                            Type = TranslateType(sensorInfo),
                            Name = sensorInfo.Name,
                            Temperature = ConvertTemperatureToIndex(GetLatestReading(sensorInfo), sensorInfo.Scale)
                        }
                    }
                };

                using (HMACSHA256 hmac = new(Convert.FromBase64String(sensorInfo.Signature_Key)))
                using (MemoryStream ms = new())
                {
                    dataPacket.Sensordata.Info.WriteTo(ms);
                    dataPacket.Sensordata.Signature = Convert.ToBase64String(hmac.ComputeHash(ms.ToArray()));
                }

                sensorInfo.Sequence += 1;
                if (sensorInfo.Sequence > 65000)
                {
                    sensorInfo.Sequence = 1;
                }
                dbContext.SaveChanges();

                UdpBroadcast(dataPacket);
            }
        }

        public static double GetLatestReading(TranslatedVenstarSensor sensor)
        {
            using (var clientHandler = new HttpClientHandler())
            {
                if (sensor.IgnoreSSLErrors)
                {
                    clientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                using (var client = new HttpClient(clientHandler))
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, sensor.URL);
                    foreach (var header in sensor.Headers)
                    {
                        request.Headers.Add(header.Name, header.Value);
                    }

                    var response = client.SendAsync(request).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    var target = "";
                    // if we don't have a JSON path expression, just look at the whole response body for the first number
                    if (!string.IsNullOrWhiteSpace(sensor.JSONPath))
                    {
                        var jToken = JToken.Parse(responseBody);
                        var field = jToken.SelectToken(sensor.JSONPath).Value<string>();
                        // extract a positive or negative (brrr) number from the field regardless of other crap in it too
                        target = Regex.Match(field, @"(-?\d+(.\d+)?)").Value;
                    }
                    else
                    {
                        // try to extract a positive or negative (brrr) number from the field regardless of other crap in it too
                        // maybe we can support non-json responses this way
                        target = Regex.Match(responseBody, @"(-?\d+(.\d+)?)").Value;
                    }

                    return Convert.ToDouble(target);
                }
            }
        }

        public static uint ConvertTemperatureToIndex(double temperature, TemperatureScale scale)
        {
            switch (scale)
            {
                case TemperatureScale.F:
                    return Convert.ToUInt32(Array.IndexOf(Temperatures_Farenheit, "" + Math.Round(Convert.ToDecimal(temperature))));
                case TemperatureScale.C:
                    return Convert.ToUInt32(Array.IndexOf(Temperatures_Celsius, "" + Math.Round(Convert.ToDecimal(temperature))));
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void UdpBroadcast(IMessage protobufPacket)
        {
            UdpClient udpClient = new() {
                EnableBroadcast = true
            };
            udpClient.Connect("255.255.255.255", 5001);
            using (var ms = new MemoryStream())
            {
                protobufPacket.WriteTo(ms);
                udpClient.Send(ms.ToArray());
                udpClient.Send(ms.ToArray());
                udpClient.Send(ms.ToArray());
                udpClient.Send(ms.ToArray());
                udpClient.Send(ms.ToArray());
            }
        }

        public static INFO.Types.SensorType TranslateType(TranslatedVenstarSensor sensor)
        {
            switch (sensor.Purpose)
            {
                case SensorPurpose.Outdoor:
                    return INFO.Types.SensorType.Outdoor;
                case SensorPurpose.Remote:
                    return INFO.Types.SensorType.Remote;
                case SensorPurpose.Return:
                    return INFO.Types.SensorType.Return;
                case SensorPurpose.Supply:
                    return INFO.Types.SensorType.Supply;
                default:
                    throw new Exception();
            }
        }

    }
}