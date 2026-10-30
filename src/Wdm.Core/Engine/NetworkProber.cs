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
        _httpClient = httpClient ?? HttpRequestHelper.CreateClient();
    }

    public async Task<DownloadMetadata> ProbeAsync(
        string url,
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default,
        DownloadRequestOptions? options = null)
    {
        var metadata = new DownloadMetadata
        {
            Url = url,
            Options = options
        };

        HttpResponseMessage? response = null;
        try
        {
            // 1. Try HEAD first; servers that reject it (403/405/...) get a 1-byte ranged GET instead
            response = await HttpRequestHelper.SendAsync(_httpClient, HttpMethod.Head, url, options, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                response = await HttpRequestHelper.SendAsync(_httpClient, HttpMethod.Get, url, options, new RangeHeaderValue(0, 0), cancellationToken);

                // 416 with "Content-Range: bytes */0" is how servers say "this file is empty"
                bool emptyFile = response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
                                 response.Content.Headers.ContentRange is { Length: 0 };
                if (!emptyFile) response.EnsureSuccessStatusCode();
            }

            // Read headers
            metadata.SupportsRanges = response.Headers.AcceptRanges.Contains("bytes") ||
                                     response.StatusCode == HttpStatusCode.PartialContent;

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                metadata.TotalBytes = 0;
                metadata.SupportsRanges = false;
            }
            else if (response.Content.Headers.ContentRange?.Length.HasValue == true)
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
            metadata.FileName = !string.IsNullOrWhiteSpace(options?.FileName)
                ? SanitizeFileName(options.FileName)
                : ExtractFileName(response, url);

            // Compute target file path
            var destDir = destinationDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            metadata.TargetFilePath = ReserveFilePath(Path.Combine(destDir, metadata.FileName), url);

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

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Strips path separators and invalid characters, Windows device names and trailing dots/spaces.</summary>
    public static string SanitizeFileName(string name)
    {
        // Windows rules on every platform: server-supplied names must not traverse paths wherever they are parsed
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();
        var clean = string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', ' ');
        if (clean.Length > 200) clean = clean[..200].TrimEnd('.', ' ');
        if (clean.Length == 0) return "download.dat";

        var stem = clean.Contains('.') ? clean[..clean.IndexOf('.')] : clean;
        return ReservedDeviceNames.Contains(stem) ? "_" + clean : clean;
    }

    /// <summary>
    /// Picks the file this download will write to and claims it so a concurrent download of the same
    /// name cannot take it. A path that holds a resumable checkpoint for the same URL is returned as-is.
    /// </summary>
    private static string ReserveFilePath(string fullPath, string url)
    {
        if (File.Exists(ResumeManager.GetCheckpointPath(fullPath)) && CheckpointUrl(fullPath) == url)
        {
            return fullPath;
        }

        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);

        for (int counter = 0; ; counter++)
        {
            string candidate = counter == 0 ? fullPath : Path.Combine(dir, $"{nameWithoutExt} ({counter}){ext}");

            if (File.Exists(candidate) || File.Exists(ResumeManager.GetCheckpointPath(candidate)))
            {
                if (counter > 0 && File.Exists(ResumeManager.GetCheckpointPath(candidate)) && CheckpointUrl(candidate) == url)
                {
                    return candidate;
                }
                continue;
            }

            try
            {
                using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Lost the race to another download: try the next name
            }
        }
    }

    private static string? CheckpointUrl(string targetFilePath) =>
        ResumeManager.LoadCheckpointAsync(targetFilePath).GetAwaiter().GetResult()?.Url;
}
