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
    public bool ShowStartDownloadDialog { get; set; } = true;
    public int NetworkTimeoutSeconds { get; set; } = 30;
    public string UserAgent { get; set; } = Engine.HttpRequestHelper.DefaultUserAgent;

    // Scheduler settings
    public bool SchedulerEnabled { get; set; } = false;
    public string SchedulerStartTime { get; set; } = "02:00";
    public string SchedulerStopTime { get; set; } = "06:00";
    public bool SchedulerShutdownOnComplete { get; set; } = false;
    public bool SchedulerStopQueueAtEndTime { get; set; } = true;

    // Theme & UI settings
    public bool IsDarkMode { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>%APPDATA%\WDM, or the folder named by the WDM_DATA_DIR environment variable (portable/test use).</summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("WDM_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WDM");

    private static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

        return new AppSettings();
    }

    /// <summary>Clamps hand-edited or stale values to what the engine and scheduler can handle.</summary>
    public void Normalize()
    {
        DefaultConnections = Math.Clamp(DefaultConnections, 1, 32);
        MaxConcurrentDownloads = Math.Clamp(MaxConcurrentDownloads, 1, 10);
        DefaultSpeedLimitKb = Math.Max(1, DefaultSpeedLimitKb);
        NetworkTimeoutSeconds = Math.Clamp(NetworkTimeoutSeconds, 5, 120);
        if (string.IsNullOrWhiteSpace(DefaultDownloadDirectory))
        {
            DefaultDownloadDirectory = new AppSettings().DefaultDownloadDirectory;
        }
        if (string.IsNullOrWhiteSpace(UserAgent)) UserAgent = Engine.HttpRequestHelper.DefaultUserAgent;
        SchedulerStartTime = NormalizeTime(SchedulerStartTime, "02:00");
        SchedulerStopTime = NormalizeTime(SchedulerStopTime, "06:00");
    }

    /// <summary>Returns <paramref name="value"/> as zero-padded HH:mm, or <paramref name="fallback"/> if it is not a time of day.</summary>
    public static string NormalizeTime(string? value, string fallback) =>
        TimeSpan.TryParseExact(value?.Trim(), new[] { @"h\:mm", @"hh\:mm" }, null, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? $"{t.Hours:D2}:{t.Minutes:D2}"
            : fallback;

    /// <summary>Overwrites every setting with the values of <paramref name="other"/> (used to discard edits).</summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite) property.SetValue(this, property.GetValue(other));
        }
    }

    public AppSettings Clone()
    {
        var copy = new AppSettings();
        copy.CopyFrom(this);
        return copy;
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
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
