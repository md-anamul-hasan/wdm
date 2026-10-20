using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "WDM - Windows Download Manager (CLI)";

        if (args.Length == 0)
        {
            PrintBanner();
            Console.WriteLine("Usage: wdm <url> [-o <directory>] [-c <connections>] [-l <limit_KB/s>]");
            Console.WriteLine("\nExample: wdm https://speed.hetzner.de/100MB.bin -c 8\n");
            return 1;
        }

        string url = args[0];
        if (!Wdm.Core.Ipc.ExternalDownloadRequest.IsHttpUrl(url) && !MediaResolver.IsYouTubeUrl(url))
        {
            Console.Error.WriteLine($"[WDM Error] '{url}' is not an http(s) URL.");
            return 1;
        }

        string? outDir = null;
        int connections = 8;
        long limitBytesPerSec = 0;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outDir = args[++i];
            }
            else if ((args[i] == "-c" || args[i] == "--connections") && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int c)) connections = Math.Clamp(c, 1, 32);
            }
            else if ((args[i] == "-l" || args[i] == "--limit") && i + 1 < args.Length)
            {
                if (long.TryParse(args[++i], out long l)) limitBytesPerSec = l * 1024;
            }
        }

        PrintBanner();
        Console.WriteLine($"Initiating download from: {url}");
        Console.WriteLine($"Connections: {connections} | Speed Limit: {(limitBytesPerSec > 0 ? $"{limitBytesPerSec / 1024} KB/s" : "Unlimited")}");

        await using var engine = new DownloadEngine(maxBytesPerSecond: limitBytesPerSec);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[WDM] Pausing download and saving checkpoint...");
            engine.Pause();
            cts.Cancel();
        };

        engine.StatusChanged += status =>
        {
            if (status == DownloadStatus.Connecting)
            {
                Console.WriteLine("[WDM] Probing server headers and range capability...");
            }
            else if (status == DownloadStatus.Downloading && engine.Metadata != null)
            {
                Console.WriteLine($"[WDM] File: {engine.Metadata.FileName}");
                Console.WriteLine($"[WDM] Size: {(engine.Metadata.TotalBytes > 0 ? DownloadProgress.FormatBytes(engine.Metadata.TotalBytes) : "Unknown")}");
                Console.WriteLine($"[WDM] Multi-segment Ranges: {(engine.Metadata.SupportsRanges ? "YES" : "NO (Single Stream)")}");
                Console.WriteLine($"[WDM] Saving to: {engine.Metadata.TargetFilePath}\n");
            }
        };

        int lastRenderedLineCount = 0;

        engine.ProgressUpdated += progress =>
        {
            if (progress.Status != DownloadStatus.Downloading) return;

            // Clear previously rendered dynamic segment lines
            if (lastRenderedLineCount > 0 && !Console.IsOutputRedirected)
            {
                try
                {
                    Console.SetCursorPosition(0, Console.CursorTop - lastRenderedLineCount);
                }
                catch { }
            }

            var lines = new List<string>();

            // Overall progress bar
            int barWidth = 36;
            int filled = (int)(progress.Percentage / 100.0 * barWidth);
            string bar = new string('█', filled) + new string('░', barWidth - filled);

            string etaStr = progress.EstimatedTimeRemaining.HasValue
                ? $"{progress.EstimatedTimeRemaining.Value:mm\\:ss}"
                : "--:--";

            lines.Add($"Progress: [{bar}] {progress.Percentage,5:F1}% | {progress.FormattedDownloaded} | {progress.FormattedSpeed,10} | ETA: {etaStr}");

            // Segment details breakdown (WDM stream visualizer)
            var segments = progress.Segments;
            lines.Add($"Active Dynamic Segments ({segments.Count}):");

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                double segPct = seg.TotalBytes > 0 ? (double)seg.DownloadedBytes / seg.TotalBytes * 100 : 0;
                int segBarWidth = 16;
                int segFilled = (int)(segPct / 100.0 * segBarWidth);
                string segBar = new string('=', segFilled) + new string('-', segBarWidth - segFilled);

                string stateChar = seg.State switch
                {
                    SegmentState.Downloading => "▶",
                    SegmentState.Completed => "✔",
                    SegmentState.Connecting => "⏳",
                    _ => "⏸"
                };

                lines.Add($"  #{seg.SegmentId:D2} {stateChar} [{segBar}] {segPct,5:F1}% | {DownloadProgress.FormatBytes(seg.DownloadedBytes),8} / {DownloadProgress.FormatBytes(seg.TotalBytes),8} | {seg.SpeedBytesPerSecond / 1024.0,7:F1} KB/s");
            }

            int width = 80;
            try { if (!Console.IsOutputRedirected && Console.WindowWidth > 0) width = Console.WindowWidth; } catch { }

            foreach (var line in lines)
            {
                Console.WriteLine(line.PadRight(width > 1 ? width - 1 : 80));
            }

            lastRenderedLineCount = lines.Count;
        };

        try
        {
            await engine.StartAsync(url, outDir, connections, cts.Token);

            if (engine.Status != DownloadStatus.Completed)
            {
                Console.WriteLine("\n[WDM] Download paused. Checkpoint saved. Run again with the same URL to resume instantly.");
                return 0;
            }

            Console.WriteLine($"\n[WDM] Download 100% Complete! Saved to: {engine.Metadata?.TargetFilePath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[WDM] Download paused. Checkpoint saved. Run again with the same URL to resume instantly.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[WDM Error] {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════╗
║             WDM - Windows Download Manager                   ║
║   High-Performance Dynamic Multi-Segment Download Engine     ║
║        Zero-Merge Direct Storage • Full Resume Engine        ║
╚══════════════════════════════════════════════════════════════╝
");
        Console.ResetColor();
    }
}
