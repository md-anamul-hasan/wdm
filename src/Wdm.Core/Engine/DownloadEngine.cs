using System.Diagnostics;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// WDM core orchestration engine: multi-connection dynamic-segmentation download pipeline
/// with middle-split load balancing and zero-merge direct file writes.
/// </summary>
public class DownloadEngine : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SpeedLimiter _speedLimiter;
    private readonly DynamicSegmenter _segmenter;
    private readonly NetworkProber _prober;
    private DownloadMetadata? _metadata;
    private DownloadRequestOptions? _options;
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

    /// <summary>When false, idle connections never split an active segment (static segmentation only).</summary>
    public bool EnableDynamicSplit { get; set; } = true;

    /// <summary>Seconds without receiving data before a connection is considered stalled and retried.</summary>
    public int NetworkTimeoutSeconds { get; set; } = 30;
    public SpeedLimiter SpeedLimiter => _speedLimiter;
    public DynamicSegmenter Segmenter => _segmenter;
    public DownloadStatus Status => _metadata?.Status ?? DownloadStatus.Queued;

    /// <param name="httpClient">Shared client; when null the engine creates (and disposes) its own.</param>
    /// <param name="maxBytesPerSecond">Per-engine limit, ignored when <paramref name="speedLimiter"/> is supplied.</param>
    /// <param name="speedLimiter">Limiter shared with other engines to enforce one global limit.</param>
    public DownloadEngine(HttpClient? httpClient = null, long maxBytesPerSecond = 0, SpeedLimiter? speedLimiter = null)
    {
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? HttpRequestHelper.CreateClient();
        _speedLimiter = speedLimiter ?? new SpeedLimiter(maxBytesPerSecond);
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
        CancellationToken cancellationToken = default,
        DownloadRequestOptions? options = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        _options = options;

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
        DownloadMetadata probe;
        DownloadMetadata? existing;
        try
        {
            probe = await _prober.ProbeAsync(url, destinationDirectory, ct, options);
            existing = await ResumeManager.LoadCheckpointAsync(probe.TargetFilePath, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            SetStatus(DownloadStatus.Paused);
            throw;
        }

        // Only servers that honour Range can resume; unknown-length downloads and a validator missing on both sides prove nothing
        bool sameFile = existing != null && existing.Url == url && existing.TotalBytes == probe.TotalBytes && existing.TotalBytes > 0 &&
            probe.SupportsRanges &&
            (probe.ETag != null ? existing.ETag == probe.ETag : probe.LastModified.HasValue && existing.LastModified == probe.LastModified);

        if (existing != null && sameFile)
        {
            // Resume existing download!
            _metadata = existing;
            _metadata.Options = options ?? existing.Options;
            _options = _metadata.Options;
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

        // Persist right away so the file reserved by the probe is always tied to this URL
        await ResumeManager.SaveCheckpointAsync(_metadata, CancellationToken.None);

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
            // Launch worker pool; the first worker failure cancels its siblings so the error surfaces promptly
            using var workersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            int actualConnections = _metadata is { SupportsRanges: true, TotalBytes: > 0 } ? connectionCount : 1;
            for (int i = 0; i < actualConnections; i++)
            {
                workers.Add(Task.Run(async () =>
                {
                    try
                    {
                        await WorkerLoopAsync(workersCts.Token);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        workersCts.Cancel();
                        throw;
                    }
                }, CancellationToken.None));
            }

            try
            {
                await Task.WhenAll(workers);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // A sibling worker failed: surface its real exception instead of the cancellation
                var failure = workers.Select(w => w.Exception?.InnerException).FirstOrDefault(x => x is not null and not OperationCanceledException);
                if (failure != null) throw failure;
                throw;
            }

            // Stop reporting task before finalizing checkpoint
            reportingCts.Cancel();
            try { await reportingTask; } catch { }

            // Final check
            if (!_segmenter.Segments.All(s => s.IsCompleted))
            {
                throw new IOException("The download stopped before every segment finished.");
            }

            if (_metadata != null)
            {
                // Size was unknown up front: it is whatever the server delivered
                if (_metadata.TotalBytes <= 0)
                {
                    _metadata.TotalBytes = Interlocked.Read(ref _totalDownloadedBytes);
                    _storageWriter?.SetLength(_metadata.TotalBytes);
                }
                _metadata.CompletedAt = DateTimeOffset.UtcNow;
                ResumeManager.DeleteCheckpoint(_metadata.TargetFilePath);
            }
            SetStatus(DownloadStatus.Completed);
            UpdateMetrics();
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
            },
            _options,
            TimeSpan.FromSeconds(Math.Max(5, NetworkTimeoutSeconds))
        );

        while (!ct.IsCancellationRequested)
        {
            DownloadSegment? segment = null;

            // 1. Try to get an idle segment
            segment = _segmenter.GetNextIdleSegment();

            // 2. If no idle segments, try dynamic middle-split from largest active segment
            if (segment == null && _metadata.SupportsRanges && _metadata.TotalBytes > 0 && EnableDynamicSplit)
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
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
