using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Tests;

/// <summary>Stress, boundary and degraded-server tests added by the audit. All servers are in-process on loopback.</summary>
public class AuditStressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_stress_{Guid.NewGuid():N}");

    public AuditStressTests() => Directory.CreateDirectory(_dir);

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

    internal static TestServer RangeServer(byte[] data, bool advertiseRanges = true, bool honourRange = true, Action<HttpListenerContext>? onRequest = null)
    {
        return new TestServer(async ctx =>
        {
            onRequest?.Invoke(ctx);
            var res = ctx.Response;
            if (advertiseRanges) res.Headers["Accept-Ranges"] = "bytes";
            res.Headers["ETag"] = "\"stress\"";

            if (ctx.Request.HttpMethod == "HEAD")
            {
                res.StatusCode = 200;
                res.ContentLength64 = data.Length;
                res.Close();
                return;
            }

            long start = 0, end = data.Length - 1;
            bool ranged = false;
            var header = ctx.Request.Headers["Range"];
            if (honourRange && header is { Length: > 6 } && header.StartsWith("bytes="))
            {
                var parts = header[6..].Split('-');
                start = long.Parse(parts[0]);
                if (parts.Length > 1 && parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), data.Length - 1);
                ranged = true;
            }

            res.StatusCode = ranged ? 206 : 200;
            if (ranged) res.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            res.ContentLength64 = end - start + 1;
            await res.OutputStream.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)));
            res.Close();
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public async Task EveryConnectionCount_ProducesByteIdenticalFile(int connections)
    {
        var data = Data(24 * 1024 * 1024 + 4097, connections); // not a multiple of any segment size
        using var server = RangeServer(data);

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "f.bin", _dir, connections);

        Assert.Equal(DownloadStatus.Completed, engine.Status);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(engine.Metadata!.TargetFilePath)));
        Assert.False(File.Exists(ResumeManager.GetCheckpointPath(engine.Metadata.TargetFilePath)));
    }

    [Fact]
    public async Task RepeatedPauseAndResume_NeverCorruptsTheFile()
    {
        // Each engine's limiter starts with a full bucket (one second of budget), so the limit must stay well below
        // the remaining size or a resumed run could finish before the first 500 ms progress tick can pause it.
        var data = Data(16 * 1024 * 1024, 77);
        using var server = RangeServer(data);
        string url = server.Prefix + "cycle.bin";
        string? target = null;

        for (int cycle = 0; cycle < 6; cycle++)
        {
            using var pause = new CancellationTokenSource();
            var engine = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
            long threshold = (cycle + 1) * 1024 * 1024;
            engine.ProgressUpdated += p => { if (p.DownloadedBytes >= threshold) pause.Cancel(); };
            try { await engine.StartAsync(url, _dir, connectionCount: 6, pause.Token); } catch (OperationCanceledException) { }
            target = engine.Metadata?.TargetFilePath ?? target;
            await engine.DisposeAsync();
        }

        await using var last = new DownloadEngine();
        await last.StartAsync(url, _dir, connectionCount: 6);

        Assert.Equal(target, last.Metadata!.TargetFilePath);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(target!)));
    }

    [Fact]
    public async Task TenSimultaneousDownloads_AllByteIdentical()
    {
        var files = Enumerable.Range(0, 10).Select(i => Data(3 * 1024 * 1024 + i * 1000, 100 + i)).ToArray();
        using var server = new TestServer(async ctx =>
        {
            int index = int.Parse(ctx.Request.Url!.AbsolutePath.Trim('/').Split('/')[0]);
            var data = files[index];
            var res = ctx.Response;
            res.Headers["Accept-Ranges"] = "bytes";
            if (ctx.Request.HttpMethod == "HEAD") { res.ContentLength64 = data.Length; res.Close(); return; }

            long start = 0, end = data.Length - 1;
            bool ranged = false;
            var header = ctx.Request.Headers["Range"];
            if (header is { Length: > 6 })
            {
                var parts = header[6..].Split('-');
                start = long.Parse(parts[0]);
                if (parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), data.Length - 1);
                ranged = true;
            }
            res.StatusCode = ranged ? 206 : 200;
            if (ranged) res.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            res.ContentLength64 = end - start + 1;
            await res.OutputStream.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)));
            res.Close();
        });

        var engines = Enumerable.Range(0, 10).Select(_ => new DownloadEngine()).ToArray();
        try
        {
            await Task.WhenAll(engines.Select((e, i) => e.StartAsync($"{server.Prefix}{i}/file{i}.bin", _dir, 4)));
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(Sha(files[i]), Sha(await File.ReadAllBytesAsync(engines[i].Metadata!.TargetFilePath)));
            }
        }
        finally
        {
            foreach (var e in engines) await e.DisposeAsync();
        }
    }

    // AUD-016: Accept-Ranges advertised but ignored must degrade to a single stream, not fail.
    [Fact]
    public async Task ServerAdvertisingButIgnoringRange_FallsBackToSingleStream()
    {
        var data = Data(6 * 1024 * 1024, 5);
        using var server = RangeServer(data, advertiseRanges: true, honourRange: false);

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "liar.bin", _dir, connectionCount: 4);

        Assert.Equal(DownloadStatus.Completed, engine.Status);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(engine.Metadata!.TargetFilePath)));
    }

    // AUD-017: empty files download instead of failing.
    [Fact]
    public async Task EmptyFile_WhenHeadIsRejected_StillDownloads()
    {
        using var server = new TestServer(ctx =>
        {
            var res = ctx.Response;
            if (ctx.Request.HttpMethod == "HEAD") res.StatusCode = 405;
            else if (ctx.Request.Headers["Range"] != null)
            {
                res.StatusCode = 416;
                res.Headers["Content-Range"] = "bytes */0";
            }
            else { res.StatusCode = 200; res.ContentLength64 = 0; }
            res.Close();
            return Task.CompletedTask;
        });

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "empty.txt", _dir, connectionCount: 4);

        Assert.Equal(DownloadStatus.Completed, engine.Status);
        Assert.Equal(0, new FileInfo(engine.Metadata!.TargetFilePath).Length);
    }

    [Fact]
    public async Task EmptyFile_WhenHeadSucceeds_Downloads()
    {
        using var server = new TestServer(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "zero.txt", _dir, connectionCount: 4);
        Assert.Equal(DownloadStatus.Completed, engine.Status);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TransientServerErrors_AreRetriedAndRecover(int status)
    {
        var data = Data(1024 * 1024, 9);
        int failures = 0;
        using var server = new TestServer(async ctx =>
        {
            var res = ctx.Response;
            res.Headers["Accept-Ranges"] = "bytes";
            if (ctx.Request.HttpMethod == "HEAD") { res.ContentLength64 = data.Length; res.Close(); return; }
            if (Interlocked.Increment(ref failures) <= 2) { res.StatusCode = status; res.Close(); return; }
            res.ContentLength64 = data.Length;
            await res.OutputStream.WriteAsync(data);
            res.Close();
        });

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "flaky.bin", _dir, connectionCount: 1);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(engine.Metadata!.TargetFilePath)));
    }

    [Fact]
    public async Task ServerDisconnectingMidway_ResumesRemainderAndMatchesSource()
    {
        var data = Data(4 * 1024 * 1024, 21);
        int gets = 0;
        using var server = new TestServer(async ctx =>
        {
            var res = ctx.Response;
            res.Headers["Accept-Ranges"] = "bytes";
            if (ctx.Request.HttpMethod == "HEAD") { res.ContentLength64 = data.Length; res.Close(); return; }

            long start = 0, end = data.Length - 1;
            bool ranged = false;
            var header = ctx.Request.Headers["Range"];
            if (header is { Length: > 6 })
            {
                var parts = header[6..].Split('-');
                start = long.Parse(parts[0]);
                if (parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), data.Length - 1);
                ranged = true;
            }
            res.StatusCode = ranged ? 206 : 200;
            if (ranged) res.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            long length = end - start + 1;
            res.ContentLength64 = length;

            if (Interlocked.Increment(ref gets) == 1)
            {
                await res.OutputStream.WriteAsync(data.AsMemory((int)start, (int)(length / 3)));
                await res.OutputStream.FlushAsync();
                res.Abort(); // connection dies after a third of the body
                return;
            }
            await res.OutputStream.WriteAsync(data.AsMemory((int)start, (int)length));
            res.Close();
        });

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "cut.bin", _dir, connectionCount: 1);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(engine.Metadata!.TargetFilePath)));
    }

    // AUD-021: the grabber regex runs over untrusted pages (up to 8 MB); it must stay linear on hostile input.
    [Fact]
    public async Task SiteGrabber_HostilePage_FinishesQuickly()
    {
        string hostile = "<a href=\"" + new string('a', 4 * 1024 * 1024) + "\">" + string.Concat(Enumerable.Repeat("\"http://x/" , 200_000));
        using var server = new TestServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html";
            var bytes = System.Text.Encoding.UTF8.GetBytes(hostile);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });

        var clock = Stopwatch.StartNew();
        await new SiteGrabberService().GrabLinksAsync(server.Prefix + "page");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }
}
