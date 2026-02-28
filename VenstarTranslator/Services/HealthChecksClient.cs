using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace VenstarTranslator.Services;

public class HealthChecksClient : IHealthChecksClient
{
    private const int MaxBodyBytes = 10_000;
    private const string AppNamespace = "VenstarTranslator";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthChecksClient> _logger;

    public HealthChecksClient(IHttpClientFactory httpClientFactory, ILogger<HealthChecksClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task PingSuccessAsync(string pingUrl)
    {
        if (string.IsNullOrWhiteSpace(pingUrl))
        {
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("HealthChecks");
            await client.GetAsync(pingUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send healthchecks.io success ping to {Url}", pingUrl);
        }
    }

    public async Task PingFailureAsync(string pingUrl, string body)
    {
        if (string.IsNullOrWhiteSpace(pingUrl))
        {
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("HealthChecks");
            var truncated = TruncateBody(body);
            var content = new StringContent(truncated, Encoding.UTF8, "text/plain");
            await client.PostAsync(pingUrl + "/fail", content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send healthchecks.io failure ping to {Url}", pingUrl);
        }
    }

    public async Task<string> CreateCheckAsync(string apiBaseUrl, string apiKey, string name, int timeout, int grace)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Post, $"{apiBaseUrl}/checks/", apiKey,
                new JObject { ["name"] = name, ["timeout"] = timeout, ["grace"] = grace });
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            var pingUrl = obj["ping_url"]?.Value<string>();
            if (pingUrl == null)
            {
                return null;
            }

            // ping_url is like "https://host/ping/<uuid>" — extract the last segment
            return pingUrl.Split('/').Last();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create healthchecks.io check '{Name}'", name);
            return null;
        }
    }

    public async Task<string> GetCheckNameAsync(string apiBaseUrl, string apiKey, string uuid)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Get, $"{apiBaseUrl}/checks/{uuid}", apiKey);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json)["name"]?.Value<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get healthchecks.io check name for UUID '{Uuid}'", uuid);
            return null;
        }
    }

    public async Task<bool> RenameCheckAsync(string apiBaseUrl, string apiKey, string uuid, string newName)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Post, $"{apiBaseUrl}/checks/{uuid}", apiKey,
                new JObject { ["name"] = newName });
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename healthchecks.io check '{Uuid}' to '{Name}'", uuid, newName);
            return false;
        }
    }

    public async Task PauseCheckAsync(string apiBaseUrl, string apiKey, string uuid)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Post, $"{apiBaseUrl}/checks/{uuid}/pause", apiKey);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pause healthchecks.io check '{Uuid}'", uuid);
        }
    }

    public async Task UnpauseCheckAsync(string apiBaseUrl, string apiKey, string uuid)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Post, $"{apiBaseUrl}/checks/{uuid}/resume", apiKey);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unpause healthchecks.io check '{Uuid}'", uuid);
        }
    }

    public async Task UpdateCheckScheduleAsync(string apiBaseUrl, string apiKey, string uuid, int timeout, int grace)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Post, $"{apiBaseUrl}/checks/{uuid}", apiKey,
                new JObject { ["timeout"] = timeout, ["grace"] = grace });
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update schedule for healthchecks.io check '{Uuid}'", uuid);
        }
    }

    public async Task DeleteCheckAsync(string apiBaseUrl, string apiKey, string uuid)
    {
        try
        {
            var response = await SendManagementRequestAsync(
                HttpMethod.Delete, $"{apiBaseUrl}/checks/{uuid}", apiKey);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete healthchecks.io check '{Uuid}'", uuid);
        }
    }

    /// <summary>
    /// Derive the management API base URL from the healthchecks.io ping base URL.
    /// Extracts scheme+host+port and appends /api/v3.
    /// Example: "http://healthchecks:8000/ping" → "http://healthchecks:8000/api/v3"
    /// </summary>
    internal static string GetManagementApiBaseUrl(string healthChecksBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(healthChecksBaseUrl))
        {
            return null;
        }

        var uri = new Uri(healthChecksBaseUrl);
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}/api/v3";
    }

    private async Task<HttpResponseMessage> SendManagementRequestAsync(
        HttpMethod method, string url, string apiKey, JObject body = null)
    {
        using var client = _httpClientFactory.CreateClient("HealthChecks");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Api-Key", apiKey);
        if (body != null)
        {
            request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Build a failure body from a non-VenstarTranslatorException.
    /// Includes exception type + message, the first 4 stack frames,
    /// then skips to the first 4 frames from our source code (if any).
    /// </summary>
    internal static string BuildFailureBody(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(ex.GetType().FullName);
        sb.Append(": ");
        sb.AppendLine(ex.Message);

        if (ex.StackTrace == null)
        {
            return sb.ToString();
        }

        var frames = ex.StackTrace.Split('\n')
            .Select(f => f.TrimEnd('\r'))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToArray();

        const int maxLeadingFrames = 4;
        const int maxAppFrames = 4;

        // Take the first 4 frames (top of call stack, where exception originated)
        var leadingFrames = frames.Take(maxLeadingFrames).ToArray();
        foreach (var frame in leadingFrames)
        {
            sb.AppendLine(frame);
        }

        // Look for app frames in the remaining stack
        var remainingFrames = frames.Skip(maxLeadingFrames).ToArray();
        var appFrames = remainingFrames
            .Where(f => f.Contains(AppNamespace))
            .Take(maxAppFrames)
            .ToArray();

        if (appFrames.Length > 0)
        {
            var skippedCount = remainingFrames.TakeWhile(f => !f.Contains(AppNamespace)).Count();
            if (skippedCount > 0)
            {
                sb.AppendLine($"   ... (skipped {skippedCount} runtime frames)");
            }
            foreach (var frame in appFrames)
            {
                sb.AppendLine(frame);
            }
        }

        return sb.ToString();
    }

    internal static string TruncateBody(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(body);
        if (bytes.Length <= MaxBodyBytes)
        {
            return body;
        }

        // Truncate at a UTF-8 safe boundary
        var truncated = Encoding.UTF8.GetString(bytes, 0, MaxBodyBytes);
        while (Encoding.UTF8.GetByteCount(truncated) > MaxBodyBytes)
        {
            truncated = truncated[..^1];
        }
        return truncated + "\n[truncated]";
    }
}
