using ReminderBubble.Services;
using Xunit;

namespace ReminderBubble.Tests;

public sealed class RecurrenceTests
{
    static DateTime Local(int y, int m, int d, int h = 9, int min = 0) =>
        new DateTime(y, m, d, h, min, 0, DateTimeKind.Local).ToUniversalTime();

    static DateTime L(DateTime utc) => utc.ToLocalTime();

    [Theory]
    [InlineData("none", false)]
    [InlineData("daily", true)]
    [InlineData("weekdays", true)]
    [InlineData("weekly", true)]
    [InlineData("monthly", true)]
    [InlineData("yearly", true)]
    [InlineData("garbage", false)]
    public void IsRecurring(string kind, bool expected) => Assert.Equal(expected, Recurrence.IsRecurring(kind));

    [Fact]
    public void None_does_not_move()
    {
        var due = Local(2030, 3, 1);
        Assert.Equal(due, Recurrence.Next(due, "none", Local(2030, 3, 2)));
    }

    [Fact]
    public void Daily_goes_to_the_next_day_at_the_same_local_time()
    {
        var next = Recurrence.Next(Local(2030, 3, 1, 9), "daily", Local(2030, 3, 1, 9, 5));
        Assert.Equal(new DateTime(2030, 3, 2, 9, 0, 0), L(next));
    }

    [Fact]
    public void Daily_skips_every_missed_occurrence()
    {
        // not opened for five days: the next one is still in the future, not five reminders at once
        var next = Recurrence.Next(Local(2030, 3, 1, 9), "daily", Local(2030, 3, 6, 12));
        Assert.Equal(new DateTime(2030, 3, 7, 9, 0, 0), L(next));
    }

    [Fact]
    public void Weekly_adds_seven_days()
    {
        var next = Recurrence.Next(Local(2030, 3, 1, 18, 30), "weekly", Local(2030, 3, 1, 19));
        Assert.Equal(new DateTime(2030, 3, 8, 18, 30, 0), L(next));
    }

    [Fact]
    public void Weekdays_skips_the_weekend()
    {
        var friday = Local(2030, 3, 1, 9);                       // 1 March 2030 is a Friday
        Assert.Equal(DayOfWeek.Friday, L(friday).DayOfWeek);
        var next = Recurrence.Next(friday, "weekdays", friday.AddMinutes(1));
        Assert.Equal(DayOfWeek.Monday, L(next).DayOfWeek);
        Assert.Equal(new DateTime(2030, 3, 4, 9, 0, 0), L(next));
    }

    [Fact]
    public void Monthly_is_anchored_to_the_original_day_so_it_does_not_drift()
    {
        var jan31 = Local(2031, 1, 31);
        var feb = Recurrence.Next(jan31, "monthly", Local(2031, 1, 31, 10));
        Assert.Equal(new DateTime(2031, 2, 28, 9, 0, 0), L(feb));        // clamped to the end of February...
        var mar = Recurrence.Next(jan31, "monthly", Local(2031, 3, 1));
        Assert.Equal(new DateTime(2031, 3, 31, 9, 0, 0), L(mar));        // ...but March is back on the 31st
    }

    [Fact]
    public void Yearly_handles_leap_day()
    {
        var feb29 = Local(2032, 2, 29);
        var next = Recurrence.Next(feb29, "yearly", Local(2032, 3, 1));
        Assert.Equal(new DateTime(2033, 2, 28, 9, 0, 0), L(next));
    }

    [Fact]
    public void Result_is_always_in_the_future()
    {
        var now = DateTime.UtcNow;
        foreach (var kind in new[] { "daily", "weekdays", "weekly", "monthly", "yearly" })
            Assert.True(Recurrence.Next(now.AddYears(-2), kind, now) > now, kind);
    }
}
