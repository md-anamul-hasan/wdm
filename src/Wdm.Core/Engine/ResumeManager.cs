using System.Text.Json;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Manages checkpointing and resuming of download state.
/// Persists chunk ranges and progress to a .wdm-state JSON file.
/// </summary>
public static class ResumeManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetCheckpointPath(string targetFilePath) => targetFilePath + ".wdm-state";

    public static async Task SaveCheckpointAsync(DownloadMetadata metadata, CancellationToken cancellationToken = default)
    {
        var checkpointPath = GetCheckpointPath(metadata.TargetFilePath);
        var tempPath = $"{checkpointPath}.{Guid.NewGuid():N}.tmp";

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        try
        {
            File.Move(tempPath, checkpointPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public static async Task<DownloadMetadata?> LoadCheckpointAsync(string targetFilePath, CancellationToken cancellationToken = default)
    {
        var checkpointPath = GetCheckpointPath(targetFilePath);
        if (!File.Exists(checkpointPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(checkpointPath, cancellationToken);
            return JsonSerializer.Deserialize<DownloadMetadata>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void DeleteCheckpoint(string targetFilePath)
    {
        var checkpointPath = GetCheckpointPath(targetFilePath);
        if (File.Exists(checkpointPath))
        {
            try { File.Delete(checkpointPath); } catch { }
        }
    }
}
