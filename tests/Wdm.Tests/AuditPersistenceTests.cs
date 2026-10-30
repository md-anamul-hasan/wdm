using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Tests;

/// <summary>Corrupt-state and settings-normalisation tests (audit: persistence and recovery).</summary>
public class AuditPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_persist_{Guid.NewGuid():N}");

    public AuditPersistenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task CorruptCheckpoint_LoadsAsNull_AndIsNeverDeleted()
    {
        string target = Path.Combine(_dir, "a.bin");
        string state = ResumeManager.GetCheckpointPath(target);
        await File.WriteAllTextAsync(state, "{ this is not json");

        Assert.Null(await ResumeManager.LoadCheckpointAsync(target));
        Assert.True(File.Exists(state));
    }

    [Fact]
    public async Task Checkpoint_RoundTripsSegmentsAndOptions()
    {
        var meta = new DownloadMetadata
        {
            Url = "https://example.com/f", TargetFilePath = Path.Combine(_dir, "f.bin"), TotalBytes = 100, SupportsRanges = true,
            Segments = { new DownloadSegment(1, 0, 49) { CurrentOffset = 10 }, new DownloadSegment(2, 50, 99) }
        };
        await ResumeManager.SaveCheckpointAsync(meta);

        var loaded = await ResumeManager.LoadCheckpointAsync(meta.TargetFilePath);
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded!.Segments[0].CurrentOffset);
        Assert.True(DynamicSegmenter.IsConsistent(loaded.Segments, 100));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp")); // atomic write leaves no temp files behind
    }

    [Fact]
    public async Task SaveCheckpoint_FlushesDataBeforeWriting()
    {
        var meta = new DownloadMetadata { Url = "u", TargetFilePath = Path.Combine(_dir, "o.bin") };
        bool stateExistedAtFlush = true;
        await ResumeManager.SaveCheckpointAsync(meta, default, () => stateExistedAtFlush = File.Exists(ResumeManager.GetCheckpointPath(meta.TargetFilePath)));
        Assert.False(stateExistedAtFlush); // flush ran first
        Assert.True(File.Exists(ResumeManager.GetCheckpointPath(meta.TargetFilePath)));
    }

    [Fact]
    public void CorruptDownloadList_IsKeptAsideNotOverwritten()
    {
        string path = Path.Combine(_dir, "downloads.json");
        File.WriteAllText(path, "[ broken");
        var store = new DownloadStore(path);

        Assert.Empty(store.Load());
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.Equal("[ broken", File.ReadAllText(path + ".corrupt"));

        store.Save(new[] { new DownloadRecord { Url = "https://example.com/x" } });
        Assert.Single(new DownloadStore(path).Load());
    }

    [Fact]
    public void DownloadList_RoundTrips()
    {
        var store = new DownloadStore(Path.Combine(_dir, "d.json"));
        store.Save(new[] { new DownloadRecord { Url = "https://e/x", FileName = "x", TotalBytes = 5, Status = "Completed" } });
        var loaded = store.Load();
        Assert.Equal("x", loaded[0].FileName);
        Assert.Equal(5, loaded[0].TotalBytes);
    }

    [Fact]
    public void Settings_Normalize_ClampsHandEditedValues()
    {
        var s = new AppSettings
        {
            DefaultConnections = 999, MaxConcurrentDownloads = 0, DefaultSpeedLimitKb = -5, NetworkTimeoutSeconds = 1,
            DefaultDownloadDirectory = " ", UserAgent = "", SchedulerStartTime = "25:99", SchedulerStopTime = "7:5"
        };
        s.Normalize();

        Assert.Equal(32, s.DefaultConnections);
        Assert.Equal(1, s.MaxConcurrentDownloads);
        Assert.Equal(1, s.DefaultSpeedLimitKb);
        Assert.Equal(5, s.NetworkTimeoutSeconds);
        Assert.False(string.IsNullOrWhiteSpace(s.DefaultDownloadDirectory));
        Assert.Equal(HttpRequestHelper.DefaultUserAgent, s.UserAgent);
        Assert.Equal("02:00", s.SchedulerStartTime);
        Assert.Equal("06:00", s.SchedulerStopTime); // "7:5" is not h:mm
    }

    [Fact]
    public void Settings_CloneIsIndependent()
    {
        var a = new AppSettings { DefaultConnections = 5 };
        var b = a.Clone();
        b.DefaultConnections = 9;
        Assert.Equal(5, a.DefaultConnections);
    }

    [Theory]
    [InlineData("12.50MiB", 13107200)]
    [InlineData("450KiB", 460800)]
    [InlineData("1GiB", 1073741824)]
    [InlineData("garbage", 0)]
    public void MediaResolver_ParseBytes(string input, double expected) => Assert.Equal(expected, MediaResolver.ParseBytes(input));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
    [InlineData("https://evil.com/?u=youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ", false)]
    public void MediaResolver_OnlyRealYouTubeHostsMatch(string url, bool expected) => Assert.Equal(expected, MediaResolver.IsYouTubeUrl(url));
}
