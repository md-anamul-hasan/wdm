using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Tests;

/// <summary>
/// Regression tests added by the engineering audit (docs/audit/ISSUE_REGISTER.md). Each test names its issue ID.
/// </summary>
[Collection("NamedPipe")]
public class AuditFaultInjectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_audit_{Guid.NewGuid():N}");

    public AuditFaultInjectionTests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Standards-following range server. <paramref name="onGet"/> may override a GET before the default handling.</summary>
    private static TestServer RangeServer(byte[] data, Func<HttpListenerContext, long, Task<bool>>? onGet = null)
    {
        return new TestServer(async ctx =>
        {
            var res = ctx.Response;
            res.Headers["Accept-Ranges"] = "bytes";
            res.Headers["ETag"] = "\"audit-etag\"";

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
            if (header is { Length: > 6 } && header.StartsWith("bytes="))
            {
                var parts = header[6..].Split('-');
                start = long.Parse(parts[0]);
                if (parts.Length > 1 && parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), data.Length - 1);
                ranged = true;
            }

            if (onGet != null && await onGet(ctx, start)) return;

            res.StatusCode = ranged ? 206 : 200;
            if (ranged) res.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            res.ContentLength64 = end - start + 1;
            await res.OutputStream.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)));
            res.Close();
        });
    }

    // AUD-001: resuming after the partial file disappeared must not report a zero-filled file as complete.
    [Fact]
    public async Task Resume_WhenPartialFileWasDeleted_RestartsInsteadOfCompletingWithZeros()
    {
        var data = Data(4 * 1024 * 1024, 1);
        using var server = RangeServer(data);
        string url = server.Prefix + "gone.bin";

        using var pause = new CancellationTokenSource();
        var first = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        first.ProgressUpdated += p => { if (p.DownloadedBytes >= 512 * 1024) pause.Cancel(); };
        try { await first.StartAsync(url, _dir, connectionCount: 4, pause.Token); } catch (OperationCanceledException) { }
        await first.DisposeAsync();

        string target = first.Metadata!.TargetFilePath;
        Assert.True(File.Exists(ResumeManager.GetCheckpointPath(target)));
        File.Delete(ResumeManager.GetPartPath(target)); // user clean-up, antivirus quarantine, removable drive...

        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, connectionCount: 4);

        Assert.Equal(DownloadStatus.Completed, second.Status);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(second.Metadata!.TargetFilePath)));
    }

    // AUD-001b: a partial file whose length no longer matches the checkpoint is not trusted either.
    [Fact]
    public async Task Resume_WhenPartialFileWasTruncated_RestartsAndMatchesSource()
    {
        var data = Data(4 * 1024 * 1024, 2);
        using var server = RangeServer(data);
        string url = server.Prefix + "short.bin";

        using var pause = new CancellationTokenSource();
        var first = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        first.ProgressUpdated += p => { if (p.DownloadedBytes >= 512 * 1024) pause.Cancel(); };
        try { await first.StartAsync(url, _dir, connectionCount: 4, pause.Token); } catch (OperationCanceledException) { }
        await first.DisposeAsync();

        using (var fs = new FileStream(ResumeManager.GetPartPath(first.Metadata!.TargetFilePath), FileMode.Open, FileAccess.Write, FileShare.None))
        {
            fs.SetLength(1000);
        }

        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, connectionCount: 4);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(second.Metadata!.TargetFilePath)));
    }

    // AUD-002: a 206 whose Content-Range does not start where requested must never be written at that offset.
    [Fact]
    public async Task Download_WhenServerReturnsWrongContentRange_FailsInsteadOfCorrupting()
    {
        var data = Data(4 * 1024 * 1024, 3);
        using var server = RangeServer(data, async (ctx, start) =>
        {
            if (start == 0) return false;
            // Claims to answer the requested range but sends bytes from offset 0
            ctx.Response.StatusCode = 206;
            ctx.Response.Headers["Content-Range"] = $"bytes 0-{data.Length - start - 1}/{data.Length}";
            ctx.Response.ContentLength64 = data.Length - start;
            await ctx.Response.OutputStream.WriteAsync(data.AsMemory(0, (int)(data.Length - start)));
            ctx.Response.Close();
            return true;
        });

        await using var engine = new DownloadEngine();
        var failure = await Record.ExceptionAsync(() => engine.StartAsync(server.Prefix + "bad.bin", _dir, connectionCount: 4));

        Assert.NotNull(failure);
        Assert.Equal(DownloadStatus.Error, engine.Status);
    }

    // AUD-003: permanent HTTP errors must surface quickly instead of burning ~46 s of backoff.
    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Download_WhenServerRevokesAccessMidway_FailsFast(int status)
    {
        var data = Data(2 * 1024 * 1024, 4);
        using var server = RangeServer(data, async (ctx, start) =>
        {
            if (ctx.Request.Headers["Range"] == null) return false;
            ctx.Response.StatusCode = status;
            ctx.Response.Close();
            await Task.CompletedTask;
            return true;
        });

        await using var engine = new DownloadEngine();
        var clock = Stopwatch.StartNew();
        var failure = await Record.ExceptionAsync(() => engine.StartAsync(server.Prefix + "x.bin", _dir, connectionCount: 2));

        Assert.NotNull(failure);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    // AUD-004: the target path is derived from the probe, never from the (user-writable) checkpoint JSON.
    [Fact]
    public async Task Resume_IgnoresTargetFilePathStoredInCheckpoint()
    {
        var data = Data(4 * 1024 * 1024, 5);
        using var server = RangeServer(data);
        string url = server.Prefix + "tamper.bin";

        using var pause = new CancellationTokenSource();
        var first = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        first.ProgressUpdated += p => { if (p.DownloadedBytes >= 512 * 1024) pause.Cancel(); };
        try { await first.StartAsync(url, _dir, connectionCount: 4, pause.Token); } catch (OperationCanceledException) { }
        await first.DisposeAsync();

        string target = first.Metadata!.TargetFilePath;
        string elsewhere = Path.Combine(_dir, "elsewhere", "evil.bin");
        string statePath = ResumeManager.GetCheckpointPath(target);
        var state = JsonSerializer.Deserialize<DownloadMetadata>(await File.ReadAllTextAsync(statePath))!;
        state.TargetFilePath = elsewhere;
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state));

        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, connectionCount: 4);

        Assert.Equal(target, second.Metadata!.TargetFilePath);
        Assert.False(File.Exists(elsewhere));
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(target)));
    }

    // AUD-004b: segments in a damaged checkpoint that fall outside the file are rejected, not written.
    [Fact]
    public async Task Resume_WithOutOfRangeSegments_RestartsCleanly()
    {
        var data = Data(4 * 1024 * 1024, 6);
        using var server = RangeServer(data);
        string url = server.Prefix + "seg.bin";

        using var pause = new CancellationTokenSource();
        var first = new DownloadEngine(maxBytesPerSecond: 1024 * 1024);
        first.ProgressUpdated += p => { if (p.DownloadedBytes >= 512 * 1024) pause.Cancel(); };
        try { await first.StartAsync(url, _dir, connectionCount: 4, pause.Token); } catch (OperationCanceledException) { }
        await first.DisposeAsync();

        string statePath = ResumeManager.GetCheckpointPath(first.Metadata!.TargetFilePath);
        var state = JsonSerializer.Deserialize<DownloadMetadata>(await File.ReadAllTextAsync(statePath))!;
        state.Segments[^1].EndOffset = data.Length * 4L;
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state));

        await using var second = new DownloadEngine();
        await second.StartAsync(url, _dir, connectionCount: 4);
        Assert.Equal(Sha(data), Sha(await File.ReadAllBytesAsync(second.Metadata!.TargetFilePath)));
    }

    // AUD-005: filename sanitising must not depend on the host OS's idea of invalid characters.
    [Theory]
    [InlineData("..\\..\\evil.dll", "....evil.dll")]
    [InlineData("../../evil.dll", "....evil.dll")]
    [InlineData("a:b*c?.txt", "abc.txt")]
    public void SanitizeFileName_StripsTraversalOnEveryPlatform(string input, string expected)
    {
        Assert.Equal(expected, NetworkProber.SanitizeFileName(input));
    }

    // AUD-006: one client that connects and goes silent must not block the IPC listener.
    [Fact]
    public async Task Pipe_SilentClient_DoesNotBlockLaterRequests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var received = new TaskCompletionSource<ExternalDownloadRequest>();
        var listener = WdmPipe.ListenAsync(r => received.TrySetResult(r), cts.Token, readTimeout: TimeSpan.FromMilliseconds(500));

        await using var silent = new NamedPipeClientStream(".", WdmPipe.Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        await silent.ConnectAsync(5000);
        // ...and never writes or closes.

        bool delivered = false;
        var sent = new ExternalDownloadRequest { Urls = { "https://example.com/after-silent.bin" } };
        // A send can be accepted by the dead instance's socket backlog and lost, so keep sending until the listener has it
        for (int i = 0; i < 60 && !received.Task.IsCompleted; i++)
        {
            delivered |= await WdmPipe.TrySendAsync(sent, 300);
            await Task.Delay(150);
        }

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("https://example.com/after-silent.bin", got.Urls[0]);

        cts.Cancel();
        try { await listener; } catch (OperationCanceledException) { }
    }

    // AUD-007: URLs received over IPC are re-validated; only http(s) reaches the UI.
    [Fact]
    public async Task Pipe_DropsNonHttpUrlsFromRequests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var received = new TaskCompletionSource<ExternalDownloadRequest>();
        var listener = WdmPipe.ListenAsync(r => received.TrySetResult(r), cts.Token);

        var sent = new ExternalDownloadRequest
        {
            Urls = { "file:///C:/Windows/System32/config/SAM", "ftp://example.com/x", "https://example.com/ok.bin" }
        };
        bool delivered = false;
        for (int i = 0; i < 30 && !delivered; i++) delivered = await WdmPipe.TrySendAsync(sent, 500);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "https://example.com/ok.bin" }, got.Urls);

        cts.Cancel();
        try { await listener; } catch (OperationCanceledException) { }
    }

    // AUD-008: CR/LF in browser-supplied header values must never smuggle an extra header onto the wire.
    [Fact]
    public async Task Request_WithCrLfInCookies_CannotInjectHeaders()
    {
        string? injected = "unset";
        using var server = new TestServer(ctx =>
        {
            injected = ctx.Request.Headers["X-Injected"];
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        using var client = HttpRequestHelper.CreateClient();
        var options = new DownloadRequestOptions { Cookies = "a=b\r\nX-Injected: 1", Referer = "http://x/\r\nX-Injected: 1" };

        try
        {
            using var response = await HttpRequestHelper.SendAsync(client, HttpMethod.Get, server.Prefix, options, null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException) { }

        Assert.NotEqual("1", injected);
    }

    // AUD-009: the store-published and sideloaded extension IDs are both authorised by default.
    [Fact]
    public void NativeHostManifest_AuthorisesStoreAndSideloadedExtensions()
    {
        var origins = NativeHostManifest.AllowedOrigins(Array.Empty<string>());

        Assert.Contains("chrome-extension://flnbghmpkajibfdfhbcfhkcjkhlokfgi/", origins);
        Assert.Contains("chrome-extension://kdjgcdbahccolkkagbckkchfpcchcfne/", origins);
        Assert.All(origins, o => Assert.DoesNotContain("*", o));
    }

    [Theory]
    [InlineData("flnbghmpkajibfdfhbcfhkcjkhlokfgi", true)]
    [InlineData("FLNBGHMPKAJIBFDFHBCFHKCJKHLOKFGI", false)]
    [InlineData("short", false)]
    [InlineData("zlnbghmpkajibfdfhbcfhkcjkhlokfgi", false)]
    [InlineData(null, false)]
    public void ChromeExtensionId_Validation(string? id, bool valid)
    {
        Assert.Equal(valid, NativeHostManifest.IsChromeExtensionId(id));
    }
}
