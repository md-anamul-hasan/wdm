using System.Diagnostics;

namespace Wdm.Core.Engine;

/// <summary>
/// Thread-safe Token Bucket rate limiter for bandwidth throttling.
/// </summary>
public class SpeedLimiter
{
    private readonly object _lock = new();
    private long _bytesPerSecond;
    private double _availableTokens;
    private long _lastRefillTicks;

    public long MaxBytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set
        {
            lock (_lock)
            {
                Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value));
                _availableTokens = Math.Min(_availableTokens, _bytesPerSecond);
            }
        }
    }

    public bool IsEnabled => MaxBytesPerSecond > 0;

    /// <param name="maxBytesPerSecond">Max allowed bytes per second. 0 disables limiting.</param>
    public SpeedLimiter(long maxBytesPerSecond = 0)
    {
        _bytesPerSecond = maxBytesPerSecond;
        _availableTokens = maxBytesPerSecond;
        _lastRefillTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Requests to consume the given number of bytes, throttling if tokens are insufficient.
    /// </summary>
    public async ValueTask ThrottleAsync(int bytes, CancellationToken cancellationToken = default)
    {
        if (MaxBytesPerSecond <= 0) return;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan delay = TimeSpan.Zero;
            lock (_lock)
            {
                Refill();

                if (_availableTokens >= bytes)
                {
                    _availableTokens -= bytes;
                    return;
                }

                // Compute wait time for enough tokens
                double needed = bytes - _availableTokens;
                double secondsToWait = needed / _bytesPerSecond;
                delay = TimeSpan.FromSeconds(Math.Min(secondsToWait, 1.0));
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Refill()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(_lastRefillTicks, now).TotalSeconds;
        if (elapsedSeconds <= 0) return;

        _lastRefillTicks = now;
        _availableTokens = Math.Min(_bytesPerSecond, _availableTokens + elapsedSeconds * _bytesPerSecond);
    }
}
