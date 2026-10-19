using System.IO;
using System.Text.Json;

namespace Wdm.Core.Models;

public class AppSettings
{
    public string DefaultDownloadDirectory { get; set; } = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public int DefaultConnections { get; set; } = 8;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int DefaultSpeedLimitKb { get; set; } = 4096;
    public bool IsSpeedLimiterEnabled { get; set; } = false;
    public bool AutoOpenFileOnComplete { get; set; } = false;
    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool EnableDynamicMiddleSplit { get; set; } = true;
    public int NetworkTimeoutSeconds { get; set; } = 30;
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

    // Scheduler settings
    public bool SchedulerEnabled { get; set; } = false;
    public string SchedulerStartTime { get; set; } = "02:00";
    public string SchedulerStopTime { get; set; } = "06:00";
    public bool SchedulerShutdownOnComplete { get; set; } = false;
    public bool SchedulerStopQueueAtEndTime { get; set; } = true;

    // Theme & UI settings
    public bool IsDarkMode { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WDM", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null) return settings;
            }
        }
        catch { }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
