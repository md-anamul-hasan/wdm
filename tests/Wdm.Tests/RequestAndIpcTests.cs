using System.Net;
using System.Net.Sockets;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;
using Xunit;

namespace Wdm.Tests;

public class RequestAndIpcTests
{
    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    [Fact]
    public void LaunchArguments_ParsesIdmStyleSwitches()
    {
        var req = LaunchArguments.Parse(new[] { "/d", "https://example.com/a.zip", "/p", @"C:\Temp", "/f", "b.zip", "/n" });

        Assert.Equal(new[] { "https://example.com/a.zip" }, req.Urls);
        Assert.Equal(@"C:\Temp", req.DestinationDirectory);
        Assert.Equal("b.zip", req.FileName);
        Assert.True(req.Silent);
        Assert.False(req.QueueOnly);
    }

    [Fact]
    public void LaunchArguments_AcceptsBareUrl_AndRejectsOtherSchemes()
    {
        var req = LaunchArguments.Parse(new[] { "\"https://example.com/x.iso\"", "file:///c:/windows/system32/cmd.exe", "javascript:alert(1)" });

        Assert.Equal(new[] { "https://example.com/x.iso" }, req.Urls);
    }

    [Fact]
    public async Task Pipe_DeliversRequestToListener()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource<ExternalDownloadRequest>();
        var listener = WdmPipe.ListenAsync(r => received.TrySetResult(r), cts.Token);

        var sent = new ExternalDownloadRequest { Urls = { "https://example.com/f.bin" }, Referer = "https://example.com/", Cookies = "a=b" };
        bool delivered = false;
        for (int i = 0; i < 20 && !delivered; i++)
        {
            delivered = await WdmPipe.TrySendAsync(sent, 500);
        }

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(delivered);
        Assert.Equal("https://example.com/f.bin", got.Urls[0]);
        Assert.Equal("a=b", got.Cookies);

        cts.Cancel();
        await listener;
    }

    [Fact]
    public void DownloadStore_RoundTripsRecords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wdm_store_{Guid.NewGuid():N}.json");
        try
        {
            var store = new DownloadStore(path);
            store.Save(new[]
            {
                new DownloadRecord
                {
                    Url = "https://example.com/a.zip", FileName = "a.zip", Status = "Paused", TotalBytes = 100, DownloadedBytes = 40,
                    Options = new DownloadRequestOptions { Referer = "https://example.com/" }
                }
            });

            var loaded = Assert.Single(new DownloadStore(path).Load());
            Assert.Equal("a.zip", loaded.FileName);
            Assert.Equal(40, loaded.DownloadedBytes);
            Assert.Equal("https://example.com/", loaded.Options?.Referer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Redirect_DoesNotLeakCookiesToOtherHost_ButKeepsThemOnSameHost()
    {
        // 127.0.0.1 vs "localhost" are different hosts for the purposes of credential scoping
        int port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var seenCookies = new Dictionary<string, string?>();
        using var cts = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }

                seenCookies[ctx.Request.Url!.Host + ctx.Request.Url.AbsolutePath] = ctx.Request.Headers["Cookie"];
                if (ctx.Request.Url.AbsolutePath == "/start")
                {
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = $"http://localhost:{port}/final";
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                }
                ctx.Response.Close();
            }
        });

        using var client = HttpRequestHelper.CreateClient();
        using var response = await HttpRequestHelper.SendAsync(
            client, HttpMethod.Get, $"http://127.0.0.1:{port}/start",
            new DownloadRequestOptions { Cookies = "session=secret", Referer = "https://origin.example/" }, null, CancellationToken.None);

        cts.Cancel();
        listener.Close();
        await server;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("session=secret", seenCookies["127.0.0.1/start"]);
        Assert.Null(seenCookies["localhost/final"]);
    }
}
