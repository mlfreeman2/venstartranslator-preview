using System.Net;
using VenstarTranslator.Exceptions;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Services;
using Xunit;

namespace VenstarTranslator.Tests;

public class HttpDocumentFetcherTests
{
    private readonly HttpDocumentFetcher _fetcher;

    public HttpDocumentFetcherTests()
    {
        _fetcher = new HttpDocumentFetcher();
    }

    #region Successful Requests

    [Fact]
    public void FetchDocument_SuccessfulRequest_ReturnsContent()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupResponse(HttpStatusCode.OK, "{\"temperature\": 72.5}");

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act
        var result = fetcher.FetchDocument("http://example.com/api", false, null);

        // Assert
        Assert.Equal("{\"temperature\": 72.5}", result);
    }

    [Fact]
    public void FetchDocument_WithCustomHeaders_SendsHeaders()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupResponse(HttpStatusCode.OK, "{}");

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer token123" },
            new DataSourceHttpHeader { Name = "X-Custom", Value = "custom-value" }
        };

        // Act
        fetcher.FetchDocument("http://example.com/api", false, headers);

        // Assert
        var request = mockHandler.CapturedRequest;
        Assert.NotNull(request);
        Assert.Contains(request.Headers, h => h.Key == "Authorization" && h.Value.Contains("Bearer token123"));
        Assert.Contains(request.Headers, h => h.Key == "X-Custom" && h.Value.Contains("custom-value"));
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public void FetchDocument_RequestTimeout_ThrowsVenstarTranslatorExceptionWithTimeoutMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupTimeout();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("timed out after 10 seconds", ex.Message);
        Assert.Contains("took too long to respond", ex.Message);
    }

    #endregion

    #region Connection Error Tests

    [Fact]
    public void FetchDocument_ConnectionRefused_ThrowsVenstarTranslatorExceptionWithConnectionRefusedMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupConnectionRefused();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Connection refused", ex.Message);
        Assert.Contains("server is not accepting connections", ex.Message);
        Assert.Contains("Check that the URL is correct", ex.Message);
    }

    [Fact]
    public void FetchDocument_NetworkUnreachable_ThrowsVenstarTranslatorExceptionWithNetworkUnreachableMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupNetworkUnreachable();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Network unreachable", ex.Message);
        Assert.Contains("network is down or unavailable", ex.Message);
    }

    [Fact]
    public void FetchDocument_ConnectionReset_ThrowsVenstarTranslatorExceptionWithConnectionResetMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupConnectionReset();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Connection reset by peer", ex.Message);
        Assert.Contains("server closed the connection unexpectedly", ex.Message);
    }

    [Fact]
    public void FetchDocument_ConnectionResetWrappedInIOException_ThrowsVenstarTranslatorExceptionWithConnectionResetMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupConnectionResetWrappedInIOException();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Connection reset by peer", ex.Message);
        Assert.Contains("server closed the connection unexpectedly", ex.Message);
    }

    [Fact]
    public void FetchDocument_ConnectionAborted_ThrowsVenstarTranslatorExceptionWithConnectionAbortedMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupConnectionAborted();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Connection aborted", ex.Message);
        Assert.Contains("connection was terminated locally", ex.Message);
    }

    [Fact]
    public void FetchDocument_SocketTimeout_ThrowsVenstarTranslatorExceptionWithTimeoutMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupSocketTimeout();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Connection timed out", ex.Message);
        Assert.Contains("server did not respond within the expected time", ex.Message);
    }

    [Fact]
    public void FetchDocument_HostUnreachable_ThrowsVenstarTranslatorExceptionWithHostUnreachableMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupHostUnreachable();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Host unreachable", ex.Message);
        Assert.Contains("no network route to the specified host", ex.Message);
    }

    [Fact]
    public void FetchDocument_HostNotFound_ThrowsVenstarTranslatorExceptionWithHostNotFoundMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupHostNotFound();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Host not found", ex.Message);
        Assert.Contains("hostname could not be resolved", ex.Message);
    }

    [Fact]
    public void FetchDocument_UnknownSocketError_RethrowsOriginalException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupUnknownSocketError();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert - Should rethrow original HttpRequestException, not wrap in VenstarTranslatorException
        var ex = Assert.Throws<HttpRequestException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("Unknown socket error", ex.Message);
    }

    #endregion

    #region SSL Certificate Tests

    [Fact]
    public void FetchDocument_SSLCertificateError_ThrowsVenstarTranslatorExceptionWithSSLMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupSSLError();

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("https://example.com/api", false, null));

        Assert.Contains("SSL certificate validation failed", ex.Message);
        Assert.Contains("self-signed certificate", ex.Message);
        Assert.Contains("Ignore SSL Errors", ex.Message);
    }

    #endregion

    #region HTTP Status Code Tests

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Bad Request (HTTP 400)", "server rejected the request")]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized (HTTP 401)", "Authentication failed")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden (HTTP 403)", "Access denied")]
    [InlineData(HttpStatusCode.NotFound, "Not Found (HTTP 404)", "URL does not exist")]
    [InlineData(HttpStatusCode.MethodNotAllowed, "Method Not Allowed (HTTP 405)", "does not accept GET requests")]
    [InlineData(HttpStatusCode.InternalServerError, "Internal Server Error (HTTP 500)", "server encountered an error")]
    [InlineData(HttpStatusCode.BadGateway, "Bad Gateway (HTTP 502)", "invalid response from an upstream server")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Service Unavailable (HTTP 503)", "temporarily unavailable")]
    public void FetchDocument_HttpStatusCodeError_ThrowsVenstarTranslatorExceptionWithAppropriateMessage(
        HttpStatusCode statusCode, string expectedPhrase, string expectedGuidance)
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupResponse(statusCode, "Error");

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains(expectedPhrase, ex.Message);
        Assert.Contains(expectedGuidance, ex.Message);
    }

    #endregion

    #region Invalid Response Tests

    [Theory]
    [InlineData("The response ended prematurely")]
    [InlineData("invalid HTTP response")]
    [InlineData("unexpected end of stream")]
    public void FetchDocument_InvalidHttpResponse_ThrowsVenstarTranslatorExceptionWithInvalidResponseMessage(string errorMessage)
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupInvalidResponse(errorMessage);

        var fetcher = new HttpDocumentFetcher(() => mockHandler);

        // Act & Assert
        var ex = Assert.Throws<VenstarTranslatorException>(() =>
            fetcher.FetchDocument("http://example.com/api", false, null));

        Assert.Contains("server returned an invalid HTTP response", ex.Message);
        Assert.Contains("might not be an HTTP/HTTPS endpoint", ex.Message);
    }

    [Fact]
    public void FetchDocument_InvalidUrl_ThrowsException()
    {
        // Act & Assert - HttpClient throws ArgumentException for invalid URLs
        Assert.ThrowsAny<Exception>(() =>
            _fetcher.FetchDocument("not a valid url", false, null));
    }

    #endregion

    #region Helper Classes

    private class MockHttpMessageHandler : HttpClientHandler
    {
        private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _responseFunc;
        public HttpRequestMessage? CapturedRequest { get; private set; }

        public void SetupResponse(HttpStatusCode statusCode, string content)
        {
            _responseFunc = (request, ct) =>
            {
                CapturedRequest = request;

                if (statusCode == HttpStatusCode.OK)
                {
                    return Task.FromResult(new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(content)
                    });
                }
                else
                {
                    throw new HttpRequestException("HTTP error", null, statusCode);
                }
            };
        }

        public void SetupTimeout()
        {
            _responseFunc = (request, ct) =>
            {
                throw new TaskCanceledException("The operation was canceled.");
            };
        }

        public void SetupConnectionRefused()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);
                throw new HttpRequestException("Connection refused", socketEx);
            };
        }

        public void SetupNetworkUnreachable()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NetworkUnreachable);
                throw new HttpRequestException("Network unreachable", socketEx);
            };
        }

        public void SetupConnectionReset()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset);
                throw new HttpRequestException("Connection reset", socketEx);
            };
        }

        public void SetupConnectionResetWrappedInIOException()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset);
                var ioEx = new System.IO.IOException("Unable to read data from the transport connection: Connection reset by peer", socketEx);
                throw new HttpRequestException("An error occurred while sending the request", ioEx);
            };
        }

        public void SetupConnectionAborted()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionAborted);
                throw new HttpRequestException("Connection aborted", socketEx);
            };
        }

        public void SetupSocketTimeout()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.TimedOut);
                throw new HttpRequestException("Connection timed out", socketEx);
            };
        }

        public void SetupHostUnreachable()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostUnreachable);
                throw new HttpRequestException("Host unreachable", socketEx);
            };
        }

        public void SetupHostNotFound()
        {
            _responseFunc = (request, ct) =>
            {
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
                throw new HttpRequestException("Host not found", socketEx);
            };
        }

        public void SetupUnknownSocketError()
        {
            _responseFunc = (request, ct) =>
            {
                // Use an obscure socket error that we don't have a user-friendly message for
                var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NoBufferSpaceAvailable);
                throw new HttpRequestException("Unknown socket error", socketEx);
            };
        }

        public void SetupSSLError()
        {
            _responseFunc = (request, ct) =>
            {
                var authEx = new System.Security.Authentication.AuthenticationException("SSL handshake failed");
                throw new HttpRequestException("Authentication failed", authEx);
            };
        }

        public void SetupInvalidResponse(string errorMessage)
        {
            _responseFunc = (request, ct) =>
            {
                throw new HttpRequestException(errorMessage);
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responseFunc == null)
            {
                throw new InvalidOperationException("No response setup for mock handler");
            }

            return _responseFunc(request, cancellationToken);
        }
    }

    #endregion
}
