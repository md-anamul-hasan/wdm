using System.Diagnostics;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Core orchestration engine replicating IDM's multi-connection dynamic segmentation download pipeline.
/// </summary>
public class DownloadEngine : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SpeedLimiter _speedLimiter;
    private readonly DynamicSegmenter _segmenter;
    private readonly NetworkProber _prober;
    private DownloadMetadata? _metadata;
    private StorageWriter? _storageWriter;
    private CancellationTokenSource? _cts;
    private Task? _orchestratorTask;
    private long _totalDownloadedBytes;
    private double _currentSpeedBytesPerSecond;
    private long _lastSpeedCheckTicks;
    private long _bytesInCurrentWindow;

    public event Action<DownloadProgress>? ProgressUpdated;
    public event Action<DownloadStatus>? StatusChanged;

    public DownloadMetadata? Metadata => _metadata;
    public SpeedLimiter SpeedLimiter => _speedLimiter;
    public DynamicSegmenter Segmenter => _segmenter;
    public DownloadStatus Status => _metadata?.Status ?? DownloadStatus.Queued;

    public DownloadEngine(HttpClient? httpClient = null, long maxBytesPerSecond = 0)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 64
        });
        _speedLimiter = new SpeedLimiter(maxBytesPerSecond);
        _segmenter = new DynamicSegmenter();
        _prober = new NetworkProber(_httpClient);
    }

    /// <summary>
    /// Starts or resumes a download task.
    /// </summary>
    public async Task StartAsync(
        string url,
        string? destinationDirectory = null,
        int connectionCount = 8,
        CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        // Check if URL is a YouTube/video platform URL
        if (MediaResolver.IsYouTubeUrl(url))
        {
            await MediaResolver.DownloadVideoAsync(
                url,
                destinationDirectory,
                progress => ProgressUpdated?.Invoke(progress),
                status => SetStatus(status),
                meta => _metadata = meta,
                ct
            );
            return;
        }

        SetStatus(DownloadStatus.Connecting);

        // Check if an existing checkpoint exists for this URL
        var probe = await _prober.ProbeAsync(url, destinationDirectory, ct);
        var existing = await ResumeManager.LoadCheckpointAsync(probe.TargetFilePath, ct);

        if (existing != null && existing.Url == url && existing.TotalBytes == probe.TotalBytes &&
            (existing.ETag == probe.ETag || existing.LastModified == probe.LastModified))
        {
            // Resume existing download!
            _metadata = existing;
            _segmenter.Restore(_metadata.Segments);
        }
        else
        {
            // New download
            _metadata = probe;
            _metadata.Segments = _segmenter.Initialize(_metadata.TotalBytes, connectionCount, _metadata.SupportsRanges);
        }

        // Initialize storage writer
        _storageWriter = new StorageWriter(_metadata.TargetFilePath, _metadata.TotalBytes);

        // Calculate already downloaded bytes
        _totalDownloadedBytes = _segmenter.Segments.Sum(s => s.DownloadedBytes);

        SetStatus(DownloadStatus.Downloading);

        _orchestratorTask = RunDownloadLoopAsync(connectionCount, ct);
        await _orchestratorTask;
    }

    private async Task RunDownloadLoopAsync(int connectionCount, CancellationToken ct)
    {
        _lastSpeedCheckTicks = Stopwatch.GetTimestamp();
        var workers = new List<Task>();

        using var reportingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var checkpointTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        var reportingTask = Task.Run(async () =>
        {
            try
            {
                while (!reportingCts.Token.IsCancellationRequested && await checkpointTimer.WaitForNextTickAsync(reportingCts.Token).ConfigureAwait(false))
                {
                    UpdateMetrics();
                    if (_metadata != null && Status != DownloadStatus.Completed)
                    {
                        await ResumeManager.SaveCheckpointAsync(_metadata, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, reportingCts.Token);

        try
        {
            // Launch worker pool
            int actualConnections = _metadata?.SupportsRanges == true ? connectionCount : 1;
            for (int i = 0; i < actualConnections; i++)
            {
                workers.Add(Task.Run(() => WorkerLoopAsync(ct), ct));
            }

            await Task.WhenAll(workers);

            // Stop reporting task before finalizing checkpoint
            reportingCts.Cancel();
            try { await reportingTask; } catch { }

            // Final check
            if (_segmenter.Segments.All(s => s.IsCompleted))
            {
                SetStatus(DownloadStatus.Completed);
                if (_metadata != null)
                {
                    _metadata.CompletedAt = DateTimeOffset.UtcNow;
                    ResumeManager.DeleteCheckpoint(_metadata.TargetFilePath);
                }
                UpdateMetrics();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(DownloadStatus.Paused);
            if (_metadata != null)
            {
                await ResumeManager.SaveCheckpointAsync(_metadata, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            SetStatus(DownloadStatus.Error);
            if (_metadata != null)
            {
                _metadata.ErrorMessage = ex.Message;
                await ResumeManager.SaveCheckpointAsync(_metadata, CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (_storageWriter != null)
            {
                await _storageWriter.DisposeAsync();
                _storageWriter = null;
            }
        }
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        if (_metadata == null || _storageWriter == null) return;

        var worker = new HttpConnectionWorker(
            _httpClient,
            _storageWriter,
            _speedLimiter,
            onBytesRead: bytes =>
            {
                Interlocked.Add(ref _totalDownloadedBytes, bytes);
                Interlocked.Add(ref _bytesInCurrentWindow, bytes);
            }
        );

        while (!ct.IsCancellationRequested)
        {
            DownloadSegment? segment = null;

            // 1. Try to get an idle segment
            segment = _segmenter.GetNextIdleSegment();

            // 2. If no idle segments, try dynamic IDM split from largest active segment
            if (segment == null && _metadata.SupportsRanges)
            {
                segment = _segmenter.TrySplitLargestActiveSegment();
            }

            if (segment == null)
            {
                // No more work available right now
                // Wait briefly in case another active segment grows large enough to split or finishes
                await Task.Delay(100, ct);

                if (_segmenter.Segments.All(s => s.IsCompleted))
                {
                    break;
                }
                continue;
            }

            await worker.DownloadSegmentAsync(_metadata.Url, segment, ct);
        }
    }

    private void UpdateMetrics()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(_lastSpeedCheckTicks, now).TotalSeconds;

        if (elapsedSeconds > 0)
        {
            long bytes = Interlocked.Exchange(ref _bytesInCurrentWindow, 0);
            _currentSpeedBytesPerSecond = bytes / elapsedSeconds;
            _lastSpeedCheckTicks = now;
        }

        long total = _metadata?.TotalBytes ?? -1;
        long downloaded = Interlocked.Read(ref _totalDownloadedBytes);
        long remaining = total > 0 ? Math.Max(0, total - downloaded) : 0;

        TimeSpan? eta = (_currentSpeedBytesPerSecond > 0 && remaining > 0)
            ? TimeSpan.FromSeconds(remaining / _currentSpeedBytesPerSecond)
            : null;

        var progress = new DownloadProgress(
            TotalBytes: total,
            DownloadedBytes: downloaded,
            SpeedBytesPerSecond: _currentSpeedBytesPerSecond,
            EstimatedTimeRemaining: eta,
            Segments: _segmenter.Segments,
            Status: Status
        );

        ProgressUpdated?.Invoke(progress);
    }

    public void Pause()
    {
        _cts?.Cancel();
    }

    private void SetStatus(DownloadStatus status)
    {
        if (_metadata != null)
        {
            _metadata.Status = status;
        }
        StatusChanged?.Invoke(status);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_orchestratorTask != null)
        {
            try { await _orchestratorTask; } catch { }
        }
        if (_storageWriter != null)
        {
            await _storageWriter.DisposeAsync();
        }
        _cts?.Dispose();
    }
}
