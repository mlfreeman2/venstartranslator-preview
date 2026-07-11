namespace VenstarTranslator.Models.ProtobufCapture;

// A page of captured messages newer than the caller's cursor (§4c).
public class MessagesPage
{
    public bool Running { get; set; }

    public CapturedMessage[] Messages { get; set; }

    // High-water Id in the buffer; the caller advances its cursor to this.
    public long LastId { get; set; }

    // The lowest Id still retained. If this exceeds the caller's cursor, messages were
    // dropped from the ring buffer between polls and the UI should warn.
    public long DroppedBeforeId { get; set; }
}
