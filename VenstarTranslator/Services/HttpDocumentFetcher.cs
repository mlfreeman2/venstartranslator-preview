using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using VenstarTranslator.Exceptions;
using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Services;

public class HttpDocumentFetcher : IHttpDocumentFetcher
{
    private const int TimeoutSeconds = 10;
    private readonly Func<HttpClientHandler> _handlerFactory;

    public HttpDocumentFetcher() : this(null)
    {
    }

    public HttpDocumentFetcher(Func<HttpClientHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory ?? (() => new HttpClientHandler());
    }

    public string FetchDocument(string url, bool ignoreSSLErrors, List<DataSourceHttpHeader> headers)
    {
        try
        {
            using var clientHandler = _handlerFactory();
            if (ignoreSSLErrors)
            {
                clientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using var client = new HttpClient(clientHandler)
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Name, header.Value);
                }
            }

            var response = client.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested == false)
        {
            throw new VenstarTranslatorException($"Request timed out after {TimeoutSeconds} seconds. The server took too long to respond.", ex);
        }
        catch (HttpRequestException ex) when (TryGetSocketException(ex, out var socketEx))
        {
            var errorMessage = GetSocketErrorMessage(socketEx);
            if (errorMessage != null)
            {
                throw new VenstarTranslatorException(errorMessage, ex);
            }

            // Unknown socket error - rethrow original exception for full diagnostics
            throw;
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new VenstarTranslatorException("SSL certificate validation failed. If this is a self-signed certificate, enable 'Ignore SSL Errors' in the sensor configuration.", ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode != null)
        {
            var statusCode = (int)ex.StatusCode.Value;
            var message = statusCode switch
            {
                400 => "Bad Request (HTTP 400). The server rejected the request. Check your URL and any required query parameters.",
                401 => "Unauthorized (HTTP 401). Authentication failed. Check your headers and API keys.",
                403 => "Forbidden (HTTP 403). Access denied. Check your authentication credentials and permissions.",
                404 => "Not Found (HTTP 404). The URL does not exist. Verify the endpoint URL is correct.",
                405 => "Method Not Allowed (HTTP 405). The server does not accept GET requests at this URL.",
                500 => "Internal Server Error (HTTP 500). The server encountered an error. Check the server logs.",
                502 => "Bad Gateway (HTTP 502). The server received an invalid response from an upstream server.",
                503 => "Service Unavailable (HTTP 503). The server is temporarily unavailable. Try again later.",
                _ => null
            };

            if (message != null)
            {
                throw new VenstarTranslatorException(message, ex);
            }

            // Unknown HTTP status code - rethrow original exception for full diagnostics
            throw;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("The response ended prematurely") ||
                                               ex.Message.Contains("invalid HTTP response") ||
                                               ex.Message.Contains("unexpected end of stream"))
        {
            throw new VenstarTranslatorException("The server returned an invalid HTTP response. This might not be an HTTP/HTTPS endpoint, or the server is misconfigured.", ex);
        }
        catch (UriFormatException ex)
        {
            throw new VenstarTranslatorException($"Invalid URL format: {ex.Message}", ex);
        }
    }

    private static bool TryGetSocketException(HttpRequestException ex, out SocketException socketEx)
    {
        // Check for direct SocketException
        if (ex.InnerException is SocketException directSocketEx)
        {
            socketEx = directSocketEx;
            return true;
        }

        // Check for IOException wrapping SocketException
        if (ex.InnerException is IOException ioEx && ioEx.InnerException is SocketException nestedSocketEx)
        {
            socketEx = nestedSocketEx;
            return true;
        }

        socketEx = null;
        return false;
    }

    private static string GetSocketErrorMessage(SocketException socketEx)
    {
        return socketEx.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => "Connection refused. The server is not accepting connections. Check that the URL is correct and the server is running.",
            SocketError.ConnectionReset => "Connection reset by peer. The server closed the connection unexpectedly. This may indicate the server is overloaded, restarting, or encountered an error while processing the request.",
            SocketError.ConnectionAborted => "Connection aborted. The connection was terminated locally. This may be due to network issues or the server closing the connection.",
            SocketError.TimedOut => "Connection timed out. The server did not respond within the expected time. Check that the server is reachable and not overloaded.",
            SocketError.HostUnreachable => "Host unreachable. There is no network route to the specified host. Check the URL and your network configuration.",
            SocketError.NetworkUnreachable => "Network unreachable. The network is down or unavailable. Check your network connection.",
            SocketError.HostNotFound => "Host not found. The hostname could not be resolved. Check the URL for typos or DNS configuration.",
            _ => null
        };
    }
}
