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
                return JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(_path), JsonOptions) ?? new();
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
                File.WriteAllText(temp, JsonSerializer.Serialize(records, JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
