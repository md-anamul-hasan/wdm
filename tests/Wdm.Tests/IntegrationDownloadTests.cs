using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Wdm.Core.Engine;
using Wdm.Core.Models;
using Xunit;

namespace Wdm.Tests;

public class IntegrationDownloadTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _serverPrefix;
    private readonly byte[] _testData;
    private readonly string _testDataHash;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;
    private readonly string _tempDownloadDir;

    public IntegrationDownloadTests()
    {
        // 1. Generate 8 MB of deterministic test data
        _testData = new byte[8 * 1024 * 1024];
        new Random(42).NextBytes(_testData);
        _testDataHash = Convert.ToHexString(SHA256.HashData(_testData));

        // 2. Allocate clean ephemeral port
        int port = GetFreePort();
        _serverPrefix = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_serverPrefix);
        _listener.Start();

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(RunServerAsync);

        _tempDownloadDir = Path.Combine(Path.GetTempPath(), $"wdm_integ_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDownloadDir);
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private async Task RunServerAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch { }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        res.Headers["Accept-Ranges"] = "bytes";
        res.Headers["ETag"] = "\"test-etag-12345\"";

        if (req.HttpMethod == "HEAD")
        {
            res.StatusCode = 200;
            res.ContentLength64 = _testData.Length;
            res.Close();
            return;
        }

        if (req.HttpMethod == "GET")
        {
            var rangeHeader = req.Headers["Range"];
            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
            {
                // Parse Range: bytes=start-end
                var parts = rangeHeader[6..].Split('-');
                long start = long.Parse(parts[0]);
                long end = parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                    ? long.Parse(parts[1])
                    : _testData.Length - 1;

                end = Math.Min(end, _testData.Length - 1);
                long length = end - start + 1;

                res.StatusCode = 206; // Partial Content
                res.Headers["Content-Range"] = $"bytes {start}-{end}/{_testData.Length}";
                res.ContentLength64 = length;
                res.OutputStream.Write(_testData, (int)start, (int)length);
                res.Close();
                return;
            }

            // Normal full GET
            res.StatusCode = 200;
            res.ContentLength64 = _testData.Length;
            res.OutputStream.Write(_testData, 0, _testData.Length);
            res.Close();
        }
    }

    [Fact]
    public async Task FullDownload_WithMultipleConnections_MatchesOriginalHash()
    {
        string downloadUrl = _serverPrefix + "testfile.bin";
        await using var engine = new DownloadEngine();

        bool progressReceived = false;
        engine.ProgressUpdated += p =>
        {
            if (p.DownloadedBytes > 0) progressReceived = true;
        };

        await engine.StartAsync(downloadUrl, _tempDownloadDir, connectionCount: 4);

        Assert.True(progressReceived, "Engine should report real-time progress");
        Assert.Equal(DownloadStatus.Completed, engine.Status);
        Assert.NotNull(engine.Metadata);

        string downloadedFile = engine.Metadata.TargetFilePath;
        Assert.True(File.Exists(downloadedFile), "Target file must exist on disk");

        byte[] downloadedBytes = await File.ReadAllBytesAsync(downloadedFile);
        string downloadedHash = Convert.ToHexString(SHA256.HashData(downloadedBytes));

        Assert.Equal(_testData.Length, downloadedBytes.Length);
        Assert.Equal(_testDataHash, downloadedHash);
    }

    [Fact]
    public async Task PauseAndResume_CompletesWithValidHash()
    {
        string downloadUrl = _serverPrefix + "testfile_resume.bin";
        
        // 1. Start download with speed throttle to ensure we can pause midway
        var engine1 = new DownloadEngine(maxBytesPerSecond: 2 * 1024 * 1024); // 2 MB/s
        using var pauseCts = new CancellationTokenSource();

        engine1.ProgressUpdated += p =>
        {
            if (p.DownloadedBytes >= 1024 * 1024) // Once at least 1 MB is downloaded
            {
                pauseCts.Cancel();
            }
        };

        try
        {
            await engine1.StartAsync(downloadUrl, _tempDownloadDir, connectionCount: 4, pauseCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected pause
        }
        await engine1.DisposeAsync();

        string expectedPath = Path.Combine(_tempDownloadDir, "testfile_resume.bin");
        string checkpointPath = ResumeManager.GetCheckpointPath(expectedPath);
        Assert.True(File.Exists(checkpointPath), "Checkpoint file should exist after pause");

        // 2. Resume download with engine2 without speed throttle
        await using var engine2 = new DownloadEngine();
        await engine2.StartAsync(downloadUrl, _tempDownloadDir, connectionCount: 4);

        Assert.Equal(DownloadStatus.Completed, engine2.Status);
        Assert.False(File.Exists(checkpointPath), "Checkpoint file should be cleaned up on completion");

        byte[] finalBytes = await File.ReadAllBytesAsync(expectedPath);
        string finalHash = Convert.ToHexString(SHA256.HashData(finalBytes));

        Assert.Equal(_testData.Length, finalBytes.Length);
        Assert.Equal(_testDataHash, finalHash);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        _cts.Dispose();

        if (Directory.Exists(_tempDownloadDir))
        {
            try { Directory.Delete(_tempDownloadDir, recursive: true); } catch { }
        }
    }
}
