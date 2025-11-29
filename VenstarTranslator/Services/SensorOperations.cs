using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using VenstarTranslator.Models;
using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Services;

public class SensorOperations : ISensorOperations
{
    private readonly IHttpDocumentFetcher _documentFetcher;
    private readonly IUdpBroadcaster _udpBroadcaster;

    public SensorOperations(IHttpDocumentFetcher documentFetcher, IUdpBroadcaster udpBroadcaster)
    {
        _documentFetcher = documentFetcher;
        _udpBroadcaster = udpBroadcaster;
    }

    public string GetDocument(TranslatedVenstarSensor sensor)
    {
        return _documentFetcher.FetchDocument(sensor.URL, sensor.IgnoreSSLErrors, sensor.Headers);
    }

    public double GetLatestReading(TranslatedVenstarSensor sensor)
    {
        var document = GetDocument(sensor);
        return sensor.ExtractValue(document);
    }

    public void SendDataPacket(TranslatedVenstarSensor sensor)
    {
        var latestReading = GetLatestReading(sensor);
        var bytes = sensor.BuildDataPacket(latestReading);
        sensor.LastPacketBytes = bytes; // Cache for potential resend
        _udpBroadcaster.Broadcast(bytes);
    }

    public void SendPairingPacket(TranslatedVenstarSensor sensor)
    {
        var latestReading = GetLatestReading(sensor);
        var bytes = sensor.BuildPairingPacket(latestReading);
        _udpBroadcaster.Broadcast(bytes);
    }

    public void ResendLastPacket(TranslatedVenstarSensor sensor)
    {
        if (sensor.LastPacketBytes == null || sensor.LastPacketBytes.Length == 0)
        {
            throw new InvalidOperationException("No packet available to resend. The sensor has not broadcast any data packets yet.");
        }
        _udpBroadcaster.Broadcast(sensor.LastPacketBytes);
    }

    [ExcludeFromCodeCoverage]
    public static void SyncToJsonFile(IConfiguration config, VenstarTranslatorDataCache dbContext)
    {
        var sensorFilePath = config.GetValue<string>("SensorFilePath");
        var sensors = dbContext.Sensors.Include(a => a.Headers).AsNoTracking().ToList();
        var sensorDTOs = sensors.Select(s => SensorJsonDTO.FromSensor(s)).ToList();
        var json = JsonConvert.SerializeObject(sensorDTOs, Formatting.Indented, new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore
        });
        File.WriteAllText(sensorFilePath, json);
    }
}
