using Wdm.Core.Engine;
using Wdm.Core.Models;
using Xunit;

namespace Wdm.Tests;

public class StorageWriterTests : IDisposable
{
    private readonly string _testFile;

    public StorageWriterTests()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"wdm_test_{Guid.NewGuid():N}.dat");
    }

    [Fact]
    public async Task WriteAsync_ConcurrentDisjointWrites_SucceedsWithoutCorruption()
    {
        long fileSize = 1024 * 1024; // 1 MB
        await using (var writer = new StorageWriter(_testFile, fileSize))
        {
            byte[] block1 = new byte[100];
            Array.Fill(block1, (byte)0xAA);

            byte[] block2 = new byte[100];
            Array.Fill(block2, (byte)0xBB);

            // Write block1 at offset 0, block2 at offset 500
            await Task.WhenAll(
                writer.WriteAsync(block1, 0).AsTask(),
                writer.WriteAsync(block2, 500).AsTask()
            );

            writer.Flush();
        }

        // Verify written bytes
        byte[] readBytes = await File.ReadAllBytesAsync(_testFile);
        Assert.Equal(fileSize, readBytes.Length);

        // Verify block1
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(0xAA, readBytes[i]);
        }

        // Verify block2
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(0xBB, readBytes[500 + i]);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_testFile))
        {
            try { File.Delete(_testFile); } catch { }
        }
    }
}

public class ResumeManagerTests : IDisposable
{
    private readonly string _testFile;

    public ResumeManagerTests()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"wdm_resume_{Guid.NewGuid():N}.dat");
    }

    [Fact]
    public async Task SaveAndLoadCheckpoint_RoundtripsSuccessfully()
    {
        var meta = new DownloadMetadata
        {
            Url = "https://example.com/file.zip",
            TargetFilePath = _testFile,
            TotalBytes = 1048576,
            SupportsRanges = true,
            ETag = "\"abc-123\"",
            Segments = new List<DownloadSegment>
            {
                new DownloadSegment(1, 0, 524287) { CurrentOffset = 250000 },
                new DownloadSegment(2, 524288, 1048575) { CurrentOffset = 800000 }
            }
        };

        await ResumeManager.SaveCheckpointAsync(meta);

        var loaded = await ResumeManager.LoadCheckpointAsync(_testFile);
        Assert.NotNull(loaded);
        Assert.Equal(meta.Url, loaded.Url);
        Assert.Equal(meta.TotalBytes, loaded.TotalBytes);
        Assert.Equal(meta.ETag, loaded.ETag);
        Assert.Equal(2, loaded.Segments.Count);
        Assert.Equal(250000, loaded.Segments[0].CurrentOffset);

        ResumeManager.DeleteCheckpoint(_testFile);
        var loadedAfterDelete = await ResumeManager.LoadCheckpointAsync(_testFile);
        Assert.Null(loadedAfterDelete);
    }

    public void Dispose()
    {
        ResumeManager.DeleteCheckpoint(_testFile);
        if (File.Exists(_testFile))
        {
            try { File.Delete(_testFile); } catch { }
        }
    }
}
