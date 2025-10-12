using VenstarTranslator.Models;

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
        _udpBroadcaster.Broadcast(bytes);
    }

    public void SendPairingPacket(TranslatedVenstarSensor sensor)
    {
        var latestReading = GetLatestReading(sensor);
        var bytes = sensor.BuildPairingPacket(latestReading);
        _udpBroadcaster.Broadcast(bytes);
    }
}
