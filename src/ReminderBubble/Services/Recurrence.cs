namespace ReminderBubble.Services;

public static class Recurrence
{
    public static readonly string[] Kinds = { "none", "daily", "weekdays", "weekly", "monthly", "yearly" };

    public static bool IsRecurring(string repeat) => repeat is "daily" or "weekdays" or "weekly" or "monthly" or "yearly";

    /// <summary>
    /// The first occurrence strictly after <paramref name="nowUtc"/>, stepping from <paramref name="dueUtc"/> in LOCAL time
    /// so "every day at 9:00" stays at 9:00 across daylight-saving changes.
    /// </summary>
    public static DateTime Next(DateTime dueUtc, string repeat, DateTime nowUtc)
    {
        if (!IsRecurring(repeat)) return dueUtc;
        var anchor = dueUtc.ToLocalTime();
        var n = anchor;
        for (var i = 1; i <= 5000; i++)
        {
            n = repeat switch
            {
                "daily" => anchor.AddDays(i),
                "weekdays" => NextWeekday(n),
                "weekly" => anchor.AddDays(7 * i),
                "monthly" => anchor.AddMonths(i),     // anchored, so Jan 31 -> Feb 28 -> Mar 31
                "yearly" => anchor.AddYears(i),
                _ => n,
            };
            if (n.ToUniversalTime() > nowUtc) break;
        }
        return n.ToUniversalTime();
    }

    static DateTime NextWeekday(DateTime d)
    {
        do { d = d.AddDays(1); } while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return d;
    }
}
