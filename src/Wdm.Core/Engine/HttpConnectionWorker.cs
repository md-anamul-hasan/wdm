using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>Raised for failures that retrying the same request cannot fix.</summary>
public class NonRetryableDownloadException(string message) : Exception(message);

/// <summary>The server advertised byte ranges but answered a ranged request with the whole body.</summary>
public sealed class RangeIgnoredException(string message) : NonRetryableDownloadException(message);

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
    private readonly DownloadRequestOptions? _options;
    private readonly TimeSpan _stallTimeout;
    private const int BufferSize = 64 * 1024; // 64 KB chunk buffer

    public HttpConnectionWorker(
        HttpClient httpClient,
        StorageWriter storageWriter,
        SpeedLimiter speedLimiter,
        Action<long> onBytesRead,
        DownloadRequestOptions? options = null,
        TimeSpan? stallTimeout = null)
    {
        _options = options;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
        _httpClient = httpClient;
        _storageWriter = storageWriter;
        _speedLimiter = speedLimiter;
        _onBytesRead = onBytesRead;
    }

    private static bool IsPermanentClientError(HttpStatusCode code) =>
        (int)code is >= 400 and < 500 && code != HttpStatusCode.RequestTimeout && (int)code != 429;

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
                // Range request if end offset is set and positive
                RangeHeaderValue? range = null;
                if (!segment.IsOpenEnded && segment.EndOffset >= segment.CurrentOffset)
                {
                    range = new RangeHeaderValue(segment.CurrentOffset, segment.EndOffset);
                }
                else if (segment.CurrentOffset > 0)
                {
                    range = new RangeHeaderValue(segment.CurrentOffset, null);
                }

                using var response = await HttpRequestHelper.SendAsync(
                    _httpClient, HttpMethod.Get, url, _options, range, cancellationToken);

                if (response.StatusCode != HttpStatusCode.PartialContent &&
                    response.StatusCode != HttpStatusCode.OK)
                {
                    string message = $"Server returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    // Access revoked, link expired, file gone or range unsatisfiable: asking again changes nothing
                    if (IsPermanentClientError(response.StatusCode)) throw new NonRetryableDownloadException(message);
                    throw new HttpRequestException(message);
                }

                // Bytes are written at an absolute offset, so a 206 that starts elsewhere than requested would corrupt the file
                if (range?.Ranges.FirstOrDefault()?.From is long requestedStart &&
                    response.StatusCode == HttpStatusCode.PartialContent &&
                    response.Content.Headers.ContentRange is { From: { } servedStart } &&
                    servedStart != requestedStart)
                {
                    throw new NonRetryableDownloadException(
                        $"The server answered with bytes {servedStart}- instead of the requested {requestedStart}-.");
                }

                // A 200 to a request for a non-zero offset means the server ignored the Range header;
                // writing that body at this offset would silently corrupt the file.
                if (range != null && response.StatusCode == HttpStatusCode.OK && segment.CurrentOffset > 0)
                {
                    throw new RangeIgnoredException("The server does not support resuming (ignored Range request).");
                }

                segment.State = SegmentState.Downloading;

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
                        int bytesRead;
                        using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                        {
                            readCts.CancelAfter(_stallTimeout);
                            try
                            {
                                bytesRead = await stream.ReadAsync(rentBuffer.AsMemory(0, maxToRead), readCts.Token);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                throw new TimeoutException("Connection stalled.");
                            }
                        }

                        if (bytesRead == 0)
                        {
                            // Stream closed by server
                            if (segment.IsOpenEnded) segment.CloseOpenEnd();
                            break;
                        }

                        retryCount = 0;

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

                // Server closed the stream early: back off and re-request the remainder
                if (++retryCount > maxRetries)
                {
                    throw new IOException("Connection closed before the segment finished.");
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryCount), 16)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                segment.State = SegmentState.Idle;
                segment.SpeedBytesPerSecond = 0;
                throw;
            }
            catch (NonRetryableDownloadException)
            {
                segment.State = SegmentState.Error;
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
