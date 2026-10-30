using System.Security.Cryptography;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Tests;

/// <summary>2.0: optional SHA-256 verification of finished downloads.</summary>
public class ChecksumTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_sha_{Guid.NewGuid():N}");

    public ChecksumTests() => Directory.CreateDirectory(_dir);

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

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Theory]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", true)]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", true)]
    [InlineData("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", true)]
    [InlineData("  e3b0c442 98fc1c14 9afbf4c8 996fb924 27ae41e4 649b934c a495991b 7852b855  ", true)]
    [InlineData("e3b0c442", false)]
    [InlineData("zzb0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryNormalize_AcceptsCommonNotations(string? input, bool valid)
    {
        Assert.Equal(valid, Sha256Checksum.TryNormalize(input, out var normalized));
        if (valid) Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", normalized);
    }

    [Fact]
    public async Task MatchingChecksum_CompletesNormally()
    {
        var data = Data(3 * 1024 * 1024, 1);
        using var server = AuditStressTests.RangeServer(data);

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "ok.bin", _dir, 4, options: new DownloadRequestOptions { ExpectedSha256 = "SHA256:" + Sha(data).ToUpperInvariant() });

        Assert.Equal(DownloadStatus.Completed, engine.Status);
        Assert.True(File.Exists(engine.Metadata!.TargetFilePath));
    }

    [Fact]
    public async Task WrongChecksum_FailsKeepsTheDataAndPublishesNothing()
    {
        var data = Data(3 * 1024 * 1024, 2);
        using var server = AuditStressTests.RangeServer(data);
        string wrong = Sha(Data(10, 99));

        await using var engine = new DownloadEngine();
        var failure = await Assert.ThrowsAsync<ChecksumMismatchException>(() =>
            engine.StartAsync(server.Prefix + "bad.bin", _dir, 4, options: new DownloadRequestOptions { ExpectedSha256 = wrong }));

        Assert.Equal(wrong, failure.Expected);
        Assert.Equal(Sha(data), failure.Actual);
        Assert.Equal(DownloadStatus.Error, engine.Status);

        string target = engine.Metadata!.TargetFilePath;
        Assert.False(File.Exists(target)); // never appears under its real name
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(ResumeManager.GetPartPath(target)))); // data kept
        Assert.True(File.Exists(ResumeManager.GetCheckpointPath(target)));
    }

    [Fact]
    public async Task InvalidChecksumText_FailsBeforeAnyRequest()
    {
        int requests = 0;
        using var server = AuditStressTests.RangeServer(Data(1000, 3), onRequest: _ => Interlocked.Increment(ref requests));

        await using var engine = new DownloadEngine();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.StartAsync(server.Prefix + "x.bin", _dir, 2, options: new DownloadRequestOptions { ExpectedSha256 = "not-a-hash" }));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ChecksumSurvivesPauseAndResume()
    {
        var data = Data(4 * 1024 * 1024, 4);
        using var server = AuditStressTests.RangeServer(data);
        string url = server.Prefix + "resume.bin";
        var options = new DownloadRequestOptions { ExpectedSha256 = Sha(data) };

        using var pause = new CancellationTokenSource();
        var first = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        first.ProgressUpdated += p => { if (p.DownloadedBytes >= 512 * 1024) pause.Cancel(); };
        try { await first.StartAsync(url, _dir, 4, pause.Token, options); } catch (OperationCanceledException) { }
        await first.DisposeAsync();

        // Second run passes no options at all: the checksum comes back from the checkpoint
        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, 4);
        Assert.Equal(DownloadStatus.Completed, second.Status);
        Assert.Equal(Sha(data), second.Metadata!.Options!.ExpectedSha256);
    }

    [Fact]
    public void CommandLine_ParsesSha256Switch()
    {
        var req = LaunchArguments.Parse(new[] { "/d", "https://example.com/a.zip", "/sha256", "ABC" });
        Assert.Equal("ABC", req.Sha256);
        Assert.Equal("ABC", req.ToOptions().ExpectedSha256);
    }

    [Fact]
    public void Batch_DoesNotShareOneChecksumAcrossFiles()
    {
        var req = new ExternalDownloadRequest { Urls = { "https://e/a", "https://e/b" }, Sha256 = "abc" };
        Assert.Null(req.ToOptions().ExpectedSha256);
    }
}
