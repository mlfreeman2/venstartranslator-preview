using System.Collections.Generic;
using System.Net.Http;
using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

public class HttpDocumentFetcher : IHttpDocumentFetcher
{
    public string FetchDocument(string url, bool ignoreSSLErrors, List<DataSourceHttpHeader> headers)
    {
        using var clientHandler = new HttpClientHandler();
        if (ignoreSSLErrors)
        {
            clientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        using var client = new HttpClient(clientHandler);
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
}
