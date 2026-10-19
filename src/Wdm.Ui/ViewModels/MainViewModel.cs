using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Wdm.Core.Engine;
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

    // Concurrency semaphore to enforce MaxConcurrentDownloads
    private SemaphoreSlim? _concurrencySemaphore;

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
        set => SetField(ref _selectedDownload, value);
    }

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
                UpdateSpeedLimits();
            }
        }
    }

    public int SpeedLimitKb
    {
        get => _speedLimitKb;
        set
        {
            if (SetField(ref _speedLimitKb, value))
            {
                UpdateSpeedLimits();
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
            }
        }
    }

    public bool IsYouTubeUrl => MediaResolver.IsYouTubeUrl(NewDownloadUrl);

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
            }
        }
    }

    public int BatchUrlCount
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_batchUrlsText)) return 0;
            return _batchUrlsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            l.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }
    }

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
    public int VideoCount => AllDownloads.Count(d => MatchesCategory(d, "Videos"));

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

        RebuildSemaphore();
        ApplyCategoryFilter();

        // Start scheduler heartbeat timer
        StartSchedulerHeartbeat();
    }

    private void RebuildSemaphore()
    {
        _concurrencySemaphore?.Dispose();
        _concurrencySemaphore = new SemaphoreSlim(
            Math.Max(1, Settings.MaxConcurrentDownloads),
            Math.Max(1, Settings.MaxConcurrentDownloads));
    }

    public void OpenAddModal(string? initialUrl = null)
    {
        NewDownloadUrl = initialUrl ?? string.Empty;
        IsAddModalOpen = true;
    }

    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        NewDownloadUrl = string.Empty;
    }

    public void OpenSettingsModal()
    {
        IsSettingsModalOpen = true;
    }

    public void CloseSettingsModal()
    {
        IsSettingsModalOpen = false;
    }

    public void SaveSettings()
    {
        Settings.Save();
        NewDownloadDir = Settings.DefaultDownloadDirectory;
        NewDownloadConnections = Settings.DefaultConnections;
        RebuildSemaphore();
        IsSettingsModalOpen = false;
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
        if (string.IsNullOrWhiteSpace(url)) return;

        int connections = Math.Clamp(NewDownloadConnections, 1, 32);
        string? targetDir = string.IsNullOrWhiteSpace(NewDownloadDir) ? null : NewDownloadDir.Trim();

        AddDownload(url, targetDir, connections);
        CloseAddModal();
    }

    public void AddDownload(string url, string? destinationDir = null, int connections = 8)
    {
        var item = new DownloadItemViewModel
        {
            Url = url,
            FileName = GetInitialName(url),
            Status = "Connecting...",
            ProgressPercentage = 0
        };

        AllDownloads.Insert(0, item);
        UpdateCounts();
        ApplyCategoryFilter();
        SelectedDownload = item;

        StartDownloadTask(item, url, destinationDir, connections);
    }

    /// <summary>
    /// Resumes a paused/errored download by reusing its existing slot in AllDownloads.
    /// </summary>
    public void ResumeItem(DownloadItemViewModel? item)
    {
        if (item == null) return;
        if (!item.IsPaused && !item.Status.StartsWith("Error")) return;

        string url = item.Url;
        string? dir = string.IsNullOrWhiteSpace(item.TargetFilePath)
            ? null
            : Path.GetDirectoryName(item.TargetFilePath);

        // Reset UI state without inserting a new row
        item.Status = "Connecting...";
        item.SpeedRaw = 0;
        item.SpeedFormatted = "0 B/s";
        item.TimeLeftFormatted = "--:--";
        item.Engine = null;

        UpdateCounts();
        ApplyCategoryFilter();

        StartDownloadTask(item, url, dir, Settings.DefaultConnections);
    }

    private void StartDownloadTask(DownloadItemViewModel item, string url, string? destinationDir, int connections)
    {
        var semaphore = _concurrencySemaphore;

        Task.Run(async () =>
        {
            // Wait for a concurrency slot
            if (semaphore != null)
            {
                await semaphore.WaitAsync();
            }

            var engine = new DownloadEngine(maxBytesPerSecond: IsSpeedLimiterEnabled ? (long)SpeedLimitKb * 1024 : 0);

            Application.Current?.Dispatcher.Invoke(() => { item.Engine = engine; });

            engine.ProgressUpdated += progress =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.ProgressPercentage = progress.Percentage;
                    item.SpeedFormatted = progress.FormattedSpeed;
                    item.SpeedRaw = progress.SpeedBytesPerSecond;
                    item.TimeLeftFormatted = progress.EstimatedTimeRemaining.HasValue
                        ? $"{progress.EstimatedTimeRemaining.Value:mm\\:ss}"
                        : "--:--";
                    item.SizeFormatted = progress.FormattedDownloaded;
                    item.Status = progress.Status.ToString();
                    item.Segments = progress.Segments.ToList();
                    UpdateAggregateSpeed();
                });
            };

            engine.StatusChanged += status =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.Status = status.ToString();
                    if (engine.Metadata != null)
                    {
                        item.FileName = engine.Metadata.FileName;
                        item.TargetFilePath = engine.Metadata.TargetFilePath;
                        UpdateCounts();
                    }
                    UpdateAggregateSpeed();
                });
            };

            try
            {
                await engine.StartAsync(url, destinationDir, connectionCount: connections);
            }
            catch (OperationCanceledException)
            {
                // Pause is normal — status already set by engine
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.Status = "Error: " + ex.Message;
                    UpdateAggregateSpeed();
                });
            }
            finally
            {
                semaphore?.Release();
                await engine.DisposeAsync();
            }
        });
    }

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
        foreach (var item in AllDownloads.Where(d => d.IsActive).ToList())
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
        // Resume all queued or paused downloads
        var queuedItems = AllDownloads.Where(d => d.IsPaused || d.Status == "Queued" || d.Status.StartsWith("Error")).ToList();
        foreach (var item in queuedItems)
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
        Settings.SchedulerEnabled = SchedulerEnabled;
        Settings.SchedulerStartTime = SchedulerStartTime;
        Settings.SchedulerStopTime = SchedulerStopTime;
        Settings.SchedulerShutdownOnComplete = SchedulerShutdownOnComplete;
        Settings.Save();
        IsSchedulerModalOpen = false;
    }

    private void StartSchedulerHeartbeat()
    {
        Task.Run(async () =>
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync())
            {
                if (!Settings.SchedulerEnabled) continue;

                var nowStr = DateTime.Now.ToString("HH:mm");
                if (nowStr == Settings.SchedulerStartTime && !IsQueueActive)
                {
                    Application.Current?.Dispatcher.Invoke(StartQueue);
                }
                else if (nowStr == Settings.SchedulerStopTime && IsQueueActive && Settings.SchedulerStopQueueAtEndTime)
                {
                    Application.Current?.Dispatcher.Invoke(StopQueue);
                }

                if (Settings.SchedulerShutdownOnComplete && AllDownloads.Count > 0 && AllDownloads.All(d => d.IsCompleted))
                {
                    // All completed, trigger shutdown notice
                }
            }
        });
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
            var items = await _grabberService.GrabLinksAsync(GrabberUrl.Trim());
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var it in items)
                {
                    GrabberItems.Add(it);
                }

                GrabberStatusText = items.Count > 0
                    ? $"Found {items.Count} downloadable files/streams."
                    : "No downloadable media or direct links found on this page.";
            });
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                GrabberStatusText = "Error during analysis: " + ex.Message;
            });
        }
        finally
        {
            Application.Current?.Dispatcher.Invoke(() => { IsGrabberBusy = false; });
        }
    }

    public void AddGrabbedDownloads()
    {
        var selected = GrabberItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            AddDownload(item.Url, null, Settings.DefaultConnections);
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

        // Pause existing engine
        item.Engine?.Pause();

        // Delete partial file if exists
        try
        {
            if (File.Exists(item.TargetFilePath))
            {
                File.Delete(item.TargetFilePath);
            }
        }
        catch { }

        // Reset state
        item.ProgressPercentage = 0;
        item.SizeFormatted = "0 B";
        item.Status = "Connecting...";
        item.SpeedFormatted = "0 B/s";
        item.TimeLeftFormatted = "--:--";

        ResumeItem(item);
    }

    // --- Delete File From Disk ---
    public void DeleteFileFromDisk(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        item.Engine?.Pause();

        try
        {
            if (File.Exists(item.TargetFilePath))
            {
                File.Delete(item.TargetFilePath);
            }
        }
        catch { }

        DeleteItem(item);
    }

    public void PauseItem(DownloadItemViewModel? item)
    {
        if (item?.Engine != null)
        {
            item.Engine.Pause();
            item.Status = "Paused";
            item.SpeedRaw = 0;
            item.SpeedFormatted = "0 B/s";
            UpdateAggregateSpeed();
            UpdateCounts();
        }
    }

    public void DeleteItem(DownloadItemViewModel? item)
    {
        if (item != null)
        {
            item.Engine?.Pause();
            AllDownloads.Remove(item);
            UpdateCounts();
            ApplyCategoryFilter();
            UpdateAggregateSpeed();
            if (SelectedDownload == item)
            {
                SelectedDownload = FilteredDownloads.FirstOrDefault();
            }
        }
    }

    public void OpenFileLocation(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        string path = item.TargetFilePath;
        if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start("explorer.exe", $"\"{dir}\"");
            }
        }
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
        long limit = IsSpeedLimiterEnabled ? (long)SpeedLimitKb * 1024 : 0;
        foreach (var item in AllDownloads)
        {
            if (item.Engine != null)
            {
                item.Engine.SpeedLimiter.MaxBytesPerSecond = limit;
            }
        }
    }

    private void UpdateAggregateSpeed()
    {
        double total = AllDownloads.Where(d => d.IsActive).Sum(d => d.SpeedRaw);
        TotalSpeedFormatted = DownloadProgress.FormatBytes((long)total) + "/s";
        ActiveDownloadsCount = AllDownloads.Count(d => d.IsActive);
        OnPropertyChanged(nameof(TotalSpeedFormatted));
        OnPropertyChanged(nameof(ActiveDownloadsCount));
    }

    // --- Batch Download Feature ---
    public void OpenBatchModal(string? initialText = null)
    {
        BatchUrlsText = initialText ?? string.Empty;
        IsBatchModalOpen = true;
    }

    public void CloseBatchModal()
    {
        IsBatchModalOpen = false;
    }

    public void ConfirmBatchDownload()
    {
        if (string.IsNullOrWhiteSpace(BatchUrlsText))
        {
            CloseBatchModal();
            return;
        }

        var lines = BatchUrlsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                AddDownload(trimmed, NewDownloadDir, Settings.DefaultConnections);
            }
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
            if ((txt.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 txt.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
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
            AddDownload(ClipboardBannerUrl, null, Settings.DefaultConnections);
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
        }
    }

    public void ToggleSpeedLimiter()
    {
        IsSpeedLimiterEnabled = !IsSpeedLimiterEnabled;
    }

    public void ApplyCategoryFilter()
    {
        FilteredDownloads.Clear();
        foreach (var item in AllDownloads)
        {
            bool matchesCategory = MatchesCategory(item, SelectedCategory);
            bool matchesFilter = ActiveFilterTag switch
            {
                "Active" => item.IsActive,
                "Completed" => item.IsCompleted,
                "Paused" => item.IsPaused,
                _ => true
            };
            bool matchesSearch = string.IsNullOrWhiteSpace(SearchText) ||
                                 item.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                 item.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                 item.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                 item.Status.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            if (matchesCategory && matchesFilter && matchesSearch)
            {
                FilteredDownloads.Add(item);
            }
        }
        OnPropertyChanged(nameof(HasNoDownloads));
        OnPropertyChanged(nameof(FreeDiskSpaceFormatted));
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
    }

    private static string GetInitialName(string url)
    {
        try
        {
            if (MediaResolver.IsYouTubeUrl(url)) return "Resolving video stream...";
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? "download.dat" : name;
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

        var ext = Path.GetExtension(item.FileName).ToLowerInvariant();
        return category switch
        {
            "Compressed" => ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso",
            "Documents"  => ext is ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx",
            "Music"      => ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a",
            "Programs"   => ext is ".exe" or ".msi" or ".apk" or ".bat" or ".cmd",
            "Videos" or "Video" => ext is ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" or ".ts",
            _            => true
        };
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
