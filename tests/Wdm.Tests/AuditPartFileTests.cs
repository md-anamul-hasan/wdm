using System.Security.Cryptography;
using System.Text.Json;
using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Tests;

/// <summary>AUD-014: an unfinished download never sits under its final name.</summary>
public class AuditPartFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_part_{Guid.NewGuid():N}");

    public AuditPartFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static byte[] Data(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private async Task<(DownloadEngine engine, string target)> PausedDownload(string url, int connections = 4)
    {
        using var pause = new CancellationTokenSource();
        var engine = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        engine.ProgressUpdated += p => { if (p.DownloadedBytes >= 512 * 1024) pause.Cancel(); };
        try { await engine.StartAsync(url, _dir, connections, pause.Token); } catch (OperationCanceledException) { }
        await engine.DisposeAsync();
        return (engine, engine.Metadata!.TargetFilePath);
    }

    [Fact]
    public async Task WhilePaused_OnlyThePartFileAndCheckpointExist()
    {
        var data = Data(4 * 1024 * 1024, 1);
        using var server = AuditStressTests.RangeServer(data);
        var (_, target) = await PausedDownload(server.Prefix + "p.bin");

        Assert.False(File.Exists(target));
        Assert.True(File.Exists(ResumeManager.GetPartPath(target)));
        Assert.True(File.Exists(ResumeManager.GetCheckpointPath(target)));
    }

    [Fact]
    public async Task OnCompletion_FileGetsItsRealNameAndHelperFilesVanish()
    {
        var data = Data(4 * 1024 * 1024, 2);
        using var server = AuditStressTests.RangeServer(data);
        string url = server.Prefix + "done.bin";
        var (_, target) = await PausedDownload(url);

        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, 4);

        Assert.Equal(target, second.Metadata!.TargetFilePath);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(target)));
        Assert.False(File.Exists(ResumeManager.GetPartPath(target)));
        Assert.False(File.Exists(ResumeManager.GetCheckpointPath(target)));
        Assert.Null(second.Metadata.PartFilePath);
    }

    [Fact]
    public async Task CheckpointFromVersion101_ResumesInPlaceAndCompletes()
    {
        var data = Data(4 * 1024 * 1024, 3);
        using var server = AuditStressTests.RangeServer(data);
        string url = server.Prefix + "legacy.bin";
        var (_, target) = await PausedDownload(url);

        // Rebuild what WDM 1.0.x left behind: data written straight into the target, no PartFilePath in the checkpoint
        File.Move(ResumeManager.GetPartPath(target), target);
        string state = ResumeManager.GetCheckpointPath(target);
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(state))!.AsObject();
        node.Remove("PartFilePath");
        await File.WriteAllTextAsync(state, node.ToJsonString());

        await using var resumed = new DownloadEngine();
        await resumed.StartAsync(url, _dir, 4);

        Assert.Equal(target, resumed.Metadata!.TargetFilePath);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(target)));
        Assert.False(File.Exists(ResumeManager.GetPartPath(target)));
        Assert.False(File.Exists(state));
    }

    [Fact]
    public async Task ForeignFileAppearingUnderTheFinalName_IsNeverOverwritten()
    {
        var data = Data(2 * 1024 * 1024, 4);
        string? targetSeen = null;
        using var server = AuditStressTests.RangeServer(data, onRequest: ctx =>
        {
            // The first GET of the body races with the user dropping a different file under the final name
            if (ctx.Request.HttpMethod == "GET" && targetSeen == null)
            {
                targetSeen = Path.Combine(_dir, "taken.bin");
                File.WriteAllText(targetSeen, "somebody else's file");
            }
        });

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "taken.bin", _dir, 2);

        Assert.Equal("somebody else's file", File.ReadAllText(Path.Combine(_dir, "taken.bin")));
        Assert.Equal(Path.Combine(_dir, "taken (1).bin"), engine.Metadata!.TargetFilePath);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(engine.Metadata.TargetFilePath)));
    }

    [Fact]
    public async Task CrashAfterLastByteBeforeRename_ResumesAndPublishes()
    {
        var data = Data(4 * 1024 * 1024, 5);
        using var server = AuditStressTests.RangeServer(data);
        string url = server.Prefix + "late.bin";
        var (_, target) = await PausedDownload(url, connections: 1);

        // Simulate dying right after the final byte landed in the part file but before it was renamed
        string part = ResumeManager.GetPartPath(target);
        await File.WriteAllBytesAsync(part, data);
        string state = ResumeManager.GetCheckpointPath(target);
        var meta = JsonSerializer.Deserialize<DownloadMetadata>(await File.ReadAllTextAsync(state))!;
        foreach (var s in meta.Segments) s.CurrentOffset = s.EndOffset + 1;
        await File.WriteAllTextAsync(state, JsonSerializer.Serialize(meta));
        Assert.False(File.Exists(target));

        await using var resumed = new DownloadEngine();
        await resumed.StartAsync(url, _dir, 1);

        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(target)));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public async Task TwoDownloadsOfTheSameNameGetDifferentPartFiles()
    {
        var a = Data(300_000, 6);
        var b = Data(300_001, 7);
        using var sa = AuditStressTests.RangeServer(a);
        using var sb = AuditStressTests.RangeServer(b);

        var prober = new NetworkProber();
        var first = await prober.ProbeAsync(sa.Prefix + "same.bin", _dir);
        var second = await prober.ProbeAsync(sb.Prefix + "same.bin", _dir);

        Assert.NotEqual(first.TargetFilePath, second.TargetFilePath);
        Assert.NotEqual(first.PartFilePath, second.PartFilePath);
    }
}
