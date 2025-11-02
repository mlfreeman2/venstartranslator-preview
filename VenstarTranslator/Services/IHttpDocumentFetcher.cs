using System.Collections.Generic;
using VenstarTranslator.Models.Db;

namespace VenstarTranslator.Services;

public interface IHttpDocumentFetcher
{
    string FetchDocument(string url, bool ignoreSSLErrors, List<DataSourceHttpHeader> headers);
}
