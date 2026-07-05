using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using VenstarTranslator.Exceptions;
using VenstarTranslator.Models;
using VenstarTranslator.Services;
using Xunit;

namespace VenstarTranslator.Tests;

public class HealthChecksClientTests
{
    private readonly CapturingHandler _handler;
    private readonly HealthChecksClient _client;

    public HealthChecksClientTests()
    {
        _handler = new CapturingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(_handler, disposeHandler: false));
        _client = new HealthChecksClient(factory.Object, new Mock<ILogger<HealthChecksClient>>().Object);
    }

    /// <summary>
    /// Records every request (method, URL, headers, body) and returns a canned response.
    /// Bodies are read during send because HealthChecksClient disposes its requests.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string ApiKey, string Body)> Requests { get; } = new();

        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _responseBody = "";
        private bool _throwOnSend;

        public void SetResponse(HttpStatusCode statusCode, string body = "")
        {
            _statusCode = statusCode;
            _responseBody = body;
        }

        public void ThrowOnSend() => _throwOnSend = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("X-Api-Key", out var apiKeys);
            Requests.Add((request.Method, request.RequestUri.ToString(), apiKeys == null ? null : string.Join(",", apiKeys), body));

            if (_throwOnSend)
            {
                throw new HttpRequestException("connection refused");
            }

            return new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) };
        }
    }

    #region Ping methods

    [Fact]
    public async Task PingSuccessAsync_SendsGetToPingUrl()
    {
        await _client.PingSuccessAsync("https://hc-ping.com/abc-123");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://hc-ping.com/abc-123", request.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PingSuccessAsync_BlankUrl_SendsNothing(string url)
    {
        await _client.PingSuccessAsync(url);

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task PingSuccessAsync_HttpError_DoesNotThrow()
    {
        _handler.ThrowOnSend();

        await _client.PingSuccessAsync("https://hc-ping.com/abc-123");
    }

    [Fact]
    public async Task PingFailureAsync_PostsBodyToFailUrl()
    {
        await _client.PingFailureAsync("https://hc-ping.com/abc-123", "it broke");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hc-ping.com/abc-123/fail", request.Url);
        Assert.Equal("it broke", request.Body);
    }

    [Fact]
    public async Task PingFailureAsync_BlankUrl_SendsNothing()
    {
        await _client.PingFailureAsync("  ", "it broke");

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task PingFailureAsync_HttpError_DoesNotThrow()
    {
        _handler.ThrowOnSend();

        await _client.PingFailureAsync("https://hc-ping.com/abc-123", "it broke");
    }

    #endregion

    #region Management API methods

    [Fact]
    public async Task CreateCheckAsync_PostsToChecksEndpointAndReturnsUuidFromPingUrl()
    {
        _handler.SetResponse(HttpStatusCode.Created, "{\"ping_url\": \"https://hc-ping.com/new-uuid-42\"}");

        var uuid = await _client.CreateCheckAsync("https://healthchecks.io/api/v3", "key-1", "My Check", 60, 300);

        Assert.Equal("new-uuid-42", uuid);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://healthchecks.io/api/v3/checks/", request.Url);
        Assert.Equal("key-1", request.ApiKey);
        Assert.Contains("\"name\": \"My Check\"", request.Body);
        Assert.Contains("\"timeout\": 60", request.Body);
        Assert.Contains("\"grace\": 300", request.Body);
    }

    [Fact]
    public async Task CreateCheckAsync_SelfHostedPingUrl_ReturnsLastSegment()
    {
        _handler.SetResponse(HttpStatusCode.Created, "{\"ping_url\": \"http://healthchecks:8000/ping/self-uuid\"}");

        var uuid = await _client.CreateCheckAsync("http://healthchecks:8000/api/v3", "key-1", "My Check", 60, 300);

        Assert.Equal("self-uuid", uuid);
    }

    [Fact]
    public async Task CreateCheckAsync_MissingPingUrl_ReturnsNull()
    {
        _handler.SetResponse(HttpStatusCode.Created, "{\"name\": \"My Check\"}");

        var uuid = await _client.CreateCheckAsync("https://healthchecks.io/api/v3", "key-1", "My Check", 60, 300);

        Assert.Null(uuid);
    }

    [Fact]
    public async Task CreateCheckAsync_ErrorResponse_ReturnsNull()
    {
        _handler.SetResponse(HttpStatusCode.Unauthorized);

        var uuid = await _client.CreateCheckAsync("https://healthchecks.io/api/v3", "bad-key", "My Check", 60, 300);

        Assert.Null(uuid);
    }

    [Fact]
    public async Task RenameCheckAsync_PostsNewName_ReturnsTrue()
    {
        var renamed = await _client.RenameCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1", "New Name");

        Assert.True(renamed);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://healthchecks.io/api/v3/checks/uuid-1", request.Url);
        Assert.Contains("\"name\": \"New Name\"", request.Body);
    }

    [Fact]
    public async Task RenameCheckAsync_ErrorResponse_ReturnsFalse()
    {
        _handler.SetResponse(HttpStatusCode.NotFound);

        var renamed = await _client.RenameCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1", "New Name");

        Assert.False(renamed);
    }

    [Fact]
    public async Task PauseCheckAsync_PostsToPauseEndpoint()
    {
        await _client.PauseCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://healthchecks.io/api/v3/checks/uuid-1/pause", request.Url);
        Assert.Equal("key-1", request.ApiKey);
    }

    [Fact]
    public async Task UnpauseCheckAsync_PostsToResumeEndpoint()
    {
        await _client.UnpauseCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://healthchecks.io/api/v3/checks/uuid-1/resume", request.Url);
    }

    [Fact]
    public async Task UpdateCheckScheduleAsync_PostsTimeoutAndGrace()
    {
        await _client.UpdateCheckScheduleAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1", 300, 1200);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://healthchecks.io/api/v3/checks/uuid-1", request.Url);
        Assert.Contains("\"timeout\": 300", request.Body);
        Assert.Contains("\"grace\": 1200", request.Body);
    }

    [Fact]
    public async Task DeleteCheckAsync_SendsDelete()
    {
        await _client.DeleteCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://healthchecks.io/api/v3/checks/uuid-1", request.Url);
    }

    [Fact]
    public async Task ManagementMethods_HttpErrors_DoNotThrow()
    {
        _handler.ThrowOnSend();

        await _client.PauseCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1");
        await _client.UnpauseCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1");
        await _client.UpdateCheckScheduleAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1", 60, 300);
        await _client.DeleteCheckAsync("https://healthchecks.io/api/v3", "key-1", "uuid-1");
    }

    #endregion

    #region GetPingUrl

    [Fact]
    public void GetPingUrl_SaasMode_HasNoPingPathPrefix()
    {
        var settings = new SettingsDTO { HealthChecksMode = "saas" };

        var url = HealthChecksClient.GetPingUrl(settings, "abc-123");

        Assert.Equal("https://hc-ping.com/abc-123", url);
    }

    [Fact]
    public void GetPingUrl_SelfHostedMode_UsesPingPathPrefix()
    {
        var settings = new SettingsDTO
        {
            HealthChecksMode = "selfhosted",
            HealthChecksSelfHostedUrl = "http://healthchecks:8000"
        };

        var url = HealthChecksClient.GetPingUrl(settings, "abc-123");

        Assert.Equal("http://healthchecks:8000/ping/abc-123", url);
    }

    [Fact]
    public void GetPingUrl_SelfHostedMode_TrimsTrailingSlash()
    {
        var settings = new SettingsDTO
        {
            HealthChecksMode = "selfhosted",
            HealthChecksSelfHostedUrl = "http://healthchecks:8000/"
        };

        var url = HealthChecksClient.GetPingUrl(settings, "abc-123");

        Assert.Equal("http://healthchecks:8000/ping/abc-123", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPingUrl_BlankUuid_ReturnsNull(string uuid)
    {
        var settings = new SettingsDTO { HealthChecksMode = "saas" };

        Assert.Null(HealthChecksClient.GetPingUrl(settings, uuid));
    }

    [Fact]
    public void GetPingUrl_NoneMode_ReturnsNull()
    {
        var settings = new SettingsDTO { HealthChecksMode = "none" };

        Assert.Null(HealthChecksClient.GetPingUrl(settings, "abc-123"));
    }

    [Fact]
    public void GetPingUrl_NullSettings_ReturnsNull()
    {
        Assert.Null(HealthChecksClient.GetPingUrl(null, "abc-123"));
    }

    [Fact]
    public void GetPingUrl_SelfHostedModeWithoutUrl_ReturnsNull()
    {
        var settings = new SettingsDTO { HealthChecksMode = "selfhosted" };

        Assert.Null(HealthChecksClient.GetPingUrl(settings, "abc-123"));
    }

    #endregion

    #region GetPingBaseUrl

    [Fact]
    public void GetPingBaseUrl_SaasMode_ReturnsHcPing()
    {
        var settings = new SettingsDTO { HealthChecksMode = "saas" };

        Assert.Equal("https://hc-ping.com", HealthChecksClient.GetPingBaseUrl(settings));
    }

    [Fact]
    public void GetPingBaseUrl_SelfHostedMode_TrimsTrailingSlash()
    {
        var settings = new SettingsDTO
        {
            HealthChecksMode = "selfhosted",
            HealthChecksSelfHostedUrl = "http://healthchecks:8000/"
        };

        Assert.Equal("http://healthchecks:8000", HealthChecksClient.GetPingBaseUrl(settings));
    }

    [Theory]
    [InlineData("none")]
    [InlineData(null)]
    public void GetPingBaseUrl_NoneOrUnsetMode_ReturnsNull(string mode)
    {
        var settings = new SettingsDTO { HealthChecksMode = mode };

        Assert.Null(HealthChecksClient.GetPingBaseUrl(settings));
    }

    #endregion

    #region GetApiBaseUrl

    [Fact]
    public void GetApiBaseUrl_SaasMode_ReturnsHealthChecksIoApi()
    {
        var settings = new SettingsDTO { HealthChecksMode = "saas" };

        Assert.Equal("https://healthchecks.io/api/v3", HealthChecksClient.GetApiBaseUrl(settings));
    }

    [Fact]
    public void GetApiBaseUrl_SelfHostedWithPort_AppendsApiPath()
    {
        var settings = new SettingsDTO
        {
            HealthChecksMode = "selfhosted",
            HealthChecksSelfHostedUrl = "http://healthchecks:8000"
        };

        Assert.Equal("http://healthchecks:8000/api/v3", HealthChecksClient.GetApiBaseUrl(settings));
    }

    [Fact]
    public void GetApiBaseUrl_SelfHostedDefaultPort_OmitsPort()
    {
        var settings = new SettingsDTO
        {
            HealthChecksMode = "selfhosted",
            HealthChecksSelfHostedUrl = "https://hc.example.com"
        };

        Assert.Equal("https://hc.example.com/api/v3", HealthChecksClient.GetApiBaseUrl(settings));
    }

    [Fact]
    public void GetApiBaseUrl_NoneMode_ReturnsNull()
    {
        var settings = new SettingsDTO { HealthChecksMode = "none" };

        Assert.Null(HealthChecksClient.GetApiBaseUrl(settings));
    }

    #endregion

    #region TruncateBody

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TruncateBody_NullOrEmpty_ReturnsEmpty(string body)
    {
        Assert.Equal(string.Empty, HealthChecksClient.TruncateBody(body));
    }

    [Fact]
    public void TruncateBody_ShortBody_ReturnsUnchanged()
    {
        var body = "a short failure message";

        Assert.Equal(body, HealthChecksClient.TruncateBody(body));
    }

    [Fact]
    public void TruncateBody_LongBody_TruncatesToLimitAndAppendsMarker()
    {
        var body = new string('x', 20_000);

        var result = HealthChecksClient.TruncateBody(body);

        Assert.EndsWith("\n[truncated]", result);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 10_000 + "\n[truncated]".Length);
    }

    [Fact]
    public void TruncateBody_MultiByteCharacters_TruncatesAtUtf8SafeBoundary()
    {
        // Each '°' is 2 bytes in UTF-8, so the 10,000 byte limit lands mid-character
        var body = new string('°', 9_999);

        var result = HealthChecksClient.TruncateBody(body);

        Assert.EndsWith("\n[truncated]", result);
        var content = result[..^"\n[truncated]".Length];
        Assert.All(content, c => Assert.Equal('°', c));
    }

    #endregion

    #region BuildFailureBody

    [Fact]
    public void BuildFailureBody_NoStackTrace_ReturnsTypeAndMessage()
    {
        var ex = new InvalidOperationException("something broke");

        var body = HealthChecksClient.BuildFailureBody(ex);

        Assert.Equal($"System.InvalidOperationException: something broke{Environment.NewLine}", body);
    }

    [Fact]
    public void BuildFailureBody_ThrownException_IncludesLeadingStackFrames()
    {
        Exception caught;
        try
        {
            throw new VenstarTranslatorException("fetch failed");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        var body = HealthChecksClient.BuildFailureBody(caught);

        Assert.StartsWith("VenstarTranslator.Exceptions.VenstarTranslatorException: fetch failed", body);
        Assert.Contains("at VenstarTranslator.Tests.HealthChecksClientTests", body);
    }

    #endregion
}
