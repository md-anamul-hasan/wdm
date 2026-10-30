using Wdm.Core.Models;

namespace Wdm.Tests;

public class ScheduleTriggerTests
{
    private static DateTime T(string s) => DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void FiresWhenTheTimeIsReachedBetweenTwoTicks()
        => Assert.True(ScheduleTrigger.Crossed(T("2026-03-01 01:59:50"), T("2026-03-01 02:00:05"), "02:00"));

    [Fact]
    public void DoesNotFireAgainOnTheNextTick()
        => Assert.False(ScheduleTrigger.Crossed(T("2026-03-01 02:00:05"), T("2026-03-01 02:00:20"), "02:00"));

    [Fact]
    public void FiresAfterSleepThatSkippedTheExactMinute()
        => Assert.True(ScheduleTrigger.Crossed(T("2026-03-01 01:50:00"), T("2026-03-01 02:07:00"), "02:00"));

    [Fact]
    public void IgnoresATriggerNoticedHoursLate()
        => Assert.False(ScheduleTrigger.Crossed(T("2026-03-01 01:50:00"), T("2026-03-01 14:00:00"), "02:00"));

    [Fact]
    public void FiresAcrossMidnight()
        => Assert.True(ScheduleTrigger.Crossed(T("2026-03-01 23:59:50"), T("2026-03-02 00:00:05"), "00:00"));

    [Fact]
    public void FiresWhenSpringForwardJumpSkipsTheScheduledTime()
        => Assert.True(ScheduleTrigger.Crossed(T("2026-03-08 01:59:50"), T("2026-03-08 03:00:05"), "02:30", TimeSpan.FromHours(2)));

    [Fact]
    public void ClockSetBackNeverFires()
        => Assert.False(ScheduleTrigger.Crossed(T("2026-03-01 02:10:00"), T("2026-03-01 01:55:00"), "02:00"));

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("abc")]
    public void InvalidTimesNeverFire(string time)
        => Assert.False(ScheduleTrigger.Crossed(T("2026-03-01 01:00:00"), T("2026-03-01 03:00:00"), time));
}
