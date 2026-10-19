using System.Diagnostics;
using System.Text.RegularExpressions;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Stream resolver and handler for video platforms (YouTube, Vimeo, etc.).
/// Automatically integrates with the bundled resolver engine or browser extension sniffer.
/// </summary>
public static class MediaResolver
{
    private static readonly Regex YouTubeRegex = new(
        @"(?:https?:\/\/)?(?:www\.)?(?:youtube\.com\/(?:watch\?v=|shorts\/|embed\/)|youtu\.be\/)([a-zA-Z0-9_-]{11})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([\d\.]+)%\s+of\s+~?\s*([\d\.]+[A-Za-z]+)(?:\s+at\s+([\d\.]+[A-Za-z]+\/s))?(?:\s+ETA\s+(\d+:\d+))?",
        RegexOptions.Compiled
    );

    public static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return YouTubeRegex.IsMatch(url);
    }

    public static string? FindResolverExecutable()
    {
        string baseDir = AppContext.BaseDirectory;
        string localPath = Path.Combine(baseDir, "yt-dlp.exe");
        if (File.Exists(localPath)) return localPath;

        // Check common parent directories (like dist/WDM)
        string distPath = Path.Combine(baseDir, "..", "..", "..", "..", "dist", "WDM", "yt-dlp.exe");
        if (File.Exists(distPath)) return Path.GetFullPath(distPath);

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(dir, "yt-dlp.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }

    public static async Task DownloadVideoAsync(
        string videoUrl,
        string? destinationDir,
        Action<DownloadProgress> onProgress,
        Action<DownloadStatus> onStatusChanged,
        Action<DownloadMetadata> onMetadataLoaded,
        CancellationToken ct)
    {
        string? resolver = FindResolverExecutable();
        if (resolver == null)
        {
            throw new InvalidOperationException("YouTube resolver executable (yt-dlp.exe) was not found in application path.");
        }

        onStatusChanged(DownloadStatus.Connecting);

        var targetDir = destinationDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        // First probe video title
        string title = "youtube_video";
        try
        {
            var titlePsi = new ProcessStartInfo
            {
                FileName = resolver,
                Arguments = $"--no-warnings --get-title \"{videoUrl}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var titleProc = Process.Start(titlePsi);
            if (titleProc != null)
            {
                string? titleOut = await titleProc.StandardOutput.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(titleOut))
                {
                    title = NetworkProber.SanitizeFileName(titleOut.Trim());
                }
                await titleProc.WaitForExitAsync(ct);
            }
        }
        catch { }

        var meta = new DownloadMetadata
        {
            Url = videoUrl,
            FileName = $"{title}.mp4",
            TargetFilePath = Path.Combine(targetDir, $"{title}.mp4"),
            SupportsRanges = true,
            Status = DownloadStatus.Downloading
        };
        onMetadataLoaded(meta);
        onStatusChanged(DownloadStatus.Downloading);

        // Launch download process with progress parsing
        var psi = new ProcessStartInfo
        {
            FileName = resolver,
            Arguments = $"--no-warnings --newline -o \"{Path.Combine(targetDir, "%(title)s.%(ext)s")}\" \"{videoUrl}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        double currentPct = 0;
        double speedBps = 0;
        TimeSpan? eta = null;

        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
        {
            var match = ProgressRegex.Match(line);
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, out double p))
                {
                    currentPct = p;
                }

                if (match.Groups[3].Success)
                {
                    speedBps = ParseSpeed(match.Groups[3].Value);
                }

                if (match.Groups[4].Success && TimeSpan.TryParseExact(match.Groups[4].Value, @"m\:ss", null, out var parsedEta))
                {
                    eta = parsedEta;
                }

                long estTotal = meta.TotalBytes > 0 ? meta.TotalBytes : 100 * 1024 * 1024;
                long estDownloaded = (long)(estTotal * (currentPct / 100.0));

                var progress = new DownloadProgress(
                    TotalBytes: estTotal,
                    DownloadedBytes: estDownloaded,
                    SpeedBytesPerSecond: speedBps,
                    EstimatedTimeRemaining: eta,
                    Segments: Array.Empty<DownloadSegment>(),
                    Status: DownloadStatus.Downloading
                );

                onProgress(progress);
            }
        }

        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode == 0)
        {
            meta.Status = DownloadStatus.Completed;
            meta.CompletedAt = DateTimeOffset.UtcNow;
            onStatusChanged(DownloadStatus.Completed);

            onProgress(new DownloadProgress(
                TotalBytes: 100,
                DownloadedBytes: 100,
                SpeedBytesPerSecond: 0,
                EstimatedTimeRemaining: TimeSpan.Zero,
                Segments: Array.Empty<DownloadSegment>(),
                Status: DownloadStatus.Completed
            ));
        }
        else
        {
            meta.Status = DownloadStatus.Error;
            onStatusChanged(DownloadStatus.Error);
            throw new Exception("Video resolver exited with code " + proc.ExitCode);
        }
    }

    private static double ParseSpeed(string speedStr)
    {
        // e.g. "1.52MiB/s", "450KiB/s"
        speedStr = speedStr.Trim().Replace("/s", "", StringComparison.OrdinalIgnoreCase);
        if (speedStr.EndsWith("GiB", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(speedStr[..^3], out double g)) return g * 1024 * 1024 * 1024;
        }
        if (speedStr.EndsWith("MiB", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(speedStr[..^3], out double m)) return m * 1024 * 1024;
        }
        if (speedStr.EndsWith("KiB", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(speedStr[..^3], out double k)) return k * 1024;
        }
        return 0;
    }
}
