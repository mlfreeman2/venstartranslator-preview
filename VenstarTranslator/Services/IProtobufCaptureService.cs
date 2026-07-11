using VenstarTranslator.Models.ProtobufCapture;

namespace VenstarTranslator.Services;

public interface IProtobufCaptureService
{
    // Begins a fresh capture session (clears the buffer, opens the socket). Returns a failed
    // status on bind failure rather than throwing (§4a).
    CaptureStatus Start();

    // Cancels the receive loop and closes the socket; the buffer is retained.
    CaptureStatus Stop();

    CaptureStatus GetStatus();

    // Messages with Id > afterId, up to limit, oldest-first.
    MessagesPage GetMessagesAfter(long afterId, int limit);

    // Snapshot of the ring buffer for download. Works while stopped (§4f).
    CaptureExport ExportCapture();
}
