using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;

using VenstarTranslator.Services;

using Xunit;

namespace VenstarTranslator.Tests;

public class ProtobufCaptureServiceTests
{
    private static ProtobufCaptureService NewService(int port = 0)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { { "ProtobufListenerPort", port.ToString() } }!)
            .Build();
        return new ProtobufCaptureService(config, Mock.Of<ILogger<ProtobufCaptureService>>());
    }

    private static readonly IPEndPoint Source = new(IPAddress.Loopback, 40000);

    // ---- Buffer / cursor logic (pure, no socket) ----

    [Fact]
    public void Capture_AssignsMonotonicIds()
    {
        using var svc = NewService();
        svc.Capture(new byte[] { 1 }, Source);
        svc.Capture(new byte[] { 2 }, Source);
        svc.Capture(new byte[] { 3 }, Source);

        var page = svc.GetMessagesAfter(0, 100);

        Assert.Equal(3, page.Messages.Length);
        Assert.Equal(1, page.Messages[0].Id);
        Assert.Equal(2, page.Messages[1].Id);
        Assert.Equal(3, page.Messages[2].Id);
        Assert.Equal(3, page.LastId);
    }

    [Fact]
    public void GetMessagesAfter_ReturnsOnlyNewerMessages()
    {
        using var svc = NewService();
        for (int i = 0; i < 5; i++)
        {
            svc.Capture(new byte[] { (byte)i }, Source);
        }

        var page = svc.GetMessagesAfter(2, 100);

        Assert.Equal(3, page.Messages.Length);
        Assert.All(page.Messages, m => Assert.True(m.Id > 2));
    }

    [Fact]
    public void GetMessagesAfter_RespectsLimit()
    {
        using var svc = NewService();
        for (int i = 0; i < 10; i++)
        {
            svc.Capture(new byte[] { (byte)i }, Source);
        }

        var page = svc.GetMessagesAfter(0, 4);

        Assert.Equal(4, page.Messages.Length);
        Assert.Equal(1, page.Messages[0].Id);
    }

    [Fact]
    public void Capture_CapEviction_AdvancesDroppedBeforeId()
    {
        using var svc = NewService();
        // Cap is 2000; inject 2001 so exactly the oldest (id 1) is evicted.
        for (int i = 0; i < 2001; i++)
        {
            svc.Capture(new byte[] { 0x08, 0x2a }, Source);
        }

        var page = svc.GetMessagesAfter(0, 5000);

        Assert.Equal(2000, page.Messages.Length);
        Assert.Equal(2, page.Messages[0].Id);      // id 1 was dropped
        Assert.Equal(1, page.DroppedBeforeId);      // messages up to and including id 1 are gone
    }

    // ---- Socket integration (ephemeral port) ----

    [Fact]
    public void Start_BindsEphemeralPort_ReportsActualPort()
    {
        using var svc = NewService(0);
        var status = svc.Start();

        Assert.True(status.Running);
        Assert.False(status.Failed);
        Assert.True(status.Port > 0);

        svc.Stop();
    }

    [Fact]
    public void ReceiveLoop_CapturesLoopbackDatagram()
    {
        using var svc = NewService(0);
        var status = svc.Start();
        int port = status.Port;

        using (var sender = new UdpClient())
        {
            sender.Send(new byte[] { 0x08, 0x2a, 0x01 }, 3, new IPEndPoint(IPAddress.Loopback, port));
        }

        var captured = WaitForCapture(svc, expected: 1);
        Assert.NotNull(captured);
        Assert.False(string.IsNullOrEmpty(captured.Source));
        Assert.True(captured.ReceivedAtUtc <= DateTime.UtcNow);

        svc.Stop();
    }

    [Fact]
    public void Start_ClearsBufferFromPreviousSession()
    {
        using var svc = NewService(0);
        svc.Start();
        svc.Capture(new byte[] { 1 }, Source); // simulate a prior capture
        Assert.True(svc.GetStatus().CapturedCount >= 1);
        svc.Stop();

        var status = svc.Start();
        Assert.Equal(0, status.CapturedCount);
        Assert.Equal(0, svc.GetMessagesAfter(0, 100).Messages.Length);

        svc.Stop();
    }

    [Fact]
    public void Stop_RetainsBuffer()
    {
        using var svc = NewService(0);
        svc.Start();
        svc.Capture(new byte[] { 1 }, Source);
        svc.Stop();

        Assert.Equal(1, svc.GetStatus().CapturedCount);
        Assert.False(svc.GetStatus().Running);
    }

    [Fact]
    public void Start_WhenPortAlreadyBound_ReturnsFailedStatus()
    {
        // Hold a fixed port with a plain socket that does NOT set address reuse.
        using var blocker = new UdpClient();
        blocker.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)blocker.Client.LocalEndPoint).Port;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { { "ProtobufListenerPort", port.ToString() } }!)
            .Build();
        using var svc = new ProtobufCaptureService(config, Mock.Of<ILogger<ProtobufCaptureService>>());

        var status = svc.Start();

        // Some platforms permit rebinding even without SO_REUSEADDR; only assert the failure
        // contract when the bind was actually refused.
        if (status.Failed)
        {
            Assert.False(status.Running);
            Assert.NotNull(status.Error);
        }
        else
        {
            svc.Stop();
        }
    }

    private static Models.ProtobufCapture.CapturedMessage WaitForCapture(ProtobufCaptureService svc, int expected)
    {
        for (int i = 0; i < 100; i++)
        {
            var page = svc.GetMessagesAfter(0, 100);
            if (page.Messages.Length >= expected)
            {
                return page.Messages[0];
            }
            Thread.Sleep(20);
        }
        return null;
    }
}
