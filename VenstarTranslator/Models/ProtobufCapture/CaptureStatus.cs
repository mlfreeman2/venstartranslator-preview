using System;

namespace VenstarTranslator.Models.ProtobufCapture;

// Snapshot of the capture session state (§4c).
public class CaptureStatus
{
    public bool Running { get; set; }

    // The ACTUAL bound port (matters when the configured port is 0 = ephemeral, used by tests).
    public int Port { get; set; }

    public int CapturedCount { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    // How many messages have been evicted from the ring buffer over the session's life.
    public long DroppedCount { get; set; }

    // Current high-water Id. Start returns this so the page can adopt it as its poll cursor,
    // preventing a stale cursor from a previous session from replaying or skipping.
    public long LastId { get; set; }

    // Set on a failed Start (e.g. bind failure) so the controller can map it to 409.
    public bool Failed { get; set; }

    public string Error { get; set; }
}
