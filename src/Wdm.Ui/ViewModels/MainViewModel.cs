using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
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
    private string _newDownloadSha256 = string.Empty;
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
                OnPropertyChanged(nameof(CanConfirmAdd));
            }
        }
    }

    /// <summary>Optional SHA-256 the finished file must match; blank means no verification.</summary>
    public string NewDownloadSha256
    {
        get => _newDownloadSha256;
        set
        {
            if (SetField(ref _newDownloadSha256, value))
            {
                OnPropertyChanged(nameof(IsNewSha256Valid));
                OnPropertyChanged(nameof(ShowSha256Error));
                OnPropertyChanged(nameof(CanConfirmAdd));
            }
        }
    }

    public bool IsNewSha256Valid => string.IsNullOrWhiteSpace(NewDownloadSha256) || Sha256Checksum.TryNormalize(NewDownloadSha256, out _);

    public bool ShowSha256Error => !IsNewSha256Valid;

    public bool CanConfirmAdd => IsNewDownloadUrlValid && IsNewSha256Valid;

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
