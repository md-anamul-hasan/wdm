using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// Worker responsible for downloading an individual byte segment using HTTP range requests.
/// Writes directly to the target file offset via StorageWriter.
/// </summary>
public class HttpConnectionWorker
{
    private readonly HttpClient _httpClient;
    private readonly StorageWriter _storageWriter;
    private readonly SpeedLimiter _speedLimiter;
    private readonly Action<long> _onBytesRead;
    private const int BufferSize = 64 * 1024; // 64 KB chunk buffer

    public HttpConnectionWorker(
        HttpClient httpClient,
        StorageWriter storageWriter,
        SpeedLimiter speedLimiter,
        Action<long> onBytesRead)
    {
        _httpClient = httpClient;
        _storageWriter = storageWriter;
        _speedLimiter = speedLimiter;
        _onBytesRead = onBytesRead;
    }

    public async Task DownloadSegmentAsync(
        string url,
        DownloadSegment segment,
        CancellationToken cancellationToken)
    {
        segment.State = SegmentState.Connecting;

        int retryCount = 0;
        const int maxRetries = 5;

        while (segment.RemainingBytes > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WDM/1.0");
                request.Headers.Add("Accept", "*/*");
                request.Headers.Add("Accept-Encoding", "identity");

                // Range request if end offset is set and positive
                if (segment.EndOffset >= segment.CurrentOffset)
                {
                    request.Headers.Range = new RangeHeaderValue(segment.CurrentOffset, segment.EndOffset);
                }
                else if (segment.CurrentOffset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(segment.CurrentOffset, null);
                }

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                if (response.StatusCode != HttpStatusCode.PartialContent &&
                    response.StatusCode != HttpStatusCode.OK)
                {
                    throw new HttpRequestException($"Server returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                segment.State = SegmentState.Downloading;
                retryCount = 0;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                byte[] rentBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);

                long lastSpeedTimestamp = Stopwatch.GetTimestamp();
                long bytesInPeriod = 0;

                try
                {
                    while (segment.RemainingBytes > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int maxToRead = (int)Math.Min(rentBuffer.Length, segment.RemainingBytes);
                        int bytesRead = await stream.ReadAsync(rentBuffer.AsMemory(0, maxToRead), cancellationToken);

                        if (bytesRead == 0)
                        {
                            // Stream closed by server
                            break;
                        }

                        // Apply speed limiting
                        await _speedLimiter.ThrottleAsync(bytesRead, cancellationToken);

                        // Direct zero-merge write to destination file at absolute segment offset
                        await _storageWriter.WriteAsync(rentBuffer.AsMemory(0, bytesRead), segment.CurrentOffset, cancellationToken);

                        segment.CurrentOffset += bytesRead;
                        bytesInPeriod += bytesRead;
                        _onBytesRead(bytesRead);

                        // Calculate speed every 250ms
                        long now = Stopwatch.GetTimestamp();
                        double elapsed = Stopwatch.GetElapsedTime(lastSpeedTimestamp, now).TotalSeconds;
                        if (elapsed >= 0.25)
                        {
                            segment.SpeedBytesPerSecond = bytesInPeriod / elapsed;
                            bytesInPeriod = 0;
                            lastSpeedTimestamp = now;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentBuffer);
                }

                if (segment.RemainingBytes <= 0)
                {
                    segment.State = SegmentState.Completed;
                    segment.SpeedBytesPerSecond = 0;
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                segment.State = SegmentState.Idle;
                segment.SpeedBytesPerSecond = 0;
                throw;
            }
            catch (Exception)
            {
                retryCount++;
                if (retryCount > maxRetries)
                {
                    segment.State = SegmentState.Error;
                    segment.SpeedBytesPerSecond = 0;
                    throw;
                }

                // Exponential backoff
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryCount), 16)), cancellationToken);
            }
        }

        segment.State = SegmentState.Completed;
        segment.SpeedBytesPerSecond = 0;
    }
}
