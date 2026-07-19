using System;

using WireModel = VenstarTranslator.Models.ProtobufCapture.Wire;

namespace VenstarTranslator.Models.ProtobufCapture;

// A single captured datagram, plus whatever the decoder could make of it.
// Serialized to the web UI with nulls INCLUDED: on the wire model, null means
// "field absent," and the UI renders it exactly that way (§4c, §5).
public class CapturedMessage
{
    // Monotonic, process-lifetime id (never reset). The page uses it as a poll cursor.
    public long Id { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    // "ip:port" of the sender.
    public string Source { get; set; }

    public int Length { get; set; }

    // Raw datagram as a lowercase hex string (no separators).
    public string Hex { get; set; }

    // True when the bytes decoded as a valid Venstar message (passed the validity gate).
    public bool Decoded { get; set; }

    // Command name (e.g. "SENSORDATA") when decoded; null otherwise.
    public string Command { get; set; }

    // Flat projection for the table columns — sensor commands (SENSORDATA/SENSORPAIR) only.
    public SensorSummary Summary { get; set; }

    // The full decoded tree for the expandable view — null when undecodable.
    public WireModel.SensorMessage Body { get; set; }

    // Reason the packet was not decodable as a Venstar message, when applicable.
    public string DecodeError { get; set; }
}
