using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using VenstarTranslator.Models.ProtobufCapture;

namespace VenstarTranslator.Services;

// Singleton that owns the UDP :5001 receive socket, the receive loop, and a bounded ring
// buffer of decoded datagrams (§4a). Nothing is persisted — this is a live diagnostic scope,
// not a log store. The socket only opens on Start and closes on Stop, so port 5001 is free
// when not diagnosing (important: the FakeMacPrefix trick lets users run multiple instances
// on one host, and a permanent bind would contend).
public class ProtobufCaptureService : IProtobufCaptureService, IDisposable
{
    private const int MaxBufferedMessages = 2000;

    private readonly ILogger<ProtobufCaptureService> _logger;
    private readonly int _configuredPort;

    private readonly object _lock = new();
    private readonly List<CapturedMessage> _buffer = new();

    private UdpClient _udp;
    private CancellationTokenSource _cts;
    private Task _receiveLoop;

    private bool _running;
    private int _boundPort;
    private DateTime? _startedAtUtc;
    private long _droppedCount;
    private long _minRetainedId = 1;

    // Monotonic, process-lifetime id counter. Never reset across sessions so a stale poll
    // cursor from a previous session can't replay or skip (§4a).
    private long _lastId;

    public ProtobufCaptureService(IConfiguration config, ILogger<ProtobufCaptureService> logger)
    {
        _logger = logger;
        _configuredPort = config.GetValue<int?>("ProtobufListenerPort") ?? 5001;
    }

    public CaptureStatus Start()
    {
        lock (_lock)
        {
            if (_running)
            {
                return BuildStatusLocked();
            }

            UdpClient udp;
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.EnableBroadcast = true;
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, _configuredPort));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Protobuf listener failed to bind port {Port}", _configuredPort);
                return new CaptureStatus
                {
                    Running = false,
                    Port = _configuredPort,
                    Failed = true,
                    Error = $"Could not bind UDP port {_configuredPort}: {ex.Message}",
                    LastId = _lastId,
                };
            }

            // Fresh session: clear the buffer. The Id counter and drop count carry over.
            _buffer.Clear();
            _udp = udp;
            _boundPort = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            _running = true;
            _startedAtUtc = DateTime.UtcNow;
            _cts = new CancellationTokenSource();

            var token = _cts.Token;
            var client = udp;
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(client, token));

            _logger.LogInformation("Protobuf listener bound to 0.0.0.0:{Port}", _boundPort);
            return BuildStatusLocked();
        }
    }

    public CaptureStatus Stop()
    {
        UdpClient toClose = null;
        CancellationTokenSource toCancel = null;

        lock (_lock)
        {
            if (_running)
            {
                _running = false;
                toClose = _udp;
                toCancel = _cts;
                _udp = null;
                _cts = null;
                _logger.LogInformation("Protobuf listener stopped");
            }

            // Buffer is retained so the user can still review the last capture until next Start.
            var status = BuildStatusLocked();

            // Close outside would race with BuildStatusLocked reads; do it after snapshotting.
            toCancel?.Cancel();
            toClose?.Dispose();

            return status;
        }
    }

    public CaptureStatus GetStatus()
    {
        lock (_lock)
        {
            return BuildStatusLocked();
        }
    }

    public MessagesPage GetMessagesAfter(long afterId, int limit)
    {
        if (limit <= 0)
        {
            limit = 500;
        }

        lock (_lock)
        {
            var messages = _buffer
                .Where(m => m.Id > afterId)
                .OrderBy(m => m.Id)
                .Take(limit)
                .ToArray();

            return new MessagesPage
            {
                Running = _running,
                Messages = messages,
                LastId = _lastId,
                DroppedBeforeId = _minRetainedId - 1,
            };
        }
    }

    public CaptureExport ExportCapture()
    {
        lock (_lock)
        {
            return new CaptureExport
            {
                ExportedAtUtc = DateTime.UtcNow,
                Port = _boundPort != 0 ? _boundPort : _configuredPort,
                StartedAtUtc = _startedAtUtc,
                Packets = _buffer
                    .OrderBy(m => m.Id)
                    .Select(m => new CaptureExportPacket
                    {
                        ReceivedAtUtc = m.ReceivedAtUtc,
                        Source = m.Source,
                        Hex = m.Hex,
                    })
                    .ToList(),
            };
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    // Socket closed on Stop, or a transient receive error; exit the loop.
                    break;
                }

                Capture(result.Buffer, result.RemoteEndPoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Protobuf listener receive loop terminated unexpectedly");
        }
    }

    // internal (not private) so buffer/cursor logic can be unit-tested without a live socket
    // via InternalsVisibleTo. Production callers reach it only through the receive loop.
    internal void Capture(byte[] data, IPEndPoint source)
    {
        // Decoding a ~98-byte packet is microseconds, so it runs inline in the receive loop.
        var message = VenstarPacketDecoder.Decode(data);
        message.ReceivedAtUtc = DateTime.UtcNow;
        message.Source = source == null ? "unknown" : $"{source.Address}:{source.Port}";

        lock (_lock)
        {
            message.Id = Interlocked.Increment(ref _lastId);
            _buffer.Add(message);

            while (_buffer.Count > MaxBufferedMessages)
            {
                _buffer.RemoveAt(0);
                _droppedCount++;
                _minRetainedId = _buffer[0].Id;
            }
        }
    }

    private CaptureStatus BuildStatusLocked()
    {
        return new CaptureStatus
        {
            Running = _running,
            Port = _running ? _boundPort : (_boundPort != 0 ? _boundPort : _configuredPort),
            CapturedCount = _buffer.Count,
            StartedAtUtc = _startedAtUtc,
            DroppedCount = _droppedCount,
            LastId = _lastId,
        };
    }

    public void Dispose()
    {
        CancellationTokenSource toCancel;
        UdpClient toClose;
        lock (_lock)
        {
            _running = false;
            toCancel = _cts;
            toClose = _udp;
            _cts = null;
            _udp = null;
        }

        toCancel?.Cancel();
        toClose?.Dispose();
        toCancel?.Dispose();
    }
}
