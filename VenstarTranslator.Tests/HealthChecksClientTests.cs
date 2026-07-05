using System;
using System.Text;
using VenstarTranslator.Exceptions;
using VenstarTranslator.Models;
using VenstarTranslator.Services;
using Xunit;

namespace VenstarTranslator.Tests;

public class HealthChecksClientTests
{
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
