using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ReminderBubble.Interop;
using ReminderBubble.Models;
using ReminderBubble.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ReminderBubble.Windows;

/// <summary>
/// The floating, always-on-top bubble. Native WPF (a transparent layered window) so it can be perfectly round,
/// glow, spring and pulse; the reminder UI itself lives in the HTML panel it opens.
/// </summary>
public sealed class BubbleWindow : Window
{
    /// <summary>DIPs of room around the circle for the glow, the pulse rings and the badge.</summary>
    const double Pad = 46;

    // ---- visuals ---------------------------------------------------------------------------
    readonly Grid _root = new() { Background = Brushes.Transparent };
    readonly Grid _circle = new();
    readonly Ellipse _shadow = new(), _glow = new(), _body = new(), _sheen = new(), _rim = new(), _ring1 = new(), _ring2 = new();
    readonly Viewbox _bell = new();
    readonly RotateTransform _bellRot = new();
    readonly Border _badge = new();
    readonly TextBlock _badgeText = new();
    readonly ScaleTransform _scale = new(1, 1), _ringScale1 = new(1, 1), _ringScale2 = new(1, 1), _badgeScale = new(0, 0);

    // ---- state -----------------------------------------------------------------------------
    AppSettings _s = new();
    IntPtr _hwnd;
    double _size = 64;
    int _dock;                      // -1 glued to the left edge, +1 right edge, 0 floating free
    bool _peeking, _hover, _pressed, _dragging, _alerting, _panelOpen, _shown;
    int _count;
    int _circleX, _circleY;         // resting top-left of the circle, physical px
    Drawing.Point _pressCursor, _pressWindow;
    Tween? _moveTween;
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(2600) };
    readonly DispatcherTimer _topmost = new() { Interval = TimeSpan.FromSeconds(3) };

    public event Action? Clicked;
    public event Action<string>? MenuAction;

    public BubbleWindow(AppSettings settings)
    {
        Title = "Reminder Bubble";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Content = _root;
        _s = settings;

        BuildVisuals();
        BuildMenu();
        WireInput();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW);   // keep it out of Alt-Tab
        };

        _idle.Tick += (_, _) => { _idle.Stop(); DockAway(); };
        _topmost.Tick += (_, _) => { if (_shown && _hwnd != IntPtr.Zero) Native.KeepOnTop(_hwnd); };
        ApplySettings(settings, initial: true);
    }

    /// <summary>Sets the window opacity immediately, dropping any animation that is holding the previous value.</summary>
    void SetOpacityNow(double v) { BeginAnimation(OpacityProperty, null); Opacity = v; }

    double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    int SPx => (int)Math.Round(_size * Dpi);
    int PadPx => (int)Math.Round(Pad * Dpi);

    public bool IsAlerting => _alerting;

    /// <summary>The circle's current bounds in screen pixels.</summary>
    public Drawing.Rectangle CircleBounds
    {
        get
        {
            var r = Native.GetRect(_hwnd);
            var cx = (r.Left + r.Right) / 2; var cy = (r.Top + r.Bottom) / 2; var s = SPx;
            return new Drawing.Rectangle(cx - s / 2, cy - s / 2, s, s);
        }
    }

    /// <summary>The circle's resting bounds (what it occupies when not tucked into the edge).</summary>
    public Drawing.Rectangle RestBounds => new(_circleX, _circleY, SPx, SPx);

    // ======================================================================================
    // Visual tree
    // ======================================================================================
    void BuildVisuals()
    {
        foreach (var e in new Ellipse[] { _shadow, _glow, _body, _sheen, _rim, _ring1, _ring2 }) e.IsHitTestVisible = false;

        _shadow.Fill = new RadialGradientBrush(Color.FromArgb(110, 0, 0, 0), Colors.Transparent) { Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        _shadow.RenderTransformOrigin = new Point(.5, .5);
        _shadow.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.18, 1.18), new TranslateTransform(0, 5) } };

        _glow.RenderTransformOrigin = new Point(.5, .5);
        _glow.Opacity = .85;

        _ring1.Fill = null; _ring2.Fill = null;
        _ring1.StrokeThickness = 2; _ring2.StrokeThickness = 2;
        _ring1.Opacity = 0; _ring2.Opacity = 0;
        _ring1.RenderTransformOrigin = new Point(.5, .5); _ring2.RenderTransformOrigin = new Point(.5, .5);
        _ring1.RenderTransform = _ringScale1; _ring2.RenderTransform = _ringScale2;

        _sheen.Fill = new RadialGradientBrush(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
        { Center = new Point(.32, .22), GradientOrigin = new Point(.32, .22), RadiusX = .62, RadiusY = .5 };
        _rim.Fill = null;
        _rim.StrokeThickness = 1.25;
        _rim.Stroke = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90);

        var bellCanvas = new Canvas { Width = 24, Height = 24 };
        foreach (var d in new[] { "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9", "M10.3 21a1.94 1.94 0 0 0 3.4 0" })
        {
            bellCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(d),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            });
        }
        bellCanvas.RenderTransformOrigin = new Point(.5, .1);
        bellCanvas.RenderTransform = _bellRot;
        _bell.Child = bellCanvas;
        _bell.IsHitTestVisible = false;
        _bell.HorizontalAlignment = HorizontalAlignment.Center;
        _bell.VerticalAlignment = VerticalAlignment.Center;

        _badgeText.Foreground = Brushes.White;
        _badgeText.FontSize = 11;
        _badgeText.FontWeight = FontWeights.Bold;
        _badgeText.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        _badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        _badgeText.VerticalAlignment = VerticalAlignment.Center;
        _badge.Child = _badgeText;
        _badge.MinWidth = 22; _badge.Height = 22;
        _badge.CornerRadius = new CornerRadius(11);
        _badge.Padding = new Thickness(5, 0, 5, 0);
        _badge.BorderThickness = new Thickness(2);
        _badge.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 16, 26));
        _badge.Background = new LinearGradientBrush(Color.FromRgb(255, 93, 115), Color.FromRgb(244, 63, 130), 45);
        _badge.HorizontalAlignment = HorizontalAlignment.Right;
        _badge.VerticalAlignment = VerticalAlignment.Top;
        _badge.Margin = new Thickness(0, -7, -7, 0);
        _badge.RenderTransformOrigin = new Point(.5, .5);
        _badge.RenderTransform = _badgeScale;
        _badge.Visibility = Visibility.Collapsed;
        _badge.IsHitTestVisible = false;

        _circle.RenderTransformOrigin = new Point(.5, .5);
        _circle.RenderTransform = _scale;
        _circle.Children.Add(_body);
        _circle.Children.Add(_sheen);
        _circle.Children.Add(_rim);
        _circle.Children.Add(_bell);
        _circle.Children.Add(_badge);

        _root.Children.Add(_shadow);
        _root.Children.Add(_glow);
        _root.Children.Add(_ring1);
        _root.Children.Add(_ring2);
        _root.Children.Add(_circle);
    }

    static (Color a, Color b) Palette(string accent) => accent switch
    {
        "blue" => (Color.FromRgb(59, 130, 246), Color.FromRgb(34, 211, 238)),
        "teal" => (Color.FromRgb(20, 184, 166), Color.FromRgb(52, 211, 153)),
        "green" => (Color.FromRgb(34, 197, 94), Color.FromRgb(163, 230, 53)),
        "amber" => (Color.FromRgb(245, 158, 11), Color.FromRgb(249, 115, 22)),
        "rose" => (Color.FromRgb(244, 63, 94), Color.FromRgb(236, 72, 153)),
        _ => (Color.FromRgb(124, 92, 255), Color.FromRgb(194, 92, 255)),
    };

    public void ApplySettings(AppSettings s, bool initial = false)
    {
        _s = s;
        var oldSize = _size;
        _size = s.BubbleSize;
        var (a, b) = Palette(s.Accent);

        _body.Fill = new LinearGradientBrush(a, b, 45);
        _glow.Fill = new RadialGradientBrush(Color.FromArgb(150, a.R, a.G, a.B), Color.FromArgb(0, a.R, a.G, a.B))
        { Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        _ring1.Stroke = new SolidColorBrush(a); _ring2.Stroke = new SolidColorBrush(b);

        var w = _size + 2 * Pad;
        _root.Width = _root.Height = w;
        Width = Height = w;
        _circle.Width = _circle.Height = _size;
        foreach (var e in new Ellipse[] { _body, _sheen, _rim, _ring1, _ring2, _shadow }) e.Width = e.Height = _size;
        _glow.Width = _glow.Height = _size + Pad * 1.7;
        _bell.Width = _bell.Height = _size * 0.46;
        var ringTarget = (_size + Pad * 1.7) / _size;
        _ringTarget = ringTarget;

        if (!initial && _hwnd != IntPtr.Zero)
        {
            // Keep the circle's centre in place while the size changes.
            var d = (_size - oldSize) * Dpi / 2;
            _circleX -= (int)Math.Round(d); _circleY -= (int)Math.Round(d);
            if (!_dragging) SnapAndPlace(animated: false);
        }
        if (_s.SnapToEdge) { if (_dock == 0) _dock = 1; } else { _dock = 0; Reveal(); }
        if (!initial) SetOpacityNow(_peeking ? _s.BubbleOpacity * 0.6 : _s.BubbleOpacity);
        else Opacity = _s.BubbleOpacity;
        RestartIdle();
    }

    double _ringTarget = 1.9;

    // ======================================================================================
    // Context menu (styled at runtime so no XAML build step is required)
    // ======================================================================================
    const string MenuXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="RbMenu" TargetType="ContextMenu">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                  <Border Margin="14" Padding="6" CornerRadius="14" Background="#F5151521"
                          BorderBrush="#2EFFFFFF" BorderThickness="1">
                    <Border.Effect><DropShadowEffect BlurRadius="26" ShadowDepth="6" Direction="270" Opacity="0.5" Color="#000000"/></Border.Effect>
                    <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="RbItem" TargetType="MenuItem">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="Foreground" Value="#F1F1F8"/>
            <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                  <Border x:Name="Bd" CornerRadius="9" Padding="14,8,22,8" MinWidth="188" Background="Transparent">
                    <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Bd" Property="Background" Value="#2AFFFFFF"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="RbSep" TargetType="Separator">
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Separator"><Border Height="1" Margin="10,5" Background="#1FFFFFFF"/></ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;

    void BuildMenu()
    {
        ContextMenu menu;
        try
        {
            var dict = (ResourceDictionary)XamlReader.Parse(MenuXaml);
            Resources.MergedDictionaries.Add(dict);
            menu = new ContextMenu { Style = (Style)dict["RbMenu"] };
            MenuItem Item(string header, string action)
            {
                var mi = new MenuItem { Header = header, Style = (Style)dict["RbItem"] };
                mi.Click += (_, _) => MenuAction?.Invoke(action);
                return mi;
            }
            menu.Items.Add(Item("Open reminders", "open"));
            menu.Items.Add(Item("New reminder", "new"));
            menu.Items.Add(new Separator { Style = (Style)dict["RbSep"] });
            menu.Items.Add(Item("Settings", "settings"));
            menu.Items.Add(Item("Hide bubble", "hide"));
            menu.Items.Add(new Separator { Style = (Style)dict["RbSep"] });
            menu.Items.Add(Item("Quit Reminder Bubble", "quit"));
        }
        catch (Exception ex)
        {
            // Styling is cosmetic - fall back to the stock menu rather than failing to start.
            Log.Write("Menu style failed: " + ex.Message);
            menu = new ContextMenu();
            foreach (var (h, a) in new[] { ("Open reminders", "open"), ("New reminder", "new"), ("Settings", "settings"), ("Hide bubble", "hide"), ("Quit Reminder Bubble", "quit") })
            {
                var mi = new MenuItem { Header = h };
                var action = a;
                mi.Click += (_, _) => MenuAction?.Invoke(action);
                menu.Items.Add(mi);
            }
        }
        menu.Opened += (_, _) => { Activate(); _idle.Stop(); };
        menu.Closed += (_, _) => RestartIdle();
        _root.ContextMenu = menu;
    }

    // ======================================================================================
    // Input: hover, press, click vs. drag
    // ======================================================================================
    void WireInput()
    {
        _root.MouseEnter += (_, _) =>
        {
            _hover = true; _idle.Stop(); Reveal();
            if (!_pressed) ScaleTo(1.08, 220);
            GlowTo(1.0, 220);
        };
        _root.MouseLeave += (_, _) =>
        {
            _hover = false;
            if (!_pressed) ScaleTo(1.0, 280);
            GlowTo(.85, 280);
            RestartIdle();
        };
        _root.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = true; _dragging = false;
            Native.GetCursorPos(out var c);
            _pressCursor = new Drawing.Point(c.X, c.Y);
            var r = Native.GetRect(_hwnd);
            _pressWindow = new Drawing.Point(r.Left, r.Top);
            _root.CaptureMouse();
            ScaleTo(0.93, 90);
            e.Handled = true;
        };
        _root.MouseMove += (_, _) =>
        {
            if (!_pressed) return;
            Native.GetCursorPos(out var c);
            int dx = c.X - _pressCursor.X, dy = c.Y - _pressCursor.Y;
            if (!_dragging && Math.Abs(dx) + Math.Abs(dy) > 5)
            {
                _dragging = true;
                _moveTween?.Stop();
                _peeking = false;
                SetOpacityNow(_s.BubbleOpacity);
            }
            if (_dragging) Native.Move(_hwnd, _pressWindow.X + dx, _pressWindow.Y + dy);
        };
        _root.MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed) return;
            _pressed = false;
            _root.ReleaseMouseCapture();
            if (_dragging)
            {
                _dragging = false;
                ScaleTo(_hover ? 1.08 : 1.0, 260);
                var r = Native.GetRect(_hwnd);
                _circleX = r.Left + PadPx; _circleY = r.Top + PadPx;
                SnapAndPlace(animated: true);
            }
            else
            {
                ScaleTo(1.08, 140);
                Clicked?.Invoke();
            }
            e.Handled = true;
        };
        _root.MouseRightButtonDown += (_, _) => Activate();
    }

    // ======================================================================================
    // Small animations (WPF animation system, for the bubble's own visuals)
    // ======================================================================================
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    void ScaleTo(double v, double ms)
    {
        var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void GlowTo(double v, double ms) =>
        _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut });

    // ======================================================================================
    // Placement: edge snapping, docking, restoring the saved position
    // ======================================================================================
    Drawing.Rectangle WorkArea()
    {
        var r = Native.GetRect(_hwnd);
        return Forms.Screen.FromRectangle(new Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)).WorkingArea;
    }

    int Gap => (int)Math.Round(10 * Dpi);

    (int x, int y) RestWindowPos(int side, Drawing.Rectangle w)
    {
        var y = Math.Clamp(_circleY, w.Top + Gap, Math.Max(w.Top + Gap, w.Bottom - Gap - SPx));
        var x = side switch
        {
            < 0 => w.Left + Gap,
            > 0 => w.Right - Gap - SPx,
            _ => Math.Clamp(_circleX, w.Left + Gap, Math.Max(w.Left + Gap, w.Right - Gap - SPx)),
        };
        return (x - PadPx, y - PadPx);
    }

    (int x, int y) PeekWindowPos(int side, Drawing.Rectangle w)
    {
        var rest = RestWindowPos(side, w);
        var visible = (int)(SPx * 0.40);
        var x = side < 0 ? w.Left - (SPx - visible) : w.Right - visible;
        return (x - PadPx, rest.y);
    }

    /// <summary>Computes the resting spot from where the circle was dropped (glued to the nearest side edge) and moves there.</summary>
    void SnapAndPlace(bool animated)
    {
        var w = WorkArea();
        if (_s.SnapToEdge) _dock = (_circleX + SPx / 2) < (w.Left + w.Width / 2) ? -1 : 1; else _dock = 0;
        var (x, y) = RestWindowPos(_dock, w);
        _circleX = x + PadPx; _circleY = y + PadPx;
        _peeking = false;
        MoveWindowTo(x, y, animated ? 460 : 0, animated ? Ease.OutBack : Ease.Linear, SavePosition);
    }

    void MoveWindowTo(int x, int y, double ms, Func<double, double> ease, Action? done = null)
    {
        _moveTween?.Stop();
        if (_hwnd == IntPtr.Zero) return;
        if (ms <= 0) { Native.Move(_hwnd, x, y); done?.Invoke(); return; }
        var r = Native.GetRect(_hwnd);
        int x0 = r.Left, y0 = r.Top;
        _moveTween = Tween.Run(ms, ease,
            t => Native.Move(_hwnd, (int)Math.Round(Tween.Lerp(x0, x, t)), (int)Math.Round(Tween.Lerp(y0, y, t))),
            () => { Native.Move(_hwnd, x, y); done?.Invoke(); });
    }

    void SavePosition() => PositionChanged?.Invoke(_circleX, _circleY);
    public event Action<double, double>? PositionChanged;

    /// <summary>Called once at start-up: restores the saved position (or picks the right edge) and plays the intro.</summary>
    public void ShowAtStartup(double? savedX, double? savedY)
    {
        Left = -10000; Top = -10000;     // placed for real below, once the HWND exists
        Show();
        _shown = true;
        var w = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Drawing.Rectangle(0, 0, 1280, 720);
        if (savedX is { } sx && savedY is { } sy && IsOnAnyScreen((int)sx, (int)sy))
        { _circleX = (int)sx; _circleY = (int)sy; }
        else
        { _circleX = w.Right - Gap - SPx; _circleY = w.Top + (int)(w.Height * 0.38); }
        // Put the window on the right monitor first so the work area used for snapping is the right one.
        Native.Move(_hwnd, _circleX - PadPx, _circleY - PadPx);
        SnapAndPlace(animated: false);

        _scale.ScaleX = _scale.ScaleY = 0;
        Opacity = 0;
        var fade = new DoubleAnimation(_s.BubbleOpacity, TimeSpan.FromMilliseconds(420)) { EasingFunction = EaseOut };
        BeginAnimation(OpacityProperty, fade);
        var spring = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(760)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .9 }, BeginTime = TimeSpan.FromMilliseconds(120) };
        spring.Completed += (_, _) => { BeginAnimation(OpacityProperty, null); Opacity = _s.BubbleOpacity; RestartIdle(); };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
        _topmost.Start();
    }

    static bool IsOnAnyScreen(int x, int y) =>
        Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(x + 8, y + 8));

    /// <summary>Re-clamps the bubble after monitor / resolution / taskbar changes.</summary>
    public void EnsureOnScreen()
    {
        if (!_shown || _hwnd == IntPtr.Zero || _dragging) return;
        SnapAndPlace(animated: false);
        RestartIdle();
    }

    // ======================================================================================
    // Docking ("peek") behaviour
    // ======================================================================================
    void RestartIdle()
    {
        _idle.Stop();
        if (_s.SnapToEdge && _s.AutoHide && !_hover && !_alerting && !_panelOpen && !_dragging && _dock != 0 && _shown) _idle.Start();
    }

    void DockAway()
    {
        if (!_s.SnapToEdge || !_s.AutoHide || _hover || _alerting || _panelOpen || _dragging || _dock == 0 || _peeking) return;
        var w = WorkArea();
        var (x, y) = PeekWindowPos(_dock, w);
        _peeking = true;
        MoveWindowTo(x, y, 420, Ease.InOutCubic);
        BeginAnimation(OpacityProperty, new DoubleAnimation(_s.BubbleOpacity * 0.6, TimeSpan.FromMilliseconds(360)));
    }

    /// <summary>Slides the bubble back out of the screen edge.</summary>
    public void Reveal()
    {
        if (!_peeking || _hwnd == IntPtr.Zero) return;
        _peeking = false;
        var (x, y) = RestWindowPos(_dock, WorkArea());
        MoveWindowTo(x, y, 380, Ease.OutBack);
        BeginAnimation(OpacityProperty, new DoubleAnimation(_s.BubbleOpacity, TimeSpan.FromMilliseconds(220)));
    }

    public void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        if (open) { _idle.Stop(); Reveal(); } else RestartIdle();
    }

    // ======================================================================================
    // Badge + alert animation
    // ======================================================================================
    public void SetCount(int count)
    {
        var was = _count;
        _count = count;
        if (count > 0)
        {
            _badgeText.Text = count > 9 ? "9+" : count.ToString();
            if (_badge.Visibility != Visibility.Visible)
            {
                _badge.Visibility = Visibility.Visible;
                var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.2 } };
                _badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                _badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            else if (count != was)
            {
                var bump = new DoubleAnimationUsingKeyFrames();
                bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), EaseOut));
                bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), new BackEase { EasingMode = EasingMode.EaseOut }));
                _badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
                _badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
            }
            StartAlert();
        }
        else
        {
            StopAlert();
            if (_badge.Visibility == Visibility.Visible)
            {
                var shrink = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = .8 } };
                shrink.Completed += (_, _) => { if (_count == 0) _badge.Visibility = Visibility.Collapsed; };
                _badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                _badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            }
        }
        RestartIdle();
    }

    void StartAlert()
    {
        if (_alerting) return;
        _alerting = true;
        Reveal();
        PulseRing(_ringScale1, _ring1, 0);
        PulseRing(_ringScale2, _ring2, 900);

        var swing = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        (double ms, double deg)[] frames = { (0, 0), (110, -17), (250, 15), (390, -12), (520, 8), (640, -4), (760, 0), (3200, 0) };
        foreach (var (ms, deg) in frames)
            swing.KeyFrames.Add(new EasingDoubleKeyFrame(deg, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), sine));
        _bellRot.BeginAnimation(RotateTransform.AngleProperty, swing);
    }

    void PulseRing(ScaleTransform st, Ellipse ring, int delayMs)
    {
        var dur = TimeSpan.FromMilliseconds(1800);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var grow = new DoubleAnimation(1.0, _ringTarget, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = EaseOut };
        var fade = new DoubleAnimation(.7, 0, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    void StopAlert()
    {
        if (!_alerting) return;
        _alerting = false;
        foreach (var st in new[] { _ringScale1, _ringScale2 })
        { st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleX = st.ScaleY = 1; }
        _ring1.BeginAnimation(OpacityProperty, null); _ring2.BeginAnimation(OpacityProperty, null);
        _ring1.Opacity = _ring2.Opacity = 0;
        _bellRot.BeginAnimation(RotateTransform.AngleProperty, null);
        _bellRot.Angle = 0;
    }

    /// <summary>A one-off wiggle, e.g. when a reminder was just added.</summary>
    public void Nudge()
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130)), EaseOut));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(_hover ? 1.08 : 1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480)), new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }));
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    public void ShowBubble()
    {
        if (!IsVisible) { Show(); _shown = true; }
        Native.KeepOnTop(_hwnd);
        Reveal();
        Nudge();
        RestartIdle();
    }

    public void HideBubble()
    {
        _shown = false;
        _idle.Stop();
        Hide();
    }

    public bool IsBubbleVisible => IsVisible;

    public void Shutdown()
    {
        _topmost.Stop(); _idle.Stop(); _moveTween?.Stop();
    }
}
