namespace VenstarTranslator.Services;

public interface IUdpBroadcaster
{
    void Broadcast(byte[] data);
}
