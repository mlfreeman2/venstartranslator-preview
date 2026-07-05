using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Services;

public interface ISensorOperations
{
    string GetDocument(TranslatedVenstarSensor sensor);
    double GetLatestReading(TranslatedVenstarSensor sensor);
    void SendDataPacket(TranslatedVenstarSensor sensor);
    void SendPairingPacket(TranslatedVenstarSensor sensor);
    void ResendLastPacket(TranslatedVenstarSensor sensor);
}
