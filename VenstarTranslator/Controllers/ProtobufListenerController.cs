using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using VenstarTranslator.Models;
using VenstarTranslator.Models.ProtobufCapture;
using VenstarTranslator.Services;

namespace VenstarTranslator.Controllers;

// Diagnostic endpoints backing web/protobuf.html (§4d). A dedicated controller rather than
// growing APIController — cleaner separation and friendlier to a future protocol reorg.
[ApiController]
[Route("/api/protobuf-listener")]
public class ProtobufListenerController : ControllerBase
{
    // Import guard rails (§4f).
    private const int MaxImportPackets = 5000;

    private readonly IProtobufCaptureService _capture;
    private readonly ILogger<ProtobufListenerController> _logger;

    public ProtobufListenerController(IProtobufCaptureService capture, ILogger<ProtobufListenerController> logger)
    {
        _capture = capture;
        _logger = logger;
    }

    [HttpPost]
    [Route("start")]
    public ActionResult Start()
    {
        var status = _capture.Start();
        if (status.Failed)
        {
            return StatusCode(409, new MessageResponse { Message = status.Error });
        }

        return new JsonResult(status);
    }

    [HttpPost]
    [Route("stop")]
    public ActionResult Stop()
    {
        return new JsonResult(_capture.Stop());
    }

    [HttpGet]
    [Route("status")]
    public ActionResult Status()
    {
        return new JsonResult(_capture.GetStatus());
    }

    [HttpGet]
    [Route("messages")]
    public ActionResult Messages(long afterId = 0, int limit = 500)
    {
        return new JsonResult(_capture.GetMessagesAfter(afterId, limit));
    }

    [HttpGet]
    [Route("export")]
    public ActionResult Export()
    {
        var export = _capture.ExportCapture();
        if (export.Packets.Count == 0)
        {
            return StatusCode(409, new MessageResponse { Message = "Nothing captured yet — start a capture first." });
        }

        var fileName = $"venstar-capture-{export.ExportedAtUtc:yyyyMMdd-HHmmss}.json";
        // Emit the documented camelCase venstar-protobuf-capture/1 shape (§4f), matching the
        // MVC pipeline and the UI prototype — not JsonConvert's PascalCase default.
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Formatting = Newtonsoft.Json.Formatting.Indented,
        };
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(export, settings);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", fileName);
    }

    // Decode an uploaded capture file for viewing. Pure — touches no capture state.
    // Re-runs every packet through the same decoder as live capture (§4f).
    [HttpPost]
    [Route("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public ActionResult Import([FromBody] CaptureExport file)
    {
        if (file == null)
        {
            return StatusCode(400, new MessageResponse { Message = "Request body is not a valid capture file." });
        }

        var format = file.Format ?? string.Empty;
        if (!format.StartsWith("venstar-protobuf-capture/1", StringComparison.Ordinal))
        {
            return StatusCode(400, new MessageResponse
            {
                Message = $"Unsupported capture format \"{(string.IsNullOrEmpty(format) ? "(missing)" : format)}\" — expected venstar-protobuf-capture/1.",
            });
        }

        var packets = file.Packets ?? new List<CaptureExportPacket>();
        if (packets.Count > MaxImportPackets)
        {
            return StatusCode(400, new MessageResponse
            {
                Message = $"Capture file has {packets.Count} packets, exceeding the {MaxImportPackets}-packet import limit.",
            });
        }

        int skipped = 0;
        long viewId = 0;
        var messages = new List<CapturedMessage>();

        foreach (var packet in packets)
        {
            if (packet == null || string.IsNullOrWhiteSpace(packet.Hex))
            {
                skipped++;
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(packet.Hex.Trim());
            }
            catch (FormatException)
            {
                skipped++;
                continue;
            }

            var message = VenstarPacketDecoder.Decode(bytes);
            message.Id = ++viewId; // per-view ids; never exported.
            message.ReceivedAtUtc = packet.ReceivedAtUtc;
            message.Source = string.IsNullOrWhiteSpace(packet.Source) ? "unknown" : packet.Source;
            messages.Add(message);
        }

        return new JsonResult(new ImportResult
        {
            Messages = messages.ToArray(),
            Skipped = skipped,
        });
    }
}
