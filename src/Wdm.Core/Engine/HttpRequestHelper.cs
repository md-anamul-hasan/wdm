using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Builds and sends download requests. Redirects are followed manually so that credentials
/// (cookies, basic auth, referer) are only ever sent to the host the request was made for.
/// </summary>
public static class HttpRequestHelper
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

    private const int MaxRedirects = 10;

    public static HttpClient CreateClient(TimeSpan? timeout = null) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 64,
        ConnectTimeout = timeout ?? TimeSpan.FromSeconds(30)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        DownloadRequestOptions? options,
        RangeHeaderValue? range,
        CancellationToken ct)
    {
        var current = new Uri(url);
        var origin = current;

        for (int hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(method, current);
            Apply(request, options, range, origin, current);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null)
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();

            if (hop >= MaxRedirects)
            {
                throw new HttpRequestException("Too many redirects.");
            }

            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
    }

    private static void Apply(HttpRequestMessage request, DownloadRequestOptions? options, RangeHeaderValue? range, Uri origin, Uri current)
    {
        // TryAddWithoutValidation does not stop CR/LF smuggling extra headers onto the wire, so values are checked here
        request.Headers.TryAddWithoutValidation("User-Agent",
            string.IsNullOrWhiteSpace(options?.UserAgent) || HasControlCharacters(options!.UserAgent) ? DefaultUserAgent : options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

        if (range != null)
        {
            request.Headers.Range = range;
        }

        if (options == null) return;

        if (!string.IsNullOrWhiteSpace(options.Referer) && !HasControlCharacters(options.Referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", options.Referer);
        }

        // Credentials stay with the host they were issued for and never travel over a downgrade to plain http
        bool downgraded = origin.Scheme == Uri.UriSchemeHttps && current.Scheme != Uri.UriSchemeHttps;
        if (downgraded) return;

        string cookieHost = string.IsNullOrWhiteSpace(options.CookieHost) ? origin.Host : options.CookieHost;
        if (!string.IsNullOrWhiteSpace(options.Cookies) && !HasControlCharacters(options.Cookies) &&
            string.Equals(current.Host, cookieHost, StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("Cookie", options.Cookies);
        }

        if (!string.IsNullOrEmpty(options.Username) &&
            string.Equals(current.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    /// <summary>True for CR, LF and other control characters (tab excepted), which are never valid in these header values.</summary>
    internal static bool HasControlCharacters(string value) => value.Any(c => char.IsControl(c) && c != '\t');

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
