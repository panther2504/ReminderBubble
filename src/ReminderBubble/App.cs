using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using ReminderBubble.Data;
using ReminderBubble.Models;
using ReminderBubble.Services;
using ReminderBubble.Windows;
using Forms = System.Windows.Forms;

namespace ReminderBubble;

public sealed class App : Application
{
    readonly string[] _args;
    readonly DispatcherTimer _scheduler = new() { Interval = TimeSpan.FromSeconds(1) };
    EventWaitHandle? _showEvent;
    TrayIcon? _tray;
    bool _quitting;

    public Database Db { get; private set; } = null!;
    public ReminderService Reminders { get; private set; } = null!;
    public SettingsService Settings { get; private set; } = null!;
    public BubbleWindow Bubble { get; private set; } = null!;
    public WebHostWindow Panel { get; private set; } = null!;
    public WebHostWindow Alert { get; private set; } = null!;

    public App(string[] args)
    {
        _args = args;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, e) => { Log.Write("Unhandled: " + e.Exception); e.Handled = true; };
    }

    // ======================================================================================
    // Start-up
    // ======================================================================================
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataDir = Log.Dir;
            Db = new Database(Path.Combine(dataDir, "reminders.db"));
            Settings = new SettingsService(Db);
            Reminders = new ReminderService(Db);
            // The Run key is the source of truth for "start with Windows": the installer may have set it (or the user
            // removed it), so mirror it into the setting instead of overwriting it.
            var autostart = StartupService.IsEnabled();
            if (autostart != Settings.Current.StartWithWindows)
                Settings.Patch(JsonDocument.Parse(autostart ? "{\"startWithWindows\":true}" : "{\"startWithWindows\":false}").RootElement);
            else if (autostart)
                StartupService.Apply(true);      // keep the path current after an update or a move

            // The bubble appears immediately; the HTML windows warm up behind it.
            Bubble = new BubbleWindow(Settings.Current);
            Bubble.Clicked += TogglePanel;
            Bubble.MenuAction += OnBubbleMenu;
            Bubble.PositionChanged += (x, y) => Settings.SetBubblePosition(x, y);
            Bubble.ShowAtStartup(Settings.Current.BubbleX, Settings.Current.BubbleY);
            Bubble.SetCount(Reminders.ActiveCount());

            _tray = new TrayIcon(() => OpenPanel(), () => OpenPanel("compose"), ToggleBubble, () => OpenPanel("settings"), Quit);
            ListenForSecondInstance();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            var env = await CreateWebViewEnvironment();
            if (env is null) { Quit(); return; }

            var bridge = new WebBridge(this);
            Panel = new WebHostWindow("index.html", 408, 660, env, bridge, hideOnDeactivate: true);
            Alert = new WebHostWindow("alert.html", 388, 200, env, bridge, hideOnDeactivate: false);
            Panel.Hidden += _ => Bubble.SetPanelOpen(false);
            await Panel.WarmAsync();
            await Alert.WarmAsync();

            Reminders.Changed += OnRemindersChanged;
            Reminders.Due += OnDue;
            Settings.Changed += OnSettingsChanged;

            var firstRun = Settings.IsFirstRun && !Settings.Current.Welcomed;
            if (firstRun) SeedWelcome();

            _scheduler.Tick += (_, _) => Reminders.Tick();
            _scheduler.Start();
            Reminders.Tick();   // anything that came due while the app was closed

            if (firstRun) { Settings.MarkWelcomed(); OpenPanel(); }
        }
        catch (Exception ex)
        {
            Log.Write("Start-up failed: " + ex);
            MessageBox.Show($"Reminder Bubble could not start.\n\n{ex.Message}\n\nDetails: {Log.Dir}", "Reminder Bubble",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Quit();
        }
    }

    static async Task<CoreWebView2Environment?> CreateWebViewEnvironment()
    {
        try
        {
            return await CoreWebView2Environment.CreateAsync(null, Path.Combine(Log.Dir, "WebView2"), new CoreWebView2EnvironmentOptions());
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || ex is System.Runtime.InteropServices.COMException)
        {
            Log.Write("WebView2 runtime missing: " + ex.Message);
            var answer = MessageBox.Show(
                "Reminder Bubble needs the Microsoft Edge WebView2 Runtime, which was not found on this PC.\n\nOpen the download page now?",
                "Reminder Bubble", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true }); } catch { /* ignore */ }
            return null;
        }
    }

    void SeedWelcome()
    {
        Reminders.Add(new ReminderInput
        {
            Title = "Welcome to Reminder Bubble 👋",
            Notes = "Tick it off when you are done, or snooze it. Drag the bubble anywhere - it tucks itself into the screen edge.",
            DueAt = DateTime.UtcNow.AddMinutes(1),
            Color = "violet",
        });
    }

    // ======================================================================================
    // Panel / alert placement
    // ======================================================================================
    void TogglePanel()
    {
        if (Panel is null || !Panel.IsReady) return;
        if (Panel.IsShown) Panel.HideAnimated();
        // Clicking the bubble first deactivates (and hides) the panel; do not instantly re-open it.
        else if ((DateTime.UtcNow - Panel.LastHiddenUtc).TotalMilliseconds > 300) OpenPanel();
    }

    /// <summary>Opens the main panel next to the bubble. <paramref name="mode"/> is forwarded to the page as an "open" event: compose | settings.</summary>
    public void OpenPanel(string? mode = null)
    {
        if (Panel is null || !Panel.IsReady) return;
        if (!Bubble.IsBubbleVisible) { Bubble.ShowBubble(); _tray?.SetBubbleVisible(true); }
        Alert.HideAnimated();
        Bubble.SetPanelOpen(true);

        var work = WorkAreaNearBubble();
        Panel.Height = Math.Min(660, Math.Max(420, work.Height / Panel.Dpi - 32));
        var (x, y, dir) = PlaceBeside(Panel);
        if (Panel.IsShown) { Panel.Relocate(x, y); Panel.Activate(); }
        else Panel.ShowAt(x, y, dir, activate: true);
        if (mode is not null) Panel.Post(new { ev = "open", data = mode });
    }

    void ShowAlert()
    {
        if (Alert is null || !Alert.IsReady || Alert.IsShown || (Panel?.IsShown ?? false)) return;
        var (x, y, dir) = PlaceBeside(Alert);
        Alert.ShowAt(x, y, dir, activate: false);
    }

    public void ResizeAlert(double height)
    {
        Alert.Height = Math.Clamp(height, 96, 600);
        if (Alert.IsShown) { var (x, y, _) = PlaceBeside(Alert); Alert.Relocate(x, y); }
    }

    System.Drawing.Rectangle WorkAreaNearBubble() => Forms.Screen.FromRectangle(Bubble.RestBounds).WorkingArea;

    /// <summary>Puts a window beside the bubble on whichever side has room, vertically centred on it, kept on-screen.</summary>
    (int x, int y, int dir) PlaceBeside(WebHostWindow w)
    {
        var circle = Bubble.RestBounds;
        var work = Forms.Screen.FromRectangle(circle).WorkingArea;
        var dpi = w.Dpi;
        int pw = (int)Math.Round(w.Width * dpi), ph = (int)Math.Round(w.Height * dpi);
        int gap = (int)Math.Round(14 * dpi), edge = (int)Math.Round(8 * dpi);

        var onRight = circle.Left + circle.Width / 2 < work.Left + work.Width / 2;
        var x = onRight ? circle.Right + gap : circle.Left - gap - pw;
        x = Math.Clamp(x, work.Left + edge, Math.Max(work.Left + edge, work.Right - pw - edge));
        var y = circle.Top + circle.Height / 2 - ph / 2;
        y = Math.Clamp(y, work.Top + edge, Math.Max(work.Top + edge, work.Bottom - ph - edge));
        return (x, y, onRight ? 1 : -1);
    }

    // ======================================================================================
    // Events
    // ======================================================================================
    void OnRemindersChanged()
    {
        var active = Reminders.ActiveCount();
        Bubble.SetCount(active);
        Panel.Post(new { ev = "changed" });
        Alert.Post(new { ev = "changed" });
        if (active == 0) Alert.HideAnimated();
    }

    void OnDue(IReadOnlyList<Reminder> due)
    {
        if (Settings.Current.Sound) Chime.Play();
        ShowAlert();
    }

    void OnSettingsChanged(AppSettings s)
    {
        Bubble.ApplySettings(s);
        StartupService.Apply(s.StartWithWindows);
        Panel.Post(new { ev = "settings", data = s });
        Alert.Post(new { ev = "settings", data = s });
    }

    void OnBubbleMenu(string action)
    {
        switch (action)
        {
            case "open": OpenPanel(); break;
            case "new": OpenPanel("compose"); break;
            case "settings": OpenPanel("settings"); break;
            case "hide": ToggleBubble(); break;
            case "quit": Quit(); break;
        }
    }

    void ToggleBubble()
    {
        if (Bubble.IsBubbleVisible)
        {
            Panel?.HideAnimated(); Alert?.HideAnimated();
            Bubble.HideBubble();
            _tray?.SetBubbleVisible(false);
        }
        else
        {
            Bubble.ShowBubble();
            _tray?.SetBubbleVisible(true);
        }
    }

    void OnDisplayChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => Bubble.EnsureOnScreen(), DispatcherPriority.Background);

    void ListenForSecondInstance()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEvent);
        new Thread(() =>
        {
            while (!_quitting && _showEvent.WaitOne())
            {
                if (_quitting) break;
                Dispatcher.BeginInvoke(() => { if (!Bubble.IsBubbleVisible) ToggleBubble(); OpenPanel(); });
            }
        }) { IsBackground = true, Name = "ShowEventListener" }.Start();
    }

    // ======================================================================================
    // Shutdown
    // ======================================================================================
    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        try
        {
            _scheduler.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _showEvent?.Set();
            _tray?.Dispose();
            Bubble?.Shutdown();
            Panel?.CloseForReal();
            Alert?.CloseForReal();
            Bubble?.Close();
            Database.ShutdownPools();
        }
        catch (Exception ex) { Log.Write("Quit: " + ex.Message); }
        Shutdown();
    }
}
