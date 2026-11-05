namespace ReminderBubble.Models;

public sealed class AppSettings
{
    /// <summary>dark | light | system</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>violet | blue | teal | green | amber | rose</summary>
    public string Accent { get; set; } = "violet";
    /// <summary>Diameter of the bubble in device-independent pixels (resizable with the mouse wheel / grip / settings).</summary>
    public int BubbleSize { get; set; } = 64;
    public double BubbleOpacity { get; set; } = 1.0;
    /// <summary>Glue the bubble to the left/right screen edge after it is dragged. Off by default: it stays wherever it is dropped.</summary>
    public bool SnapToEdge { get; set; }
    /// <summary>Slide the bubble half-way off the screen edge when idle (only with <see cref="SnapToEdge"/>).</summary>
    public bool AutoHide { get; set; }
    public bool Sound { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool Clock24 { get; set; }
    public int DefaultSnooze { get; set; } = 10;
    public bool Welcomed { get; set; }

    /// <summary>Settings schema version, used to migrate older installs (0 = saved before versioning existed). 2 = free placement is the default.</summary>
    public int Version { get; set; }
    public const int CurrentVersion = 2;
    public const int MinBubbleSize = 40, MaxBubbleSize = 160;

    // Last bubble position (top-left of the circle, physical pixels). Written by the bubble itself.
    public double? BubbleX { get; set; }
    public double? BubbleY { get; set; }

    public static readonly string[] Themes = { "dark", "light", "system" };
    public static readonly string[] Accents = { "violet", "blue", "teal", "green", "amber", "rose" };

    public void Normalize()
    {
        if (Array.IndexOf(Themes, Theme) < 0) Theme = "dark";
        if (Array.IndexOf(Accents, Accent) < 0) Accent = "violet";
        BubbleSize = Math.Clamp(BubbleSize, MinBubbleSize, MaxBubbleSize);
        BubbleOpacity = Math.Clamp(BubbleOpacity, 0.35, 1.0);
        DefaultSnooze = Math.Clamp(DefaultSnooze, 1, 1440);
        if (!SnapToEdge) AutoHide = false;
    }
}
