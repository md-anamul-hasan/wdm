namespace Wdm.Core.Models;

/// <summary>
/// Decides whether a daily wall-clock time (HH:mm) was reached between two observations of the clock.
/// Unlike comparing the current minute for equality, this still fires after sleep/hibernate, a busy
/// dispatcher or a daylight-saving jump that skips the scheduled time.
/// </summary>
public static class ScheduleTrigger
{
    /// <summary>A trigger noticed later than this after its time is ignored (e.g. waking at 14:00 for a 02:00 job).</summary>
    public static readonly TimeSpan DefaultMaxLateness = TimeSpan.FromMinutes(30);

    /// <param name="previous">Local time of the previous check.</param>
    /// <param name="now">Local time of this check.</param>
    /// <param name="time">Scheduled time of day as HH:mm (see <see cref="AppSettings.NormalizeTime"/>).</param>
    public static bool Crossed(DateTime previous, DateTime now, string time, TimeSpan? maxLateness = null)
    {
        if (now <= previous) return false; // clock set back: nothing new can have been reached
        if (!TimeSpan.TryParseExact(time, new[] { @"h\:mm", @"hh\:mm" }, null, out var at)) return false;

        var limit = maxLateness ?? DefaultMaxLateness;
        for (var day = previous.Date; day <= now.Date; day = day.AddDays(1))
        {
            var moment = day + at;
            if (moment > previous && moment <= now && now - moment <= limit) return true;
        }
        return false;
    }
}
