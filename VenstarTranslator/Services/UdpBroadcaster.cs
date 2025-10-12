using System.Net.Sockets;

namespace VenstarTranslator.Services;

public class UdpBroadcaster : IUdpBroadcaster
{
    public void Broadcast(byte[] data)
    {
        using UdpClient udpClient = new() { EnableBroadcast = true };
        udpClient.Connect("255.255.255.255", 5001);
        for (int i = 0; i < 5; i++)
        {
            udpClient.Send(data);
        }
    }
}
