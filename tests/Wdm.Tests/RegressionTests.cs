using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Tests;

/// <summary>Minimal in-process HTTP server whose behaviour each test supplies.</summary>
internal sealed class TestServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerContext, Task> _handler;
    public string Prefix { get; }

    public TestServer(Func<HttpListenerContext, Task> handler)
    {
        _handler = handler;
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        Prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(async () =>
                {
                    try { await _handler(context); }
                    catch { try { context.Response.Abort(); } catch { } }
                });
            }
            catch { break; }
        }
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}

public class RegressionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_reg_{Guid.NewGuid():N}");

    public RegressionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task UnknownLength_ChunkedResponse_DownloadsEveryByte()
    {
        var data = new byte[3 * 1024 * 1024 + 123];
        new Random(7).NextBytes(data);

        using var server = new TestServer(async ctx =>
        {
            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.SendChunked = true;
                ctx.Response.Close();
                return;
            }

            ctx.Response.SendChunked = true; // no Content-Length, no Accept-Ranges
            await ctx.Response.OutputStream.WriteAsync(data);
            ctx.Response.Close();
        });

        await using var engine = new DownloadEngine();
        await engine.StartAsync(server.Prefix + "stream.bin", _dir, connectionCount: 4);

        Assert.Equal(DownloadStatus.Completed, engine.Status);
        var path = engine.Metadata!.TargetFilePath;
        Assert.Equal(data.Length, new FileInfo(path).Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        Assert.Equal(data.Length, engine.Metadata.TotalBytes);
    }

    [Fact]
    public async Task PauseAndResume_OnServerIgnoringRange_RestartsAndCompletes()
    {
        var data = new byte[4 * 1024 * 1024];
        new Random(11).NextBytes(data);
        var lastModified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        using var server = new TestServer(async ctx =>
        {
            // Like python's http.server: Last-Modified, no Accept-Ranges, Range ignored
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Last-Modified"] = lastModified.ToString("R");
            ctx.Response.ContentLength64 = data.Length;
            if (ctx.Request.HttpMethod != "HEAD") await ctx.Response.OutputStream.WriteAsync(data);
            ctx.Response.Close();
        });

        string url = server.Prefix + "norange.bin";
        using var pause = new CancellationTokenSource();
        var first = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        first.ProgressUpdated += p =>
        {
            if (p.DownloadedBytes >= 256 * 1024) pause.Cancel();
        };

        try { await first.StartAsync(url, _dir, connectionCount: 4, pause.Token); }
        catch (OperationCanceledException) { }
        await first.DisposeAsync();

        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, connectionCount: 4);

        Assert.Equal(DownloadStatus.Completed, second.Status);
        var bytes = await File.ReadAllBytesAsync(second.Metadata!.TargetFilePath);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task SpeedLimiter_LimitSmallerThanOneRead_DoesNotHang()
    {
        var limiter = new SpeedLimiter(10 * 1024); // 10 KB/s, reads are 64 KB
        var work = Task.Run(async () =>
        {
            await limiter.ThrottleAsync(64 * 1024);
            await limiter.ThrottleAsync(64 * 1024);
        });

        await work.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(work.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SpeedLimiter_DisabledWhileWaiting_ReleasesTheWaiter()
    {
        var limiter = new SpeedLimiter(1024);
        await limiter.ThrottleAsync(64 * 1024); // overdraws the bucket

        var waiter = limiter.ThrottleAsync(64 * 1024).AsTask();
        await Task.Delay(200);
        limiter.MaxBytesPerSecond = 0;

        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(waiter.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SameFileNameFromDifferentUrls_NeverShareATargetFile()
    {
        using var server = new TestServer(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = 10;
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"same.bin\"";
            if (ctx.Request.HttpMethod != "HEAD") ctx.Response.OutputStream.Write(new byte[10]);
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        var prober = new NetworkProber();
        var first = await prober.ProbeAsync(server.Prefix + "a", _dir);
        var second = await prober.ProbeAsync(server.Prefix + "b", _dir);

        Assert.NotEqual(first.TargetFilePath, second.TargetFilePath);
        Assert.Equal("same.bin", Path.GetFileName(first.TargetFilePath));
        Assert.Equal("same (1).bin", Path.GetFileName(second.TargetFilePath));
    }

    [Fact]
    public async Task Cookies_AreOnlySentToTheirOwnHost()
    {
        string? seenCookie = null;
        using var server = new TestServer(ctx =>
        {
            seenCookie = ctx.Request.Headers["Cookie"];
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        using var client = HttpRequestHelper.CreateClient();

        var foreign = new DownloadRequestOptions { Cookies = "sid=secret", CookieHost = "other.example" };
        using (await HttpRequestHelper.SendAsync(client, HttpMethod.Get, server.Prefix, foreign, null, CancellationToken.None)) { }
        Assert.Null(seenCookie);

        var own = new DownloadRequestOptions { Cookies = "sid=secret", CookieHost = "127.0.0.1" };
        using (await HttpRequestHelper.SendAsync(client, HttpMethod.Get, server.Prefix, own, null, CancellationToken.None)) { }
        Assert.Equal("sid=secret", seenCookie);
    }

    [Theory]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("..", "download.dat")]
    [InlineData("report. ", "report")]
    [InlineData("a/b\\c.zip", "abc.zip")]
    public void SanitizeFileName_RemovesUnsafeNames(string input, string expected)
    {
        Assert.Equal(expected, NetworkProber.SanitizeFileName(input));
    }

    [Theory]
    [InlineData(".flv", FileCategory.Video)]
    [InlineData("mp3", FileCategory.Music)]
    [InlineData(".ISO", FileCategory.Compressed)]
    [InlineData(".xls", FileCategory.Documents)]
    [InlineData(".msi", FileCategory.Programs)]
    [InlineData(".png", FileCategory.Images)]
    [InlineData(".unknown", FileCategory.General)]
    [InlineData("", FileCategory.General)]
    public void FileCategories_MapExtensions(string extension, FileCategory expected)
    {
        Assert.Equal(expected, FileCategories.FromExtension(extension));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
    [InlineData("https://evil.example/?next=youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://example.com/file.zip", false)]
    public void IsYouTubeUrl_OnlyMatchesRealYouTubeLinks(string url, bool expected)
    {
        Assert.Equal(expected, MediaResolver.IsYouTubeUrl(url));
    }

    [Theory]
    [InlineData("12.50MiB", 13107200)]
    [InlineData("450KiB", 460800)]
    [InlineData("2.00GiB", 2147483648)]
    public void ParseBytes_UnderstandsYtDlpSizes(string text, long expected)
    {
        Assert.Equal(expected, (long)MediaResolver.ParseBytes(text));
    }

    [Theory]
    [InlineData("2:00", "02:00")]
    [InlineData("23:59", "23:59")]
    [InlineData("25:00", "fallback")]
    [InlineData("soon", "fallback")]
    public void NormalizeTime_AcceptsOnlyTimesOfDay(string input, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeTime(input, "fallback"));
    }

    [Fact]
    public void LaunchArguments_ResolvesRelativeDestination()
    {
        var request = LaunchArguments.Parse(new[] { "/d", "https://example.com/f.zip", "/p", "downloads" });
        Assert.Equal(Path.GetFullPath("downloads"), request.DestinationDirectory);
    }
}
