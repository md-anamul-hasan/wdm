using System.Text.Json;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>One persisted row of the download list.</summary>
public class DownloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string TargetFilePath { get; set; } = string.Empty;
    public string Status { get; set; } = "Paused";
    public long TotalBytes { get; set; } = -1;
    public long DownloadedBytes { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public DownloadRequestOptions? Options { get; set; }

    /// <summary>What is persisted: a finished download no longer needs its cookies or credentials.</summary>
    internal DownloadRecord ForStorage()
    {
        var copy = (DownloadRecord)MemberwiseClone();
        if (Status == "Completed" && Options is { HasSecrets: true }) copy.Options = Options.WithoutSecrets();
        return copy;
    }
}

/// <summary>Persists the download list to %APPDATA%\WDM\downloads.json (atomic replace).</summary>
public class DownloadStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();

    public DownloadStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppSettings.DataDirectory, "downloads.json");
    }

    public List<DownloadRecord> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var records = JsonSerializer.Deserialize<List<DownloadRecord>>(json, JsonOptions) ?? new();

                // Migrate: lists written by older versions hold cookies/passwords in clear (and secrets of finished downloads)
                if (records.Any(r => r.Options is { } o && (SecretProtector.IsLegacyPlaintext(o.Cookies) ||
                                                              SecretProtector.IsLegacyPlaintext(o.Username) ||
                                                              SecretProtector.IsLegacyPlaintext(o.Password) ||
                                                              (r.Status == "Completed" && o.HasSecrets))))
                {
                    Save(records);
                }
                return records;
            }
        }
        catch (JsonException)
        {
            // Keep the unreadable list so the next save does not destroy it
            try { File.Move(_path, _path + ".corrupt", overwrite: true); } catch (IOException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return new();
    }

    public void Save(IEnumerable<DownloadRecord> records)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(records.Select(r => r.ForStorage()).ToList(), JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
