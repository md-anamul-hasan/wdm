using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Stream resolver for video platforms (YouTube). Delegates to the bundled yt-dlp executable and
/// translates its progress output into <see cref="DownloadProgress"/> updates.
/// </summary>
public static class MediaResolver
{
    private const string TitlePrefix = "WDMTITLE:";
    private const string FilePrefix = "WDMFILE:";

    private static readonly Regex YouTubeRegex = new(
        @"^(?:https?://)?(?:www\.|m\.)?(?:youtube\.com/(?:watch\?(?:[^#]*&)?v=|shorts/|embed/)|youtu\.be/)[a-zA-Z0-9_-]{11}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+[A-Za-z]+)(?:\s+at\s+([\d.]+[A-Za-z]+/s))?(?:\s+ETA\s+(\d+(?::\d+){1,2}))?",
        RegexOptions.Compiled
    );

    public static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return YouTubeRegex.IsMatch(url.Trim());
    }

    public static string? FindResolverExecutable() => FindOnPathOrBeside("yt-dlp.exe", null);

    private static string? FindOnPathOrBeside(string exeName, string? besideExecutable)
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, exeName) };
        if (besideExecutable != null) candidates.Add(Path.Combine(Path.GetDirectoryName(besideExecutable) ?? "", exeName));

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir, exeName)); }
            catch (ArgumentException) { }
        }

        return candidates.FirstOrDefault(File.Exists);
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
            throw new InvalidOperationException("The video resolver (yt-dlp.exe) was not found next to WDM or on PATH.");
        }

        onStatusChanged(DownloadStatus.Connecting);

        var targetDir = destinationDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(targetDir);

        var meta = new DownloadMetadata
        {
            Url = videoUrl,
            FileName = "YouTube video",
            SupportsRanges = false,
            Status = DownloadStatus.Connecting
        };

        var psi = new ProcessStartInfo
        {
            FileName = resolver,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Separate video+audio streams can only be joined when ffmpeg is available; otherwise ask for a single-file format
        bool hasFfmpeg = FindOnPathOrBeside("ffmpeg.exe", resolver) != null;
        var formatArgs = hasFfmpeg
            ? new[] { "--merge-output-format", "mp4" }
            : new[] { "-f", "b[ext=mp4]/b" };

        foreach (var arg in formatArgs.Concat(new[]
        {
            "--encoding", "utf-8", // redirected output otherwise degrades non-ASCII titles to "?", and the printed file path stops matching the file
            "--no-warnings", "--no-playlist", "--newline", "--progress", "--no-simulate",
            "--print", "before_dl:" + TitlePrefix + "%(title)s",
            "--print", "after_move:" + FilePrefix + "%(filepath)s",
            "-o", Path.Combine(targetDir, "%(title)s.%(ext)s"),
            "--", videoUrl
        }))
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = new Process { StartInfo = psi };
        var errorTail = new Queue<string>();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (errorTail)
            {
                errorTail.Enqueue(e.Data.Trim());
                if (errorTail.Count > 8) errorTail.Dequeue();
            }
        };

        proc.Start();
        proc.BeginErrorReadLine();

        // yt-dlp must never outlive a pause, delete or application exit
        using var killOnCancel = ct.Register(() => TryKill(proc));

        long totalBytes = 0;
        long finishedStreamsBytes = 0; // video and audio arrive as separate streams, each reporting 0-100%
        double speedBps = 0;
        TimeSpan? eta = null;
        string? finalPath = null;
        bool metadataPublished = false;

        try
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
            {
                if (line.StartsWith(TitlePrefix, StringComparison.Ordinal))
                {
                    var title = NetworkProber.SanitizeFileName(line[TitlePrefix.Length..].Trim());
                    meta.FileName = title;
                    meta.TargetFilePath = Path.Combine(targetDir, title);
                    continue;
                }

                if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
                {
                    finalPath = line[FilePrefix.Length..].Trim();
                    continue;
                }

                if (line.StartsWith("[download] Destination:", StringComparison.Ordinal))
                {
                    finishedStreamsBytes += totalBytes;
                    totalBytes = 0;
                    continue;
                }

                var match = ProgressRegex.Match(line);
                if (!match.Success) continue;

                if (!metadataPublished)
                {
                    metadataPublished = true;
                    meta.Status = DownloadStatus.Downloading;
                    onMetadataLoaded(meta);
                    onStatusChanged(DownloadStatus.Downloading);
                }

                double pct = double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
                totalBytes = (long)ParseBytes(match.Groups[2].Value);
                if (match.Groups[3].Success) speedBps = ParseBytes(match.Groups[3].Value.Replace("/s", "", StringComparison.OrdinalIgnoreCase));
                eta = match.Groups[4].Success ? ParseEta(match.Groups[4].Value) : null;

                meta.TotalBytes = finishedStreamsBytes + totalBytes;
                onProgress(new DownloadProgress(
                    TotalBytes: finishedStreamsBytes + totalBytes,
                    DownloadedBytes: finishedStreamsBytes + (long)(totalBytes * pct / 100.0),
                    SpeedBytesPerSecond: speedBps,
                    EstimatedTimeRemaining: eta,
                    Segments: Array.Empty<DownloadSegment>(),
                    Status: DownloadStatus.Downloading));
            }

            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        if (proc.ExitCode != 0)
        {
            string detail;
            lock (errorTail) detail = string.Join(" ", errorTail);
            meta.Status = DownloadStatus.Error;
            onStatusChanged(DownloadStatus.Error);
            if (string.IsNullOrWhiteSpace(detail)) detail = $"Video resolver exited with code {proc.ExitCode}.";
            if (!hasFfmpeg) detail += " (Most videos need ffmpeg: place ffmpeg.exe next to yt-dlp.exe or on PATH.)";
            throw new InvalidOperationException(detail);
        }

        if (string.IsNullOrEmpty(finalPath) || !File.Exists(finalPath))
        {
            meta.Status = DownloadStatus.Error;
            onStatusChanged(DownloadStatus.Error);
            throw new FileNotFoundException("The video resolver finished but its output file was not found.", finalPath);
        }

        meta.TargetFilePath = finalPath;
        meta.FileName = Path.GetFileName(finalPath);
        totalBytes = new FileInfo(finalPath).Length;

        meta.TotalBytes = totalBytes;
        meta.Status = DownloadStatus.Completed;
        meta.CompletedAt = DateTimeOffset.UtcNow;
        onMetadataLoaded(meta);
        onStatusChanged(DownloadStatus.Completed);
        onProgress(new DownloadProgress(
            TotalBytes: totalBytes,
            DownloadedBytes: totalBytes,
            SpeedBytesPerSecond: 0,
            EstimatedTimeRemaining: TimeSpan.Zero,
            Segments: Array.Empty<DownloadSegment>(),
            Status: DownloadStatus.Completed));
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static TimeSpan? ParseEta(string eta)
    {
        var parts = eta.Split(':');
        if (!parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return null;

        var n = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return n.Length == 3 ? new TimeSpan(n[0], n[1], n[2]) : new TimeSpan(0, n[0], n[1]);
    }

    /// <summary>Parses yt-dlp sizes such as "12.50MiB" or "450KiB" into bytes.</summary>
    public static double ParseBytes(string value)
    {
        value = value.Trim();
        int split = value.Length;
        while (split > 0 && char.IsLetter(value[split - 1])) split--;

        if (!double.TryParse(value[..split], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return 0;

        double scale = value[split..].ToUpperInvariant() switch
        {
            "KIB" or "KB" => 1024d,
            "MIB" or "MB" => 1024d * 1024,
            "GIB" or "GB" => 1024d * 1024 * 1024,
            "TIB" or "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return number * scale;
    }
}
