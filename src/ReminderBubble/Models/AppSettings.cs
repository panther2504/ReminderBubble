namespace ReminderBubble.Models;

public sealed class AppSettings
{
    /// <summary>dark | light | system</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>violet | blue | teal | green | amber | rose</summary>
    public string Accent { get; set; } = "violet";
    public int BubbleSize { get; set; } = 64;
    public double BubbleOpacity { get; set; } = 1.0;
    /// <summary>Glue the bubble to the left/right screen edge after it is dragged.</summary>
    public bool SnapToEdge { get; set; } = true;
    /// <summary>Slide the bubble half-way off the screen edge when idle.</summary>
    public bool AutoHide { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool Clock24 { get; set; }
    public int DefaultSnooze { get; set; } = 10;
    public bool Welcomed { get; set; }

    // Last bubble position (top-left of the circle, physical pixels). Written by the bubble itself.
    public double? BubbleX { get; set; }
    public double? BubbleY { get; set; }

    public static readonly string[] Themes = { "dark", "light", "system" };
    public static readonly string[] Accents = { "violet", "blue", "teal", "green", "amber", "rose" };

    public void Normalize()
    {
        if (Array.IndexOf(Themes, Theme) < 0) Theme = "dark";
        if (Array.IndexOf(Accents, Accent) < 0) Accent = "violet";
        BubbleSize = Math.Clamp(BubbleSize, 44, 96);
        BubbleOpacity = Math.Clamp(BubbleOpacity, 0.35, 1.0);
        DefaultSnooze = Math.Clamp(DefaultSnooze, 1, 1440);
        if (!SnapToEdge) AutoHide = false;
    }
}
