using System.Text.RegularExpressions;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Site and Media Grabber engine: analyzes a web page or media link and extracts the downloadable
/// files, streams and archives it references.
/// </summary>
public class SiteGrabberService
{
    private const long MaxPageBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly HttpClient Client = HttpRequestHelper.CreateClient();

    private static readonly Regex MediaPattern = new(
        @"(?:href|src|data-src|data-url)\s*=\s*[""'](?<url>[^""'#\s>]+)[""']|" +
        @"[""'](?<url>https?:\/\/[^""'\s\\]+\.(?:mp4|m3u8|webm|mkv|mp3|wav|flac|aac|ogg|zip|rar|7z|tar|gz|iso|pdf|docx?|xlsx?|pptx?|exe|msi|apk|png|jpe?g|webp)[^""'\s\\]*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>Lists downloadable links on <paramref name="pageUrl"/>. Network failures are thrown to the caller.</summary>
    public async Task<List<GrabMediaItem>> GrabLinksAsync(string pageUrl, string? userAgent = null, CancellationToken ct = default)
    {
        var results = new List<GrabMediaItem>();
        if (string.IsNullOrWhiteSpace(pageUrl)) return results;

        pageUrl = pageUrl.Trim();
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
        {
            if (pageUrl.Contains("://") || !Uri.TryCreate("https://" + pageUrl, UriKind.Absolute, out baseUri))
            {
                return results;
            }
        }

        // Direct file URL
        var directExt = Path.GetExtension(baseUri.AbsolutePath);
        if (FileCategories.IsKnownExtension(directExt))
        {
            results.Add(CreateItem(baseUri, directExt, results.Count));
            return results;
        }

        string html;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(RequestTimeout);
            try
            {
                using var response = await HttpRequestHelper.SendAsync(
                    Client, HttpMethod.Get, baseUri.AbsoluteUri, new DownloadRequestOptions { UserAgent = userAgent }, null, timeout.Token);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    // Not a page: the URL itself is the download
                    results.Add(CreateItem(baseUri, directExt, results.Count));
                    return results;
                }

                await response.Content.LoadIntoBufferAsync(MaxPageBytes, timeout.Token);
                html = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"The page did not respond within {RequestTimeout.TotalSeconds:F0} seconds.");
            }
        }

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MediaPattern.Matches(html))
        {
            var raw = match.Groups["url"].Value;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!Uri.TryCreate(baseUri, raw, out var resolved)) continue;
            if (resolved.Scheme is not ("http" or "https")) continue;
            if (!seenUrls.Add(resolved.AbsoluteUri)) continue;

            var ext = Path.GetExtension(resolved.AbsolutePath);
            if (FileCategories.IsKnownExtension(ext))
            {
                results.Add(CreateItem(resolved, ext, results.Count));
            }
        }

        return results;
    }

    private static GrabMediaItem CreateItem(Uri uri, string extension, int index)
    {
        var fileName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"download_{index + 1}{extension}";

        extension = extension.ToLowerInvariant();
        return new GrabMediaItem(uri.AbsoluteUri, fileName, FileCategories.DisplayName(FileCategories.FromExtension(extension)), extension);
    }
}
