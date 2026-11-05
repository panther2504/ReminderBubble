namespace ReminderBubble;

public static class Program
{
    public const string InstanceMutex = @"Local\ReminderBubble.Instance";
    public const string ShowEvent = @"Local\ReminderBubble.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutex, out var isFirst);
        if (!isFirst)
        {
            // Already running: ask the running instance to pop its panel open, then leave.
            try { using var evt = EventWaitHandle.OpenExisting(ShowEvent); evt.Set(); } catch { /* the other instance is still starting */ }
            return 0;
        }
        var app = new App(args);
        return app.Run();
    }
}
