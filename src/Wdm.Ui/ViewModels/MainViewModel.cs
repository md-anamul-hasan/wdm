using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _selectedCategory = "All Downloads";
    private DownloadItemViewModel? _selectedDownload;
    private bool _isSpeedLimiterEnabled;
    private int _speedLimitKb = 4096; // 4 MB/s default
    private string _searchText = string.Empty;

    // Header aggregate stats
    private string _totalSpeedFormatted = "0 B/s";
    private int _activeDownloadsCount = 0;
    private bool _isSegmentPanelVisible = true;

    // Modal dialog properties
    private bool _isAddModalOpen;
    private string _newDownloadUrl = string.Empty;
    private string _newDownloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private int _newDownloadConnections = 8;

    // Concurrency semaphore to enforce MaxConcurrentDownloads. Replaced (never disposed) when the limit changes,
    // because running and queued downloads still hold the instance they started with.
    private SemaphoreSlim? _concurrencySemaphore;
    private int _concurrencyLimit;

    // One client and one limiter serve every download, so "total speed" really is total
    private readonly HttpClient _httpClient = HttpRequestHelper.CreateClient();
    private readonly SpeedLimiter _speedLimiter = new();
    private AppSettings? _settingsSnapshot;

    private readonly DownloadStore _store = new();
    private DispatcherTimer? _saveTimer;
    private bool _shutdownPending;

    public ObservableCollection<DownloadItemViewModel> AllDownloads { get; } = new();
    public ObservableCollection<DownloadItemViewModel> FilteredDownloads { get; } = new();

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                ApplyCategoryFilter();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ApplyCategoryFilter();
            }
        }
    }

    public DownloadItemViewModel? SelectedDownload
    {
        get => _selectedDownload;
        set
        {
            if (SetField(ref _selectedDownload, value))
            {
                NotifySelectionState();
            }
        }
    }

    public bool HasSelection => SelectedDownload != null;
    public bool CanResumeSelected => SelectedDownload is { IsCompleted: false, IsRunning: false };
    public bool CanPauseSelected => SelectedDownload is { IsCompleted: false } s && (s.IsRunning || s.IsQueued);

    private void NotifySelectionState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanResumeSelected));
        OnPropertyChanged(nameof(CanPauseSelected));
    }

    public string WindowTitle => ActiveDownloadsCount > 0 ? $"{TotalSpeedFormatted} - WDM" : "WDM - Windows Download Manager";

    public bool IsLibraryEmpty => AllDownloads.Count == 0;

    public string TotalSpeedFormatted
    {
        get => _totalSpeedFormatted;
        set => SetField(ref _totalSpeedFormatted, value);
    }

    public int ActiveDownloadsCount
    {
        get => _activeDownloadsCount;
        set => SetField(ref _activeDownloadsCount, value);
    }

    public bool IsSegmentPanelVisible
    {
        get => _isSegmentPanelVisible;
        set => SetField(ref _isSegmentPanelVisible, value);
    }

    public bool IsSpeedLimiterEnabled
    {
        get => _isSpeedLimiterEnabled;
        set
        {
            if (SetField(ref _isSpeedLimiterEnabled, value))
            {
                Settings.IsSpeedLimiterEnabled = value;
                UpdateSpeedLimits();
                PersistOutsideSettingsDialog();
            }
        }
    }

    public int SpeedLimitKb
    {
        get => _speedLimitKb;
        set
        {
            if (SetField(ref _speedLimitKb, Math.Max(1, value)))
            {
                Settings.DefaultSpeedLimitKb = _speedLimitKb;
                UpdateSpeedLimits();
                PersistOutsideSettingsDialog();
            }
        }
    }

    // Modal bindings
    public bool IsAddModalOpen
    {
        get => _isAddModalOpen;
        set => SetField(ref _isAddModalOpen, value);
    }

    public string NewDownloadUrl
    {
        get => _newDownloadUrl;
        set
        {
            if (SetField(ref _newDownloadUrl, value))
            {
                OnPropertyChanged(nameof(IsYouTubeUrl));
                OnPropertyChanged(nameof(IsNewDownloadUrlValid));
                OnPropertyChanged(nameof(ShowUrlError));
            }
        }
    }

    public bool IsYouTubeUrl => MediaResolver.IsYouTubeUrl(NewDownloadUrl);

    public bool ShowUrlError => !string.IsNullOrWhiteSpace(NewDownloadUrl) && !IsNewDownloadUrlValid;

    public bool IsNewDownloadUrlValid => ExternalDownloadRequest.IsHttpUrl(NewDownloadUrl.Trim());

    public AppSettings Settings { get; } = AppSettings.Load();
    private bool _isSettingsModalOpen;

    public bool IsSettingsModalOpen
    {
        get => _isSettingsModalOpen;
        set => SetField(ref _isSettingsModalOpen, value);
    }

    public string NewDownloadDir
    {
        get => _newDownloadDir;
        set => SetField(ref _newDownloadDir, value);
    }

    public int NewDownloadConnections
    {
        get => _newDownloadConnections;
        set => SetField(ref _newDownloadConnections, value);
    }

    // Queue status
    private bool _isQueueActive;
    public bool IsQueueActive
    {
        get => _isQueueActive;
        set
        {
            if (SetField(ref _isQueueActive, value))
            {
                OnPropertyChanged(nameof(QueueStatusText));
            }
        }
    }
    public string QueueStatusText => IsQueueActive ? "● Queue Active" : "Queue Idle";

    // Scheduler Modal Properties
    private bool _isSchedulerModalOpen;
    public bool IsSchedulerModalOpen
    {
        get => _isSchedulerModalOpen;
        set => SetField(ref _isSchedulerModalOpen, value);
    }

    private bool _schedulerEnabled;
    public bool SchedulerEnabled
    {
        get => _schedulerEnabled;
        set => SetField(ref _schedulerEnabled, value);
    }

    private string _schedulerStartTime = "02:00";
    public string SchedulerStartTime
    {
        get => _schedulerStartTime;
        set => SetField(ref _schedulerStartTime, value);
    }

    private string _schedulerStopTime = "06:00";
    public string SchedulerStopTime
    {
        get => _schedulerStopTime;
        set => SetField(ref _schedulerStopTime, value);
    }

    private bool _schedulerShutdownOnComplete;
    public bool SchedulerShutdownOnComplete
    {
        get => _schedulerShutdownOnComplete;
        set => SetField(ref _schedulerShutdownOnComplete, value);
    }

    // Site / Media Grabber Properties
    private bool _isGrabberModalOpen;
    public bool IsGrabberModalOpen
    {
        get => _isGrabberModalOpen;
        set => SetField(ref _isGrabberModalOpen, value);
    }

    private string _grabberUrl = string.Empty;
    public string GrabberUrl
    {
        get => _grabberUrl;
        set => SetField(ref _grabberUrl, value);
    }

    private bool _isGrabberBusy;
    public bool IsGrabberBusy
    {
        get => _isGrabberBusy;
        set => SetField(ref _isGrabberBusy, value);
    }

    private string _grabberStatusText = string.Empty;
    public string GrabberStatusText
    {
        get => _grabberStatusText;
        set => SetField(ref _grabberStatusText, value);
    }

    public ObservableCollection<GrabMediaItem> GrabberItems { get; } = new();
    private readonly SiteGrabberService _grabberService = new();

    // Properties Modal
    private bool _isPropertiesModalOpen;
    public bool IsPropertiesModalOpen
    {
        get => _isPropertiesModalOpen;
        set => SetField(ref _isPropertiesModalOpen, value);
    }

    private DownloadItemViewModel? _propertiesItem;
    public DownloadItemViewModel? PropertiesItem
    {
        get => _propertiesItem;
        set => SetField(ref _propertiesItem, value);
    }

    // Batch Download Modal
    private bool _isBatchModalOpen;
    public bool IsBatchModalOpen
    {
        get => _isBatchModalOpen;
        set => SetField(ref _isBatchModalOpen, value);
    }

    private string _batchUrlsText = string.Empty;
    public string BatchUrlsText
    {
        get => _batchUrlsText;
        set
        {
            if (SetField(ref _batchUrlsText, value))
            {
                OnPropertyChanged(nameof(BatchUrlCount));
                OnPropertyChanged(nameof(HasBatchUrls));
            }
        }
    }

    public bool HasBatchUrls => BatchUrlCount > 0;

    public int BatchUrlCount => ParseUrlLines(_batchUrlsText).Count;

    /// <summary>Referer/cookies that come with URLs handed over by the browser (null for pasted lists).</summary>
    private DownloadRequestOptions? _batchOptions;

    private static List<string> ParseUrlLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(ExternalDownloadRequest.IsHttpUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    // Clipboard Sniffer Notification Banner
    private bool _isClipboardBannerVisible;
    public bool IsClipboardBannerVisible
    {
        get => _isClipboardBannerVisible;
        set => SetField(ref _isClipboardBannerVisible, value);
    }

    private string _clipboardBannerUrl = string.Empty;
    public string ClipboardBannerUrl
    {
        get => _clipboardBannerUrl;
        set => SetField(ref _clipboardBannerUrl, value);
    }

    // Active Quick Filter Tag ("All", "Active", "Completed", "Paused")
    private string _activeFilterTag = "All";
    public string ActiveFilterTag
    {
        get => _activeFilterTag;
        set
        {
            if (SetField(ref _activeFilterTag, value))
            {
                ApplyCategoryFilter();
            }
        }
    }

    // Theme Toggle
    public bool IsDarkMode
    {
        get => Settings.IsDarkMode;
        set
        {
            if (Settings.IsDarkMode != value)
            {
                Settings.IsDarkMode = value;
                Settings.Save();
                OnPropertyChanged();
                OnThemeChanged?.Invoke(value);
            }
        }
    }
    public Action<bool>? OnThemeChanged { get; set; }

    public string FreeDiskSpaceFormatted
    {
        get
        {
            try
            {
                var path = Settings.DefaultDownloadDirectory;
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    return $"{drive.Name.TrimEnd('\\')} ({DownloadProgress.FormatBytes(drive.AvailableFreeSpace)} free)";
                }
            }
            catch { }
            return "Disk Ready";
        }
    }

    // Status Filter Counts
    public int AllCount => AllDownloads.Count;
    public int DownloadingCount => AllDownloads.Count(d => d.IsActive);
    public int CompletedCount => AllDownloads.Count(d => d.IsCompleted);
    public int PausedCount => AllDownloads.Count(d => d.IsPaused);
    public int UnfinishedCount => AllDownloads.Count(d => !d.IsCompleted);
    public int FinishedCount => AllDownloads.Count(d => d.IsCompleted);

    // Category Counts
    public int CompressedCount => AllDownloads.Count(d => MatchesCategory(d, "Compressed"));
    public int DocumentsCount => AllDownloads.Count(d => MatchesCategory(d, "Documents"));
    public int MusicCount => AllDownloads.Count(d => MatchesCategory(d, "Music"));
    public int ProgramsCount => AllDownloads.Count(d => MatchesCategory(d, "Programs"));
    public int VideoCount => AllDownloads.Count(d => MatchesCategory(d, "Video"));

    public bool HasNoDownloads => FilteredDownloads.Count == 0;

    public MainViewModel()
    {
        _newDownloadDir = Settings.DefaultDownloadDirectory;
        _newDownloadConnections = Settings.DefaultConnections;
        _speedLimitKb = Settings.DefaultSpeedLimitKb;
        _isSpeedLimiterEnabled = Settings.IsSpeedLimiterEnabled;

        // Load scheduler settings
        _schedulerEnabled = Settings.SchedulerEnabled;
        _schedulerStartTime = Settings.SchedulerStartTime;
        _schedulerStopTime = Settings.SchedulerStopTime;
        _schedulerShutdownOnComplete = Settings.SchedulerShutdownOnComplete;

        UpdateSpeedLimits();
        UpdateSpeedLimits();
        UpdateSpeedLimits();
        RebuildSemaphore();

        foreach (var record in _store.Load())
        {
            AllDownloads.Add(DownloadItemViewModel.FromRecord(record));
        }
        ApplyCategoryFilter();
        UpdateCounts();

        // Start scheduler heartbeat timer
        StartSchedulerHeartbeat();
    }

    // --- Persistence ---
    private void ScheduleSave()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _saveTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _saveTimer.Tick -= OnSaveTimerTick;
            _saveTimer.Tick += OnSaveTimerTick;
            _saveTimer.Stop();
            _saveTimer.Start();
        });
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        SaveNow();
    }

    public void SaveNow() => _store.Save(AllDownloads.Select(d => d.ToRecord()).ToList());

    /// <summary>Pauses every running download and flushes the list to disk. Call on application exit.</summary>
    public void Shutdown()
    {
        var running = AllDownloads.Where(d => d.IsRunning).ToList();
        foreach (var item in running)
        {
            item.RunCts?.Cancel();
        }

        try { Task.WaitAll(running.Select(d => d.RunTask!).ToArray(), TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }

        foreach (var item in running.Where(d => !d.IsCompleted && !d.IsError))
        {
            item.Status = "Paused";
        }

        SaveNow();
    }

    private void RebuildSemaphore()
    {
        int limit = Math.Max(1, Settings.MaxConcurrentDownloads);
        if (_concurrencySemaphore != null && limit == _concurrencyLimit) return;

        _concurrencyLimit = limit;
        _concurrencySemaphore = new SemaphoreSlim(limit, limit);
    }

    /// <summary>Quick toggles (toolbar, menu) save at once; inside the settings dialog Save/Cancel decides.</summary>
    private void PersistOutsideSettingsDialog()
    {
        if (!IsSettingsModalOpen) Settings.Save();
    }

    /// <summary>Marshals an engine/worker-thread update onto the UI thread without blocking the caller.</summary>
    private static void UiPost(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public void OpenAddModal(string? initialUrl = null, bool keepPending = false)
    {
        if (!keepPending) PendingOptions = null;
        NewDownloadUrl = initialUrl ?? string.Empty;
        IsAddModalOpen = true;
    }

    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        NewDownloadUrl = string.Empty;
        PendingOptions = null;
    }

    public void OpenSettingsModal()
    {
        _settingsSnapshot = Settings.Clone();
        IsSettingsModalOpen = true;
    }

    /// <summary>Cancel: discards every edit made since the dialog was opened.</summary>
    public void CloseSettingsModal()
    {
        if (_settingsSnapshot != null)
        {
            Settings.CopyFrom(_settingsSnapshot);
            _settingsSnapshot = null;
            SyncFromSettings();
        }

        IsSettingsModalOpen = false;
    }

    public void SaveSettings()
    {
        Settings.Normalize();
        Settings.Save();
        _settingsSnapshot = null;
        SyncFromSettings();
        RebuildSemaphore();
        IsSettingsModalOpen = false;
    }

    public void SetDefaultDownloadDirectory(string folder)
    {
        Settings.DefaultDownloadDirectory = folder;
        NewDownloadDir = folder;
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>Refreshes everything that mirrors <see cref="Settings"/> after it changed behind the bindings' back.</summary>
    private void SyncFromSettings()
    {
        NewDownloadDir = Settings.DefaultDownloadDirectory;
        NewDownloadConnections = Settings.DefaultConnections;

        _isSpeedLimiterEnabled = Settings.IsSpeedLimiterEnabled;
        _speedLimitKb = Settings.DefaultSpeedLimitKb;
        OnPropertyChanged(nameof(IsSpeedLimiterEnabled));
        OnPropertyChanged(nameof(SpeedLimitKb));
        UpdateSpeedLimits();

        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(FreeDiskSpaceFormatted));
    }

    public void SetConnectionsPreset(int streams)
    {
        NewDownloadConnections = streams;
    }

    public void ToggleSegmentPanel()
    {
        IsSegmentPanelVisible = !IsSegmentPanelVisible;
    }

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

    // --- Queue Management ---
    public void StartQueue()
    {
        IsQueueActive = true;
        _shutdownPending = false;
        foreach (var item in AllDownloads.Where(d => !d.IsCompleted && !d.IsRunning).Reverse().ToList())
        {
            ResumeItem(item);
        }
    }

    public void StopQueue()
    {
        IsQueueActive = false;
        StopAll();
    }

    // --- Scheduler Feature ---
    public void OpenSchedulerModal()
    {
        SchedulerEnabled = Settings.SchedulerEnabled;
        SchedulerStartTime = Settings.SchedulerStartTime;
        SchedulerStopTime = Settings.SchedulerStopTime;
        SchedulerShutdownOnComplete = Settings.SchedulerShutdownOnComplete;
        IsSchedulerModalOpen = true;
    }

    public void CloseSchedulerModal()
    {
        IsSchedulerModalOpen = false;
    }

    public void SaveSchedulerSettings()
    {
        // Free-text times are normalised to HH:mm; anything that is not a time of day keeps the previous value
        SchedulerStartTime = AppSettings.NormalizeTime(SchedulerStartTime, Settings.SchedulerStartTime);
        SchedulerStopTime = AppSettings.NormalizeTime(SchedulerStopTime, Settings.SchedulerStopTime);

        Settings.SchedulerEnabled = SchedulerEnabled;
        Settings.SchedulerStartTime = SchedulerStartTime;
        Settings.SchedulerStopTime = SchedulerStopTime;
        Settings.SchedulerShutdownOnComplete = SchedulerShutdownOnComplete;
        Settings.Save();
        IsSchedulerModalOpen = false;
    }

    private void StartSchedulerHeartbeat()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => OnSchedulerTick();
        timer.Start();
    }

    private void OnSchedulerTick()
    {
        if (Settings.SchedulerEnabled)
        {
            var now = DateTime.Now.ToString("HH:mm");
            if (now == Settings.SchedulerStartTime && !IsQueueActive)
            {
                StartQueue();
            }
            else if (now == Settings.SchedulerStopTime && IsQueueActive && Settings.SchedulerStopQueueAtEndTime)
            {
                StopQueue();
            }
        }

        // While the queue runs, failed downloads are retried every 30 s (up to 5 attempts), like IDM's scheduler
        if (IsQueueActive)
        {
            foreach (var item in AllDownloads.Where(d => d.IsError && !d.IsRunning && d.RetryCount < 5 &&
                                                         (DateTime.Now - d.LastFailureAt).TotalSeconds >= 30).ToList())
            {
                ResumeItem(item);
            }
        }
    }

    /// <summary>When the active queue has nothing left to do and shutdown-on-complete is set, offers to power off.</summary>
    private void CheckQueueFinished()
    {
        if (!IsQueueActive) return;
        if (AllDownloads.Any(d => !d.IsCompleted && !d.IsPaused && !(d.IsError && d.RetryCount >= 5))) return;
        if (!AllDownloads.Any(d => d.IsCompleted)) return;

        IsQueueActive = false;
        if (!Settings.SchedulerShutdownOnComplete || _shutdownPending) return;

        _shutdownPending = true;
        SaveNow();
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 60 /c \"WDM: all downloads completed\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { return; }

        var answer = MessageBox.Show(
            "All downloads are complete. Windows will shut down in 60 seconds.\n\nClick Cancel to abort the shutdown.",
            "WDM — Shutdown", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel)
        {
            try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        }
    }

    // --- Site / Media Grabber Feature ---
    public void OpenGrabberModal()
    {
        GrabberUrl = string.Empty;
        GrabberStatusText = "Enter any webpage URL to crawl for downloadable media and files.";
        GrabberItems.Clear();
        IsGrabberModalOpen = true;
    }

    public void CloseGrabberModal()
    {
        IsGrabberModalOpen = false;
    }

    public async Task ExecuteGrabberAnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(GrabberUrl)) return;

        IsGrabberBusy = true;
        GrabberStatusText = "Analyzing URL and discovering media streams...";
        GrabberItems.Clear();

        try
        {
            var items = await _grabberService.GrabLinksAsync(GrabberUrl.Trim(), Settings.UserAgent);
            foreach (var it in items)
            {
                GrabberItems.Add(it);
            }

            GrabberStatusText = items.Count > 0
                ? $"Found {items.Count} downloadable files/streams."
                : "No downloadable media or direct links found on this page.";
        }
        catch (Exception ex)
        {
            GrabberStatusText = "Error during analysis: " + ex.Message;
        }
        finally
        {
            IsGrabberBusy = false;
        }
    }

    public void AddGrabbedDownloads()
    {
        var selected = GrabberItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            AddDownload(item.Url, Settings.DefaultDownloadDirectory, Settings.DefaultConnections,
                new DownloadRequestOptions { Referer = GrabberUrl.Trim() });
        }

        CloseGrabberModal();
    }

    // --- Properties Modal ---
    public void OpenPropertiesModal(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        PropertiesItem = item;
        IsPropertiesModalOpen = true;
    }

    public void ClosePropertiesModal()
    {
        IsPropertiesModalOpen = false;
        PropertiesItem = null;
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

    // --- Batch Download Feature ---
    public void OpenBatchModal(string? initialText = null, DownloadRequestOptions? options = null)
    {
        _batchOptions = options;
        BatchUrlsText = initialText ?? string.Empty;
        IsBatchModalOpen = true;
    }

    public void CloseBatchModal()
    {
        IsBatchModalOpen = false;
        _batchOptions = null;
    }

    public void ConfirmBatchDownload()
    {
        var dir = string.IsNullOrWhiteSpace(NewDownloadDir) ? Settings.DefaultDownloadDirectory : NewDownloadDir.Trim();
        foreach (var url in ParseUrlLines(BatchUrlsText))
        {
            AddDownload(url, dir, Settings.DefaultConnections, _batchOptions == null ? null : CloneSharedOptions(_batchOptions));
        }

        CloseBatchModal();
    }

    public void ExtractClipboardUrls()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                var matches = System.Text.RegularExpressions.Regex.Matches(text, @"https?://[^\s""'<>]+");
                var urls = matches.Select(m => m.Value).Distinct().ToList();
                if (urls.Count > 0)
                {
                    var existing = string.IsNullOrWhiteSpace(BatchUrlsText) ? "" : BatchUrlsText + Environment.NewLine;
                    BatchUrlsText = existing + string.Join(Environment.NewLine, urls);
                }
            }
        }
        catch { }
    }

    // --- Clipboard Sniffer ---
    public void CheckClipboardForDownloadableLink()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var txt = Clipboard.GetText().Trim();
            if (ExternalDownloadRequest.IsHttpUrl(txt) &&
                !AllDownloads.Any(d => d.Url.Equals(txt, StringComparison.OrdinalIgnoreCase)))
            {
                if (txt != ClipboardBannerUrl)
                {
                    ClipboardBannerUrl = txt;
                    IsClipboardBannerVisible = true;
                }
            }
        }
        catch { }
    }

    public void DismissClipboardBanner()
    {
        IsClipboardBannerVisible = false;
    }

    public void DownloadClipboardBanner()
    {
        if (!string.IsNullOrWhiteSpace(ClipboardBannerUrl))
        {
            AddDownload(ClipboardBannerUrl, Settings.DefaultDownloadDirectory, Settings.DefaultConnections);
            DismissClipboardBanner();
        }
    }

    // --- Queue Priority Reordering ---
    public void MoveUpSelected()
    {
        if (SelectedDownload == null) return;
        int idx = AllDownloads.IndexOf(SelectedDownload);
        if (idx > 0)
        {
            AllDownloads.Move(idx, idx - 1);
            ApplyCategoryFilter();
            ScheduleSave();
        }
    }

    public void MoveDownSelected()
    {
        if (SelectedDownload == null) return;
        int idx = AllDownloads.IndexOf(SelectedDownload);
        if (idx >= 0 && idx < AllDownloads.Count - 1)
        {
            AllDownloads.Move(idx, idx + 1);
            ApplyCategoryFilter();
            ScheduleSave();
        }
    }

    public void ToggleSpeedLimiter()
    {
        IsSpeedLimiterEnabled = !IsSpeedLimiterEnabled;
    }

    /// <summary>
    /// Re-evaluates the filters and reconciles <see cref="FilteredDownloads"/> in place, so rows are not
    /// recreated (no flicker, selection and scroll position survive status updates).
    /// </summary>
    public void ApplyCategoryFilter()
    {
        var wanted = AllDownloads.Where(MatchesFilters).ToList();

        for (int i = FilteredDownloads.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(FilteredDownloads[i])) FilteredDownloads.RemoveAt(i);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < FilteredDownloads.Count && ReferenceEquals(FilteredDownloads[i], wanted[i])) continue;

            int existing = FilteredDownloads.IndexOf(wanted[i]);
            if (existing >= 0) FilteredDownloads.Move(existing, i);
            else FilteredDownloads.Insert(i, wanted[i]);
        }

        OnPropertyChanged(nameof(HasNoDownloads));
        OnPropertyChanged(nameof(FreeDiskSpaceFormatted));
    }

    private bool MatchesFilters(DownloadItemViewModel item)
    {
        if (!MatchesCategory(item, SelectedCategory)) return false;

        bool matchesFilter = ActiveFilterTag switch
        {
            "Active" => item.IsActive || item.IsQueued,
            "Completed" => item.IsCompleted,
            "Paused" => item.IsPaused,
            _ => true
        };
        if (!matchesFilter) return false;

        return string.IsNullOrWhiteSpace(SearchText) ||
               item.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.Status.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(DownloadingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(PausedCount));
        OnPropertyChanged(nameof(UnfinishedCount));
        OnPropertyChanged(nameof(FinishedCount));
        OnPropertyChanged(nameof(CompressedCount));
        OnPropertyChanged(nameof(DocumentsCount));
        OnPropertyChanged(nameof(MusicCount));
        OnPropertyChanged(nameof(ProgramsCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(HasNoDownloads));
        OnPropertyChanged(nameof(IsLibraryEmpty));
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

    private static bool MatchesCategory(DownloadItemViewModel item, string category)
    {
        if (category == "All Downloads") return true;
        if (category == "Downloading" || category == "Unfinished") return !item.IsCompleted;
        if (category == "Completed" || category == "Finished") return item.IsCompleted;
        if (category == "Paused") return item.IsPaused;

        return string.Equals(item.CategoryName, category, StringComparison.Ordinal);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
