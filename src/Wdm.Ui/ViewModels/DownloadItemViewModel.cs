using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public class DownloadItemViewModel : INotifyPropertyChanged
{
    public static DownloadItemViewModel FromRecord(DownloadRecord r)
    {
        // Anything that was in flight when the app closed comes back paused and resumable
        var status = r.Status is "Completed" ? "Completed" : r.Status.StartsWith("Error", StringComparison.Ordinal) ? r.Status : "Paused";
        var item = new DownloadItemViewModel
        {
            Id = r.Id,
            Url = r.Url,
            FileName = r.FileName,
            TargetFilePath = r.TargetFilePath,
            TotalBytes = r.TotalBytes,
            AddedAt = r.AddedAt,
            CompletedAt = r.CompletedAt,
            Options = r.Options,
            Status = status,
            LastTryDate = r.AddedAt.ToString("g")
        };

        if (status == "Completed")
        {
            item.ProgressPercentage = 100;
            item.SizeFormatted = DownloadProgress.FormatBytes(r.TotalBytes > 0 ? r.TotalBytes : r.DownloadedBytes);
        }
        else
        {
            item.ProgressPercentage = r.TotalBytes > 0 ? Math.Clamp((double)r.DownloadedBytes / r.TotalBytes * 100, 0, 100) : 0;
            item.SizeFormatted = DownloadProgress.FormatBytes(r.DownloadedBytes);
        }

        return item;
    }

    public DownloadRecord ToRecord() => new()
    {
        Id = Id,
        Url = Url,
        FileName = FileName,
        TargetFilePath = TargetFilePath,
        Status = Status,
        TotalBytes = TotalBytes,
        DownloadedBytes = TotalBytes > 0 ? (long)(TotalBytes * ProgressPercentage / 100.0) : 0,
        AddedAt = AddedAt,
        CompletedAt = IsCompleted ? CompletedAt ??= DateTime.Now : null,
        // Session cookies / credentials are only needed to resume, so drop them once finished
        Options = IsCompleted ? null : Options
    };

    private string _fileName = string.Empty;
    private string _sizeFormatted = "0 B";
    private string _status = "Queued";
    private double _progressPercentage;
    private string _speedFormatted = "0 B/s";
    private string _timeLeftFormatted = "--:--";
    private List<DownloadSegment> _segments = new();

    private double _speedRaw;
    private string _domain = string.Empty;
    private string _lastTryDate = DateTime.Now.ToString("g");

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LastTryDate
    {
        get => _lastTryDate;
        set => SetField(ref _lastTryDate, value);
    }
    public string Url
    {
        get => _url;
        set
        {
            if (SetField(ref _url, value))
            {
                UpdateDomain();
            }
        }
    }
    private string _url = string.Empty;

    public string Domain
    {
        get => _domain;
        private set => SetField(ref _domain, value);
    }

    public string TargetFilePath { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DownloadEngine? Engine { get; set; }

    // Run-time bookkeeping (not persisted)
    public CancellationTokenSource? RunCts { get; set; }
    public Task? RunTask { get; set; }
    public int RetryCount { get; set; }
    public DateTime LastFailureAt { get; set; } = DateTime.MinValue;
    public bool IsRunning => RunTask is { IsCompleted: false };

    public long TotalBytes { get; set; } = -1;
    public DateTime? CompletedAt { get; set; }
    public DownloadRequestOptions? Options { get; set; }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetField(ref _fileName, value))
            {
                OnPropertyChanged(nameof(Category));
                OnPropertyChanged(nameof(CategoryName));
                OnPropertyChanged(nameof(FileIconGeometry));
                OnPropertyChanged(nameof(FileIconBrush));
                OnPropertyChanged(nameof(FileTileBrush));
            }
        }
    }

    public FileCategory Category => FileCategories.FromFileName(FileName);

    private string FileIconResourceKey => Category switch
    {
        FileCategory.Video => "GeoVideo",
        FileCategory.Music => "GeoMusic",
        FileCategory.Compressed => "GeoArchive",
        FileCategory.Programs => "GeoProgram",
        FileCategory.Documents => "GeoDocument",
        _ => "GeoFile"
    };

    public Geometry? FileIconGeometry
    {
        get
        {
            try
            {
                return Application.Current?.TryFindResource(FileIconResourceKey) as Geometry;
            }
            catch
            {
                return null;
            }
        }
    }

    private static SolidColorBrush Frozen(string hex, byte? alpha = null)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        if (alpha.HasValue) c.A = alpha.Value;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static readonly SolidColorBrush VideoBrush = Frozen("#E11D48"), MusicBrush = Frozen("#DB2777"),
        ArchiveBrush = Frozen("#EA580C"), AppBrush = Frozen("#7C3AED"), DocBrush = Frozen("#2563EB"), FileBrush = Frozen("#64748B");

    public Brush FileIconBrush => Category switch
    {
        FileCategory.Video => VideoBrush,
        FileCategory.Music => MusicBrush,
        FileCategory.Compressed => ArchiveBrush,
        FileCategory.Programs => AppBrush,
        FileCategory.Documents => DocBrush,
        _ => FileBrush
    };

    /// <summary>Soft tinted tile behind the file icon (translucent so it works on light and dark surfaces).</summary>
    public Brush FileTileBrush => Category switch
    {
        FileCategory.Video => TileVideo,
        FileCategory.Music => TileMusic,
        FileCategory.Compressed => TileArchive,
        FileCategory.Programs => TileApp,
        FileCategory.Documents => TileDoc,
        _ => TileFile
    };

    private static readonly SolidColorBrush TileVideo = Frozen("#E11D48", 0x24), TileMusic = Frozen("#DB2777", 0x24),
        TileArchive = Frozen("#EA580C", 0x24), TileApp = Frozen("#7C3AED", 0x24), TileDoc = Frozen("#2563EB", 0x24), TileFile = Frozen("#64748B", 0x24);

    private void UpdateDomain()
    {
        try
        {
            if (Uri.TryCreate(_url, UriKind.Absolute, out var uri))
            {
                Domain = uri.Host;
            }
            else
            {
                Domain = string.Empty;
            }
        }
        catch
        {
            Domain = string.Empty;
        }
    }

    public double SpeedRaw
    {
        get => _speedRaw;
        set => SetField(ref _speedRaw, value);
    }

    public string SizeFormatted
    {
        get => _sizeFormatted;
        set => SetField(ref _sizeFormatted, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusToolTip));
                OnPropertyChanged(nameof(ErrorText));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(IsQueued));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsPaused));
            }
        }
    }

    public bool IsActive => Status is "Downloading" or "Connecting";
    public bool IsCompleted => Status is "Completed";
    public bool IsPaused => Status is "Paused";
    public bool IsQueued => Status is "Queued";
    public bool IsError => Status.StartsWith("Error", StringComparison.Ordinal);

    public string CategoryName => FileCategories.DisplayName(Category);

    public string FormattedPercentage => $"{ProgressPercentage:F1}%";

    private static readonly SolidColorBrush StDownloading = Frozen("#2563EB"), StCompleted = Frozen("#16A34A"),
        StPaused = Frozen("#D97706"), StError = Frozen("#DC2626"), StIdle = Frozen("#64748B");

    private static readonly SolidColorBrush BgDownloading = Frozen("#2563EB", 0x22), BgCompleted = Frozen("#16A34A", 0x22),
        BgPaused = Frozen("#D97706", 0x22), BgError = Frozen("#DC2626", 0x22), BgIdle = Frozen("#64748B", 0x22);

    public Brush StatusColor => Status switch
    {
        "Downloading" => StDownloading,
        "Completed" => StCompleted,
        "Paused" => StPaused,
        _ when IsError => StError,
        _ => StIdle
    };

    public Brush StatusBadgeBackground => Status switch
    {
        "Downloading" => BgDownloading,
        "Completed" => BgCompleted,
        "Paused" => BgPaused,
        _ when IsError => BgError,
        _ => BgIdle
    };

    /// <summary>Short label for the status badge; the full error text is exposed through <see cref="StatusToolTip"/>.</summary>
    public string StatusLabel => IsError ? "Error" : Status;

    public string StatusToolTip => IsError ? Status : string.Empty;

    /// <summary>The failure reason without the "Error: " prefix, shown in the details panel so it does not hide behind a tooltip.</summary>
    public string ErrorText => !IsError ? string.Empty : Status.StartsWith("Error:", StringComparison.Ordinal) ? Status[6..].Trim() : "The download failed.";

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set
        {
            if (SetField(ref _progressPercentage, value))
            {
                OnPropertyChanged(nameof(FormattedPercentage));
            }
        }
    }

    public string SpeedFormatted
    {
        get => _speedFormatted;
        set => SetField(ref _speedFormatted, value);
    }

    public string TimeLeftFormatted
    {
        get => _timeLeftFormatted;
        set => SetField(ref _timeLeftFormatted, value);
    }

    public List<DownloadSegment> Segments
    {
        get => _segments;
        set => SetField(ref _segments, value);
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
