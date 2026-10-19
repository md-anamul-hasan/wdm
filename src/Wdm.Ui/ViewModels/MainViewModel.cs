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

    // Status Filter Counts
    public int AllCount => AllDownloads.Count;
    public int DownloadingCount => AllDownloads.Count(d => d.IsActive);
    public int CompletedCount => AllDownloads.Count(d => d.IsCompleted);
    public int PausedCount => AllDownloads.Count(d => d.IsPaused);

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
        ApplyCategoryFilter();
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

        Task.Run(async () =>
        {
            var engine = new DownloadEngine(maxBytesPerSecond: IsSpeedLimiterEnabled ? (long)SpeedLimitKb * 1024 : 0);
            item.Engine = engine;

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
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.Status = "Error: " + ex.Message;
                    UpdateAggregateSpeed();
                });
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

    public void ResumeItem(DownloadItemViewModel? item)
    {
        if (item != null && (item.Status == "Paused" || item.Status.StartsWith("Error")))
        {
            AddDownload(item.Url, Path.GetDirectoryName(item.TargetFilePath));
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

    public void ApplyCategoryFilter()
    {
        FilteredDownloads.Clear();
        foreach (var item in AllDownloads)
        {
            bool matchesCategory = MatchesCategory(item, SelectedCategory);
            bool matchesSearch = string.IsNullOrWhiteSpace(SearchText) || 
                                 item.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                 item.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            if (matchesCategory && matchesSearch)
            {
                FilteredDownloads.Add(item);
            }
        }
        OnPropertyChanged(nameof(HasNoDownloads));
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(DownloadingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(PausedCount));
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
            if (MediaResolver.IsYouTubeUrl(url)) return "Resolving YouTube Video...";
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
        if (category == "Downloading") return item.IsActive;
        if (category == "Completed") return item.IsCompleted;
        if (category == "Paused") return item.IsPaused;

        var ext = Path.GetExtension(item.FileName).ToLowerInvariant();
        return category switch
        {
            "Compressed" => ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso",
            "Documents" => ext is ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx",
            "Music" => ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a",
            "Programs" => ext is ".exe" or ".msi" or ".apk" or ".bat" or ".cmd",
            "Videos" or "Video" => ext is ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" or ".ts",
            _ => true
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
