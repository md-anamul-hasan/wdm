using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public class DownloadItemViewModel : INotifyPropertyChanged
{
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
    private string _description = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LastTryDate
    {
        get => _lastTryDate;
        set => SetField(ref _lastTryDate, value);
    }
    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
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

    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetField(ref _fileName, value))
            {
                OnPropertyChanged(nameof(FileExtension));
                OnPropertyChanged(nameof(CategoryIconKind));
                OnPropertyChanged(nameof(CategoryColor));
                OnPropertyChanged(nameof(CategoryBadgeBackground));
            }
        }
    }

    public string FileExtension => Path.GetExtension(FileName).ToLowerInvariant();

    public string CategoryIconKind
    {
        get
        {
            var ext = FileExtension;
            if (ext is ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" or ".ts" or ".flv") return "Video";
            if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a") return "Music";
            if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso") return "Archive";
            if (ext is ".exe" or ".msi" or ".apk" or ".bat" or ".cmd") return "App";
            if (ext is ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx") return "Document";
            return "File";
        }
    }

    public Brush CategoryColor => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cbd5e1"));

    public Brush CategoryBadgeBackground => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172236"));

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
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsPaused));
            }
        }
    }

    public bool IsActive => Status is "Downloading" or "Connecting" or "Starting...";
    public bool IsCompleted => Status is "Completed";
    public bool IsPaused => Status is "Paused";

    public Brush StatusColor => Status switch
    {
        "Downloading" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8")),
        "Completed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34d399")),
        "Paused" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#fbbf24")),
        string s when s.StartsWith("Error") => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f87171")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8"))
    };

    public Brush StatusBadgeBackground => Status switch
    {
        "Downloading" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172554")),
        "Completed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064e3b")),
        "Paused" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451a03")),
        string s when s.StartsWith("Error") => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#450a0a")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b"))
    };

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetField(ref _progressPercentage, value);
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
