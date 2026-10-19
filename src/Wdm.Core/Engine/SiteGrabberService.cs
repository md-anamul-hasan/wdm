using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Site and Media Grabber engine: crawls and analyzes target web pages or media links,
/// extracting downloadable files, streams, archives, and media assets.
/// </summary>
public class SiteGrabberService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        CheckCertificateRevocationList = false
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly Regex MediaPattern = new(
        @"(?:href|src|data-src|data-url)\s*=\s*[""'](?<url>[^""'#\s>]+)[""']|" +
        @"[""'](?<url>https?:\/\/[^""'\s\\]+\.(?:mp4|m3u8|webm|mkv|mp3|wav|flac|aac|ogg|zip|rar|7z|tar|gz|iso|pdf|docx?|xlsx?|pptx?|exe|msi|apk|png|jpe?g|webp)[^""'\s\\]*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    static SiteGrabberService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");
    }

    public async Task<List<GrabMediaItem>> GrabLinksAsync(string pageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)) return new List<GrabMediaItem>();

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri))
        {
            if (Uri.TryCreate("https://" + pageUrl, UriKind.Absolute, out var httpsUri))
            {
                baseUri = httpsUri;
            }
            else
            {
                return new List<GrabMediaItem>();
            }
        }

        var results = new List<GrabMediaItem>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Direct file URL check
        var directExt = Path.GetExtension(baseUri.AbsolutePath).ToLowerInvariant();
        if (IsDownloadableExtension(directExt))
        {
            var fileName = Path.GetFileName(baseUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "download" + directExt;
            results.Add(new GrabMediaItem(baseUri.AbsoluteUri, fileName, Categorize(directExt), directExt));
            return results;
        }

        string html;
        try
        {
            using var response = await HttpClient.GetAsync(baseUri, ct);
            if (!response.IsSuccessStatusCode) return results;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                // Not HTML, direct binary download
                var name = Path.GetFileName(baseUri.LocalPath);
                if (string.IsNullOrWhiteSpace(name)) name = "download.dat";
                results.Add(new GrabMediaItem(baseUri.AbsoluteUri, name, Categorize(directExt), directExt));
                return results;
            }

            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return results;
        }

        var matches = MediaPattern.Matches(html);
        foreach (Match match in matches)
        {
            var raw = match.Groups["url"].Value;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, raw, out var resolvedUri)) continue;
            if (!seenUrls.Add(resolvedUri.AbsoluteUri)) continue;

            var ext = Path.GetExtension(resolvedUri.AbsolutePath).ToLowerInvariant();
            if (IsDownloadableExtension(ext))
            {
                var fileName = Path.GetFileName(resolvedUri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"download_{results.Count + 1}{ext}";
                }

                results.Add(new GrabMediaItem(resolvedUri.AbsoluteUri, fileName, Categorize(ext), ext));
            }
        }

        return results;
    }

    private static bool IsDownloadableExtension(string ext) => ext switch
    {
        ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" or ".m3u8" or ".ts" or ".flv" => true,
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => true,
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" or ".bz2" => true,
        ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx" or ".epub" => true,
        ".exe" or ".msi" or ".apk" or ".bat" or ".cmd" or ".pkg" => true,
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg" => true,
        _ => false
    };

    private static string Categorize(string ext) => ext switch
    {
        ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" or ".m3u8" or ".ts" or ".flv" => "Video",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => "Music",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" or ".bz2" => "Compressed",
        ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx" or ".epub" => "Documents",
        ".exe" or ".msi" or ".apk" or ".bat" or ".cmd" or ".pkg" => "Programs",
        _ => "Other"
    };
}
