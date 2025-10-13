using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

public interface ISensorOperations
{
    string GetDocument(TranslatedVenstarSensor sensor);
    double GetLatestReading(TranslatedVenstarSensor sensor);
    void SendDataPacket(TranslatedVenstarSensor sensor);
    void SendPairingPacket(TranslatedVenstarSensor sensor);
}
