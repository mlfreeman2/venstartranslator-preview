using System;
using System.Collections.Generic;

namespace VenstarTranslator.Models.ProtobufCapture;

// The venstar-protobuf-capture/1 file format (§4f). Raw hex only — no decoded fields are
// persisted, so decoder improvements retroactively apply and a sender's older build can't
// skew what the maintainer sees on import.
public class CaptureExport
{
    public string Format { get; set; } = "venstar-protobuf-capture/1";

    public DateTime ExportedAtUtc { get; set; }

    public int Port { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public List<CaptureExportPacket> Packets { get; set; } = new();
}

public class CaptureExportPacket
{
    public DateTime ReceivedAtUtc { get; set; }

    public string Source { get; set; }

    public string Hex { get; set; }
}

// Response from POST import (§4f). A pure view: no capture state is touched.
public class ImportResult
{
    public CapturedMessage[] Messages { get; set; }

    public int Skipped { get; set; }
}
