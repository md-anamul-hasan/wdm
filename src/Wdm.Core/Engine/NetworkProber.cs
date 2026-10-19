using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

public class NetworkProber
{
    private readonly HttpClient _httpClient;

    public NetworkProber(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None, // We want raw byte stream for ranges
            MaxAutomaticRedirections = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        });
    }

    public async Task<DownloadMetadata> ProbeAsync(string url, string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        var metadata = new DownloadMetadata
        {
            Url = url
        };


        HttpResponseMessage? response = null;
        try
        {
            // 1. Try HEAD request first
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            headRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WDM/1.0");
            headRequest.Headers.Add("Accept", "*/*");
            
            response = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Some servers return 405 Method Not Allowed or 403 on HEAD. In that case, fall back to Range GET
            if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                response.Dispose();
                using var getRangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
                getRangeRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WDM/1.0");
                getRangeRequest.Headers.Range = new RangeHeaderValue(0, 0);
                response = await _httpClient.SendAsync(getRangeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            // Read headers
            metadata.SupportsRanges = response.Headers.AcceptRanges.Contains("bytes") || 
                                     response.Content.Headers.ContentRange != null;

            if (response.Content.Headers.ContentRange?.Length.HasValue == true)
            {
                metadata.TotalBytes = response.Content.Headers.ContentRange.Length.Value;
                metadata.SupportsRanges = true;
            }
            else if (response.Content.Headers.ContentLength.HasValue)
            {
                metadata.TotalBytes = response.Content.Headers.ContentLength.Value;
            }

            if (response.Headers.ETag != null)
            {
                metadata.ETag = response.Headers.ETag.Tag;
            }

            if (response.Content.Headers.LastModified.HasValue)
            {
                metadata.LastModified = response.Content.Headers.LastModified.Value;
            }

            metadata.ContentType = response.Content.Headers.ContentType?.MediaType;

            // Extract filename from Content-Disposition or URL
            metadata.FileName = ExtractFileName(response, url);

            // Compute target file path
            var destDir = destinationDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            metadata.TargetFilePath = Path.Combine(destDir, metadata.FileName);
            metadata.TargetFilePath = GetUniqueFilePath(metadata.TargetFilePath);

            return metadata;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static string ExtractFileName(HttpResponseMessage response, string url)
    {
        // 1. Try Content-Disposition
        if (response.Content.Headers.ContentDisposition != null)
        {
            var cd = response.Content.Headers.ContentDisposition;
            if (!string.IsNullOrWhiteSpace(cd.FileNameStar))
            {
                var decoded = DecodeRfc5987(cd.FileNameStar);
                if (!string.IsNullOrWhiteSpace(decoded)) return SanitizeFileName(decoded);
            }
            if (!string.IsNullOrWhiteSpace(cd.FileName))
            {
                var unquoted = cd.FileName.Trim('"').Trim('\'');
                if (!string.IsNullOrWhiteSpace(unquoted)) return SanitizeFileName(unquoted);
            }
        }

        // Raw header search fallback
        if (response.Content.Headers.TryGetValues("Content-Disposition", out var cdValues))
        {
            foreach (var val in cdValues)
            {
                var matchStar = Regex.Match(val, @"filename\*=(?:UTF-8''|utf-8'')([^;]+)", RegexOptions.IgnoreCase);
                if (matchStar.Success)
                {
                    return SanitizeFileName(Uri.UnescapeDataString(matchStar.Groups[1].Value.Trim('"', '\'')));
                }
                var match = Regex.Match(val, @"filename=""?([^"";]+)""?", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return SanitizeFileName(match.Groups[1].Value);
                }
            }
        }

        // 2. Parse URL path
        try
        {
            var uri = new Uri(url);
            var localPath = uri.AbsolutePath;
            var fileName = Path.GetFileName(localPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return SanitizeFileName(Uri.UnescapeDataString(fileName));
            }
        }
        catch { }

        // Fallback default
        return $"download_{DateTime.UtcNow:yyyyMMdd_HHmmss}.dat";
    }

    private static string DecodeRfc5987(string fileNameStar)
    {
        // Format: UTF-8''encoded_filename
        var parts = fileNameStar.Split("''", 2);
        return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : Uri.UnescapeDataString(fileNameStar);
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? "download.dat" : clean;
    }

    private static string GetUniqueFilePath(string fullPath)
    {
        // If the file has an active checkpoint, it's an existing resumable download
        if (File.Exists(fullPath + ".wdm-state")) return fullPath;
        if (!File.Exists(fullPath)) return fullPath;

        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);

        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
