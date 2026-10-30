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

    /// <summary>The in-progress file for a download whose final name is <paramref name="targetFilePath"/>.</summary>
    public static string GetPartPath(string targetFilePath) => targetFilePath + ".wdm-part";

    /// <param name="flushData">
    /// Called after the progress snapshot is taken and before it is written: the checkpoint may only claim
    /// bytes that have reached the disk, otherwise a power loss would resume over data that never landed.
    /// </param>
    public static async Task SaveCheckpointAsync(DownloadMetadata metadata, CancellationToken cancellationToken = default, Action? flushData = null)
    {
        var checkpointPath = GetCheckpointPath(metadata.TargetFilePath);
        var tempPath = $"{checkpointPath}.{Guid.NewGuid():N}.tmp";

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        flushData?.Invoke();
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        int retries = 5;
        while (true)
        {
            try
            {
                File.Move(tempPath, checkpointPath, overwrite: true);
                break;
            }
            catch (IOException) when (retries > 0)
            {
                retries--;
                await Task.Delay(25, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (retries > 0)
            {
                retries--;
                await Task.Delay(25, cancellationToken);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
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
