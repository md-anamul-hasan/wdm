using System.Diagnostics;

namespace Wdm.Core.Engine;

/// <summary>
/// Thread-safe token-bucket rate limiter for bandwidth throttling. A single instance can be shared by
/// several engines to enforce a global limit. Callers may overdraw the bucket (token debt), so a read
/// larger than one second of budget is delayed instead of waiting forever.
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
                long limit = Math.Max(0, value);
                Interlocked.Exchange(ref _bytesPerSecond, limit);
                _availableTokens = Math.Min(_availableTokens, limit);
                _lastRefillTicks = Stopwatch.GetTimestamp();
            }
        }
    }

    public bool IsEnabled => MaxBytesPerSecond > 0;

    /// <param name="maxBytesPerSecond">Max allowed bytes per second. 0 disables limiting.</param>
    public SpeedLimiter(long maxBytesPerSecond = 0)
    {
        _bytesPerSecond = Math.Max(0, maxBytesPerSecond);
        _availableTokens = _bytesPerSecond;
        _lastRefillTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>Consumes <paramref name="bytes"/> of budget, waiting while the bucket is in debt.</summary>
    public async ValueTask ThrottleAsync(int bytes, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan delay;
            lock (_lock)
            {
                long rate = _bytesPerSecond;
                if (rate <= 0) return; // disabled, possibly while this call was waiting

                Refill(rate);

                if (_availableTokens > 0)
                {
                    _availableTokens -= bytes;
                    return;
                }

                delay = TimeSpan.FromSeconds(Math.Clamp(-_availableTokens / rate, 0.001, 1.0));
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Refill(long rate)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(_lastRefillTicks, now).TotalSeconds;
        if (elapsedSeconds <= 0) return;

        _lastRefillTicks = now;
        _availableTokens = Math.Min(rate, _availableTokens + elapsedSeconds * rate);
    }
}
