using Microsoft.Win32;

namespace ReminderBubble.Services;

/// <summary>"Start with Windows" via the per-user Run registry key (no admin rights needed).</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ReminderBubble";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\" --autostart");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Write("StartupService: " + ex.Message); }
    }
}
