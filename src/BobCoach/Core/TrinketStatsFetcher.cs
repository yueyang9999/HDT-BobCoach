using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BobCoach.Engine
{
    public sealed class ExternalFetchResult
    {
        public bool NotModified;
        public byte[] Content;
        public string ETag = "";
        public DateTime LastModifiedUtc;
    }

    /// <summary>受限HTTPS读取器：固定域名、超时、响应体上限、条件请求。</summary>
    public sealed class TrinketStatsFetcher : IDisposable
    {
        private static readonly HashSet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api.hearthstonejson.com",
        };

        private readonly HttpClient _http;

        public TrinketStatsFetcher()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            _http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("BobCoach/0.2 external-validation-readonly");
        }

        public async Task<ExternalFetchResult> FetchAsync(
            string url, string etag, DateTime? modifiedUtc,
            int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var uri = new Uri(url, UriKind.Absolute);
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !AllowedHosts.Contains(uri.Host))
                throw new InvalidOperationException("external-source-not-allowed:" + uri.Host);

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(timeout);
                if (!string.IsNullOrWhiteSpace(etag))
                {
                    EntityTagHeaderValue parsed;
                    if (EntityTagHeaderValue.TryParse(etag, out parsed))
                        request.Headers.IfNoneMatch.Add(parsed);
                }
                if (modifiedUtc.HasValue && modifiedUtc.Value != DateTime.MinValue)
                    request.Headers.IfModifiedSince = new DateTimeOffset(modifiedUtc.Value.ToUniversalTime());

                using (var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotModified)
                    {
                        return new ExternalFetchResult
                        {
                            NotModified = true,
                            ETag = response.Headers.ETag != null ? response.Headers.ETag.ToString() : etag,
                            LastModifiedUtc = GetLastModified(response),
                        };
                    }
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new HttpRequestException("HTTP " + (int)response.StatusCode + " " + uri.Host);

                    var declared = response.Content.Headers.ContentLength;
                    if (declared.HasValue && declared.Value > maxBytes)
                        throw new InvalidDataException("response-too-large:" + declared.Value);

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + read > maxBytes)
                                throw new InvalidDataException("response-too-large-streamed");
                            output.Write(buffer, 0, read);
                        }
                        return new ExternalFetchResult
                        {
                            Content = output.ToArray(),
                            ETag = response.Headers.ETag != null ? response.Headers.ETag.ToString() : "",
                            LastModifiedUtc = GetLastModified(response),
                        };
                    }
                }
            }
        }

        private static DateTime GetLastModified(HttpResponseMessage response)
        {
            var value = response.Content != null ? response.Content.Headers.LastModified : null;
            return value.HasValue ? value.Value.UtcDateTime : DateTime.MinValue;
        }

        public void Dispose() { _http.Dispose(); }
    }
}
