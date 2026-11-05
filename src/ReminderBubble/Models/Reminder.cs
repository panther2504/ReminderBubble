namespace ReminderBubble.Models;

/// <summary>A reminder as stored in SQLite and sent to the web UI (camelCase JSON, UTC timestamps).</summary>
public sealed class Reminder
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime DueAt { get; set; }
    /// <summary>none | daily | weekdays | weekly | monthly | yearly</summary>
    public string Repeat { get; set; } = "none";
    /// <summary>violet | blue | teal | green | amber | rose</summary>
    public string Color { get; set; } = "violet";
    /// <summary>0 = low, 1 = normal, 2 = high</summary>
    public int Priority { get; set; } = 1;
    public bool IsDone { get; set; }
    /// <summary>True once the alert for the current due time has been shown.</summary>
    public bool Notified { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The editable part of a reminder, as submitted by the UI.</summary>
public sealed class ReminderInput
{
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime DueAt { get; set; }
    public string Repeat { get; set; } = "none";
    public string Color { get; set; } = "violet";
    public int Priority { get; set; } = 1;
}
