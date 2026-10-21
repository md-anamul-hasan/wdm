using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public partial class MainViewModel
{
    public void ConfirmAddDownload()
    {
        string url = NewDownloadUrl.Trim();
        if (!ExternalDownloadRequest.IsHttpUrl(url)) return;

        int connections = Math.Clamp(NewDownloadConnections, 1, 32);
        string? targetDir = string.IsNullOrWhiteSpace(NewDownloadDir) ? null : NewDownloadDir.Trim();

        var options = PendingOptions;
        AddDownload(url, targetDir ?? Settings.DefaultDownloadDirectory, connections, options);
        CloseAddModal();
    }

    public void AddDownload(string url, string? destinationDir = null, int connections = 8,
        DownloadRequestOptions? options = null, bool queueOnly = false)
    {
        // Same URL already in the list: reuse it instead of creating a duplicate row
        var existing = AllDownloads.FirstOrDefault(d => d.Url.Equals(url, StringComparison.OrdinalIgnoreCase) && !d.IsCompleted);
        if (existing != null)
        {
            SelectedDownload = existing;
            if (!queueOnly && !existing.IsRunning) ResumeItem(existing);
            return;
        }

        var item = new DownloadItemViewModel
        {
            Url = url,
            FileName = options?.FileName ?? GetInitialName(url),
            Status = "Queued",
            Options = options,
            ProgressPercentage = 0
        };

        AllDownloads.Insert(0, item);
        UpdateCounts();
        ApplyCategoryFilter();
        SelectedDownload = item;
        ScheduleSave();

        if (!queueOnly)
        {
            StartDownloadTask(item, destinationDir, connections);
        }
        else if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            item.TargetFilePath = Path.Combine(destinationDir, item.FileName);
        }
    }

    /// <summary>Entry point for downloads handed over by the browser extension, command line or another instance.</summary>
    public void HandleExternalRequest(ExternalDownloadRequest request)
    {
        if (request.IsEmpty) return;

        var options = request.ToOptions();
        var dir = request.DestinationDirectory;

        if (request.Urls.Count > 1)
        {
            if (!request.Silent && !request.QueueOnly && Settings.ShowStartDownloadDialog)
            {
                if (!string.IsNullOrWhiteSpace(dir)) NewDownloadDir = dir;
                OpenBatchModal(string.Join(Environment.NewLine, request.Urls), options);
                return;
            }

            foreach (var url in request.Urls)
            {
                AddDownload(url, dir ?? Settings.DefaultDownloadDirectory, Settings.DefaultConnections,
                    CloneSharedOptions(options), request.QueueOnly);
            }
            return;
        }

        var single = request.Urls[0];
        if (request.Silent || request.QueueOnly || !Settings.ShowStartDownloadDialog)
        {
            AddDownload(single, dir ?? Settings.DefaultDownloadDirectory, Settings.DefaultConnections, options, request.QueueOnly);
            return;
        }

        PendingOptions = options;
        if (!string.IsNullOrWhiteSpace(dir)) NewDownloadDir = dir;
        OpenAddModal(single, keepPending: true);
    }

    /// <summary>Copy of the browser context that is safe to share by many URLs (no per-file name).</summary>
    private static DownloadRequestOptions CloneSharedOptions(DownloadRequestOptions? source) => new()
    {
        Referer = source?.Referer,
        Cookies = source?.Cookies,
        CookieHost = source?.CookieHost,
        UserAgent = source?.UserAgent
    };

    /// <summary>Referer/cookies captured for the URL currently shown in the "add download" dialog.</summary>
    public DownloadRequestOptions? PendingOptions { get; private set; }

    /// <summary>
    /// Resumes a paused/errored download by reusing its existing slot in AllDownloads.
    /// </summary>
    public void ResumeItem(DownloadItemViewModel? item)
    {
        if (item == null || item.IsRunning) return;
        if (item.IsCompleted) return;

        string? dir = string.IsNullOrWhiteSpace(item.TargetFilePath)
            ? Settings.DefaultDownloadDirectory
            : Path.GetDirectoryName(item.TargetFilePath);

        item.Status = "Queued";
        item.SpeedRaw = 0;
        item.SpeedFormatted = "0 B/s";
        item.TimeLeftFormatted = "--:--";

        UpdateCounts();
        ApplyCategoryFilter();
        StartDownloadTask(item, dir, Settings.DefaultConnections);
    }

    private void StartDownloadTask(DownloadItemViewModel item, string? destinationDir, int connections)
    {
        var semaphore = _concurrencySemaphore;
        var cts = new CancellationTokenSource();
        item.RunCts = cts;
        string url = item.Url;

        item.RunTask = Task.Run(async () =>
        {
            bool slotHeld = false;
            DownloadEngine? engine = null;

            try
            {
                // Wait for a concurrency slot (still "Queued", and cancellable)
                if (semaphore != null)
                {
                    await semaphore.WaitAsync(cts.Token);
                    slotHeld = true;
                }

                engine = new DownloadEngine(_httpClient, speedLimiter: _speedLimiter)
                {
                    EnableDynamicSplit = Settings.EnableDynamicMiddleSplit,
                    NetworkTimeoutSeconds = Settings.NetworkTimeoutSeconds
                };

                var options = item.Options ?? new DownloadRequestOptions();
                options.UserAgent ??= Settings.UserAgent;
                item.Options = options;

                var startedEngine = engine;
                UiPost(() =>
                {
                    item.Engine = startedEngine;
                    item.LastTryDate = DateTime.Now.ToString("g");
                });

                engine.ProgressUpdated += progress =>
                {
                    UiPost(() =>
                    {
                        // A report queued just before Pause must not flip the row back to "Downloading"
                        if (cts.IsCancellationRequested) return;

                        item.ProgressPercentage = progress.Percentage;
                        item.TotalBytes = progress.TotalBytes;
                        item.SpeedFormatted = progress.FormattedSpeed;
                        item.SpeedRaw = progress.SpeedBytesPerSecond;
                        item.TimeLeftFormatted = progress.EstimatedTimeRemaining.HasValue
                            ? $"{progress.EstimatedTimeRemaining.Value:hh\\:mm\\:ss}"
                            : "--:--";
                        item.SizeFormatted = progress.FormattedDownloaded;
                        item.Status = progress.Status.ToString();
                        item.Segments = progress.Segments.ToList();
                        UpdateAggregateSpeed();
                    });
                };

                engine.StatusChanged += status =>
                {
                    UiPost(() =>
                    {
                        item.Status = status.ToString();
                        var meta = startedEngine.Metadata;
                        if (meta != null)
                        {
                            if (!string.IsNullOrEmpty(meta.FileName)) item.FileName = meta.FileName;
                            if (!string.IsNullOrEmpty(meta.TargetFilePath)) item.TargetFilePath = meta.TargetFilePath;
                            if (meta.TotalBytes > 0) item.TotalBytes = meta.TotalBytes;
                        }
                        OnItemStatusChanged(item);
                    });
                };

                await engine.StartAsync(url, destinationDir, connectionCount: connections, cts.Token, options);
            }
            catch (OperationCanceledException)
            {
                // Pause is normal; ensure the row reflects it even if cancelled before the engine started
                UiPost(() =>
                {
                    if (!item.IsCompleted && !item.IsError) item.Status = "Paused";
                    OnItemStatusChanged(item);
                });
            }
            catch (Exception ex)
            {
                UiPost(() =>
                {
                    item.Status = "Error: " + ex.Message;
                    item.LastFailureAt = DateTime.Now;
                    item.RetryCount++;
                    OnItemStatusChanged(item);
                });
            }
            finally
            {
                if (slotHeld) semaphore?.Release();
                if (engine != null) await engine.DisposeAsync();
                UiPost(() =>
                {
                    item.Engine = null;
                    item.SpeedRaw = 0;
                    if (!item.IsCompleted) item.SpeedFormatted = "0 B/s";
                    UpdateAggregateSpeed();
                });
            }
        });
    }

    private void OnItemStatusChanged(DownloadItemViewModel item)
    {
        if (item.IsCompleted)
        {
            item.ProgressPercentage = 100;
            item.RetryCount = 0;
            item.TimeLeftFormatted = "--:--";
            if (Settings.AutoOpenFileOnComplete && _openedOnComplete.Add(item.Id))
            {
                OpenFile(item);
            }
        }

        UpdateCounts();
        ApplyCategoryFilter();
        UpdateAggregateSpeed();
        ScheduleSave();
        CheckQueueFinished();
    }

    private readonly HashSet<string> _openedOnComplete = new();

    public void PauseSelected()
    {
        PauseItem(SelectedDownload);
    }

    public void ResumeSelected()
    {
        ResumeItem(SelectedDownload);
    }

    public void DeleteSelected()
    {
        DeleteItem(SelectedDownload);
    }

    public void StopAll()
    {
        foreach (var item in AllDownloads.Where(d => d.IsRunning).ToList())
        {
            PauseItem(item);
        }
    }

    public void DeleteCompleted()
    {
        var completedList = AllDownloads.Where(d => d.IsCompleted).ToList();
        foreach (var item in completedList)
        {
            DeleteItem(item);
        }
    }

    // --- Redownload / Restart ---
    public void RedownloadItem(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        var running = item.RunTask;
        item.RunCts?.Cancel();
        item.Engine?.Pause();

        _ = Task.Run(async () =>
        {
            if (running != null) { try { await running; } catch { } }

            // Discard the old file and checkpoint so the engine starts from byte zero
            DiscardPartialFile(await ReadTargetPathAsync(item));
            UiPost(() =>
            {
                item.ProgressPercentage = 0;
                item.SizeFormatted = "0 B";
                item.TotalBytes = -1;
                item.Status = "Paused";
                ResumeItem(item);
            });
        });
    }

    /// <summary>Reads the row's target path after every UI update already queued by the finished download has run.</summary>
    private static Task<string> ReadTargetPathAsync(DownloadItemViewModel item) =>
        Application.Current is { } app
            ? app.Dispatcher.InvokeAsync(() => item.TargetFilePath).Task
            : Task.FromResult(item.TargetFilePath);

    private static void DiscardPartialFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            ResumeManager.DeleteCheckpoint(path);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // --- Delete File From Disk ---
    public void DeleteFileFromDisk(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        var path = item.TargetFilePath;
        var running = item.RunTask;
        item.RunCts?.Cancel();
        item.Engine?.Pause();

        _ = Task.Run(async () =>
        {
            if (running != null) { try { await running; } catch { } }
            DiscardPartialFile(string.IsNullOrEmpty(path) ? await ReadTargetPathAsync(item) : path);
        });

        item.Status = "Paused"; // prevents DeleteItem from scheduling a second cleanup
        item.TargetFilePath = string.Empty;
        DeleteItem(item);
    }

    public void PauseItem(DownloadItemViewModel? item)
    {
        if (item == null) return;

        if (item.IsRunning)
        {
            item.RunCts?.Cancel();
            item.Engine?.Pause();
        }

        if (!item.IsCompleted)
        {
            item.Status = "Paused";
            item.SpeedRaw = 0;
            item.SpeedFormatted = "0 B/s";
        }

        UpdateAggregateSpeed();
        UpdateCounts();
        ApplyCategoryFilter();
        ScheduleSave();
    }

    /// <summary>Removes the row. Unfinished downloads also lose their partial file and checkpoint.</summary>
    public void DeleteItem(DownloadItemViewModel? item)
    {
        if (item == null) return;

        item.RunCts?.Cancel();
        item.Engine?.Pause();

        if (!item.IsCompleted)
        {
            // The target path may only become known once the running engine has stopped
            var running = item.RunTask;
            _ = Task.Run(async () =>
            {
                if (running != null) { try { await running; } catch { } }
                DiscardPartialFile(await ReadTargetPathAsync(item));
            });
        }

        AllDownloads.Remove(item);
        UpdateCounts();
        ApplyCategoryFilter();
        UpdateAggregateSpeed();
        ScheduleSave();
        if (SelectedDownload == item)
        {
            SelectedDownload = FilteredDownloads.FirstOrDefault();
        }
    }

    public void OpenFileLocation(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        string path = item.TargetFilePath;
        if (File.Exists(path))
        {
            StartExplorer($"/select,\"{path}\"");
        }
        else
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                StartExplorer($"\"{dir}\"");
            }
        }
    }

    private static void StartExplorer(string arguments)
    {
        try { Process.Start("explorer.exe", arguments); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public void OpenFile(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        string path = item.TargetFilePath;
        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void UpdateSpeedLimits()
    {
        _speedLimiter.MaxBytesPerSecond = IsSpeedLimiterEnabled ? (long)SpeedLimitKb * 1024 : 0;
    }

    private void UpdateAggregateSpeed()
    {
        double total = AllDownloads.Where(d => d.IsActive).Sum(d => d.SpeedRaw);
        TotalSpeedFormatted = DownloadProgress.FormatBytes((long)total) + "/s";
        ActiveDownloadsCount = AllDownloads.Count(d => d.IsActive);
        OnPropertyChanged(nameof(WindowTitle));
        NotifySelectionState();
    }

    private static string GetInitialName(string url)
    {
        try
        {
            if (MediaResolver.IsYouTubeUrl(url)) return "Resolving video stream...";
            var uri = new Uri(url);
            var name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            return string.IsNullOrWhiteSpace(name) ? "download.dat" : NetworkProber.SanitizeFileName(name);
        }
        catch
        {
            return "download.dat";
        }
    }
}
