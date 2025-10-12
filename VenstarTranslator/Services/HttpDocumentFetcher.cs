using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

public class HttpDocumentFetcher : IHttpDocumentFetcher
{
    private const int TimeoutSeconds = 10;

    public string FetchDocument(string url, bool ignoreSSLErrors, List<DataSourceHttpHeader> headers)
    {
        try
        {
            using var clientHandler = new HttpClientHandler();
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
            throw new HttpRequestException($"Request timed out after {TimeoutSeconds} seconds. The server took too long to respond.", ex);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx)
        {
            if (socketEx.SocketErrorCode == SocketError.ConnectionRefused)
            {
                throw new HttpRequestException("Connection refused. The server is not accepting connections. Check that the URL is correct and the server is running.", ex);
            }
            throw new HttpRequestException($"Network error: {socketEx.Message}", ex);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new HttpRequestException("SSL certificate validation failed. If this is a self-signed certificate, enable 'Ignore SSL Errors' in the sensor configuration.", ex);
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
                _ => $"HTTP {statusCode} error. {ex.Message}"
            };
            throw new HttpRequestException(message, ex, ex.StatusCode);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("The response ended prematurely") ||
                                               ex.Message.Contains("invalid HTTP response") ||
                                               ex.Message.Contains("unexpected end of stream"))
        {
            throw new HttpRequestException("The server returned an invalid HTTP response. This might not be an HTTP/HTTPS endpoint, or the server is misconfigured.", ex);
        }
        catch (UriFormatException ex)
        {
            throw new HttpRequestException($"Invalid URL format: {ex.Message}", ex);
        }
    }
}
