using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ReminderBubble.Interop;
using ReminderBubble.Services;

namespace ReminderBubble.Windows;

/// <summary>
/// A borderless, always-on-top window hosting one page of the HTML UI in WebView2. It is created hidden once and
/// then shown / hidden with a native fade + slide, so opening the panel is instant.
/// </summary>
public sealed class WebHostWindow : Window
{
    public const string Host = "app.reminderbubble";

    static readonly Color Surface = Color.FromRgb(11, 11, 18);

    readonly string _page;
    readonly CoreWebView2Environment _env;
    readonly WebBridge _bridge;
    readonly bool _hideOnDeactivate;
    readonly WebView2 _web;
    IntPtr _hwnd;
    Tween? _tween;
    bool _allowClose;

    public bool IsReady { get; private set; }
    /// <summary>Logically visible (true from the moment a show starts until a hide starts).</summary>
    public bool IsShown { get; private set; }
    public bool Pinned { get; set; }
    public DateTime LastHiddenUtc { get; private set; } = DateTime.MinValue;
    public double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    public event Action<WebHostWindow>? Hidden;

    public WebHostWindow(string page, double width, double height, CoreWebView2Environment env, WebBridge bridge, bool hideOnDeactivate)
    {
        _page = page; _env = env; _bridge = bridge; _hideOnDeactivate = hideOnDeactivate;

        Title = "Reminder Bubble";
        Width = width; Height = height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Surface);
        UseLayoutRounding = true;
        // GlassFrameThickness keeps the native DWM shadow (and Windows 11 rounded corners) on a borderless window.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, Surface.R, Surface.G, Surface.B) };
        Content = _web;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW);
            int dark = 1, round = 2;                                  // DWMWA_USE_IMMERSIVE_DARK_MODE, DWMWCP_ROUND
            Native.DwmSetWindowAttribute(_hwnd, 20, ref dark, sizeof(int));
            Native.DwmSetWindowAttribute(_hwnd, 33, ref round, sizeof(int));
        };
        Deactivated += OnDeactivated;
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; HideAnimated(); } };
    }

    // ---- start-up ----------------------------------------------------------------------------
    /// <summary>Creates the browser (the window has to be shown once for that) and loads the page, then hides again.</summary>
    public async Task WarmAsync()
    {
        Left = -10000; Top = -10000;
        ShowActivated = false;
        Show();
        await _web.EnsureCoreWebView2Async(_env);
        var core = _web.CoreWebView2;

        var dev = Environment.GetEnvironmentVariable("RB_DEVTOOLS") == "1";
        var st = core.Settings;
        st.AreDefaultContextMenusEnabled = dev;
        st.AreDevToolsEnabled = dev;
        st.AreBrowserAcceleratorKeysEnabled = dev;
        st.IsStatusBarEnabled = false;
        st.IsZoomControlEnabled = false;
        st.IsPinchZoomEnabled = false;
        st.IsSwipeNavigationEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.IsPasswordAutosaveEnabled = false;

        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += (_, e) => _bridge.Dispatch(this, e.WebMessageAsJson);
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
                try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch { /* ignore */ }
        };

        var loaded = new TaskCompletionSource();
        void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) { core.NavigationCompleted -= Done; loaded.TrySetResult(); }
        core.NavigationCompleted += Done;
        core.Navigate($"https://{Host}/{_page}");
        await loaded.Task;

        Hide();
        ShowActivated = true;
        IsReady = true;
    }

    // ---- messaging ---------------------------------------------------------------------------
    public void Post(object message)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, SettingsService.Json)); }
        catch (Exception ex) { Log.Write("Post failed: " + ex.Message); }
    }

    /// <summary>Starts a native window drag (used by the page's custom header).</summary>
    public void BeginDrag()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.ReleaseCapture();
        Native.SendMessage(_hwnd, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
    }

    // ---- show / hide -------------------------------------------------------------------------
    int PxW => (int)Math.Round(Width * Dpi);
    int PxH => (int)Math.Round(Height * Dpi);

    public void ShowAt(int x, int y, int dir, bool activate)
    {
        if (!IsReady) return;
        _tween?.Stop();
        var slide = (int)Math.Round(22 * Dpi);

        Native.SetOpacity(_hwnd, 0);
        if (!IsVisible) { ShowActivated = activate; Show(); }
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x - dir * slide, y, PxW, PxH, Native.SWP_NOACTIVATE);
        IsShown = true;
        if (activate) Activate();
        Post(new { ev = "shown" });

        _tween = Tween.Run(280, Ease.OutCubic,
            t =>
            {
                Native.Move(_hwnd, (int)Math.Round(x - dir * slide * (1 - t)), y);
                Native.SetOpacity(_hwnd, t);
            },
            () => { Native.Move(_hwnd, x, y); Native.SetOpacity(_hwnd, 1); });
    }

    /// <summary>Moves / resizes the already visible window (the alert changes height as items come and go).</summary>
    public void Relocate(int x, int y)
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        _tween?.Stop();
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, PxW, PxH, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Native.SetOpacity(_hwnd, 1);
    }

    public void HideAnimated()
    {
        if (!IsShown) return;
        IsShown = false;
        LastHiddenUtc = DateTime.UtcNow;
        Post(new { ev = "hidden" });
        _tween?.Stop();
        var r = Native.GetRect(_hwnd);
        int x0 = r.Left, y = r.Top, slide = (int)Math.Round(10 * Dpi);
        _tween = Tween.Run(170, Ease.InCubic,
            t => { Native.Move(_hwnd, x0, y + (int)Math.Round(slide * t)); Native.SetOpacity(_hwnd, 1 - t); },
            () => { Hide(); Native.SetOpacity(_hwnd, 1); Hidden?.Invoke(this); });
    }

    async void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_hideOnDeactivate) return;
        // Give a moment to settle: focus may be moving to one of our own popups (a picker, the bubble, ...).
        await Task.Delay(140);
        if (IsShown && !Pinned && !IsActive && !Native.ForegroundIsOurs()) HideAnimated();
    }

    /// <summary>Really closes the window (app shutdown); normal closing only hides it.</summary>
    public void CloseForReal()
    {
        _allowClose = true;
        _tween?.Stop();
        try { _web.Dispose(); } catch { /* shutting down */ }
        Close();
    }
}
