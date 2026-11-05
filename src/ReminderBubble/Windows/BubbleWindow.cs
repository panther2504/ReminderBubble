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
/// The floating, always-on-top bubble: a vector 3D glass sphere in a transparent layered window.
/// It can be dragged anywhere on any monitor, resized (mouse wheel, the grip that appears on hover, the menu or the
/// settings), and optionally glued to a screen edge. The reminder UI itself lives in the HTML panel it opens.
/// </summary>
public sealed class BubbleWindow : Window
{
    /// <summary>DIPs of room around the circle for the pulse rings, the contact shadow and the badge.</summary>
    const double Pad = 46;
    public const int MinSize = 40, MaxSize = 160;

    // ---- visuals ---------------------------------------------------------------------------
    readonly Grid _root = new() { Background = Brushes.Transparent };
    readonly Grid _circle = new();
    readonly Ellipse _shadow = new(), _ring1 = new(), _ring2 = new();
    readonly Viewbox _sphereBox = new();
    readonly Ellipse _body = new(), _edge = new(), _refl = new(), _rim = new(), _spec = new();
    readonly System.Windows.Shapes.Path _gloss = new();
    RadialGradientBrush _bodyBrush = new();
    readonly TranslateTransform _glossShift = new(), _shadowShift = new();
    readonly RotateTransform _bellRot = new() { CenterX = 100, CenterY = 48 };
    readonly Border _badge = new();
    readonly TextBlock _badgeText = new();
    readonly Border _grip = new();
    readonly ScaleTransform _scale = new(1, 1), _ringScale1 = new(1, 1), _ringScale2 = new(1, 1), _badgeScale = new(0, 0), _gripScale = new(.6, .6);

    // ---- state -----------------------------------------------------------------------------
    AppSettings _s = new();
    IntPtr _hwnd;
    double _size = 64;
    double _ringTarget = 1.9;
    int _dock;                      // -1 glued to the left edge, +1 right edge, 0 floating free
    bool _peeking, _hover, _pressed, _dragging, _resizing, _alerting, _panelOpen, _shown;
    int _count;
    int _circleX, _circleY;         // resting top-left of the circle, physical px
    double _tiltX, _tiltY;
    Drawing.Point _pressCursor, _pressWindow;
    Tween? _moveTween, _tiltTween;
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(2600) };
    readonly DispatcherTimer _topmost = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer _commit = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public event Action? Clicked;
    public event Action<string>? MenuAction;
    /// <summary>The circle's resting position changed (top-left, physical px). Persist it.</summary>
    public event Action<double, double>? PositionChanged;
    /// <summary>The user finished resizing the bubble (DIPs). Persist it.</summary>
    public event Action<int>? SizeCommitted;

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
        SnapsToDevicePixels = false;       // the sphere is smooth vector art - never pixel-snap it
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
        _commit.Tick += (_, _) => { _commit.Stop(); CommitSize(); };
        ApplySettings(settings, initial: true);
    }

    double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    int SPx => (int)Math.Round(_size * Dpi);
    int PadPx => (int)Math.Round(Pad * Dpi);
    int WinPx => (int)Math.Round((_size + 2 * Pad) * Dpi);

    /// <summary>Sets the window opacity immediately, dropping any animation that is holding the previous value.</summary>
    void SetOpacityNow(double v) { BeginAnimation(OpacityProperty, null); Opacity = v; }

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
    // Visual tree: a 3D glass sphere drawn in a 200 x 200 design space (scales to any size)
    // ======================================================================================
    static Color Lighten(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    static Color Darken(Color c, double t) => Color.FromRgb((byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));
    static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);

    static T At<T>(T el, double x, double y, double w, double h) where T : FrameworkElement
    {
        el.Width = w; el.Height = h;
        Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
        el.IsHitTestVisible = false;
        return el;
    }

    void BuildVisuals()
    {
        // contact shadow and pulse rings sit behind the sphere
        _shadow.Fill = new RadialGradientBrush(Color.FromArgb(120, 0, 0, 0), Colors.Transparent) { Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        _shadow.RenderTransform = _shadowShift;
        _ring1.Fill = null; _ring2.Fill = null;
        _ring1.StrokeThickness = 2; _ring2.StrokeThickness = 2;
        _ring1.Opacity = 0; _ring2.Opacity = 0;
        _ring1.RenderTransformOrigin = new Point(.5, .5); _ring2.RenderTransformOrigin = new Point(.5, .5);
        _ring1.RenderTransform = _ringScale1; _ring2.RenderTransform = _ringScale2;
        foreach (var e in new[] { _shadow, _ring1, _ring2 }) e.IsHitTestVisible = false;

        // ---- the sphere (design space 200 x 200, sphere radius 80) ----
        var design = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false };
        design.Children.Add(At(_body, 20, 20, 160, 160));
        design.Children.Add(At(_edge, 20, 20, 160, 160));
        design.Children.Add(At(_refl, 46, 116, 108, 60));                       // light bouncing up from the bottom
        _rim.Fill = null; _rim.StrokeThickness = 1.8;
        design.Children.Add(At(_rim, 20.8, 20.8, 158.4, 158.4));                // fresnel rim

        var glossLayer = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false, RenderTransform = _glossShift };
        _gloss.Data = Geometry.Parse("M 100 20.5 C 66 20.5 40 40 33 66 C 54 52 78 49 100 49 C 122 49 146 52 167 66 C 160 40 134 20.5 100 20.5 Z");
        _gloss.Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(230, 255, 255, 255), 0), new(Color.FromArgb(56, 255, 255, 255), .55), new(Color.FromArgb(5, 255, 255, 255), 1),
        }, new Point(0, 0), new Point(0, 1));
        _gloss.IsHitTestVisible = false;
        glossLayer.Children.Add(_gloss);
        _spec.Fill = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));
        glossLayer.Children.Add(At(_spec, 55, 43.8, 14, 8.4));
        _spec.RenderTransformOrigin = new Point(.5, .5);
        _spec.RenderTransform = new RotateTransform(-38);
        design.Children.Add(glossLayer);

        // the bell: soft stacked shadow, then the white enamel
        var bellHolder = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false, RenderTransform = _bellRot };
        var bell = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false, RenderTransform = new ScaleTransform(3.4, 3.4) };
        Canvas.SetLeft(bell, 59.2); Canvas.SetTop(bell, 62.92);
        const string dome = "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9z";
        const string clapper = "M10 18.5a2 2 0 0 0 4 0z";
        foreach (var (dy, a) in new[] { (1.5, 22), (1.0, 30), (0.55, 42) })
            foreach (var d in new[] { dome, clapper })
                bell.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(d), Fill = new SolidColorBrush(Color.FromArgb((byte)a, 26, 11, 77)), RenderTransform = new TranslateTransform(0, dy), IsHitTestVisible = false });
        var enamel = new LinearGradientBrush(Colors.White, Color.FromRgb(233, 226, 255), 90);
        foreach (var d in new[] { dome, clapper })
            bell.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(d), Fill = enamel, IsHitTestVisible = false });
        bellHolder.Children.Add(bell);
        design.Children.Add(bellHolder);

        _sphereBox.Child = design;
        _sphereBox.Stretch = Stretch.Uniform;
        _sphereBox.IsHitTestVisible = false;

        // ---- count badge ----
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
        _badge.RenderTransformOrigin = new Point(.5, .5);
        _badge.RenderTransform = _badgeScale;
        _badge.Visibility = Visibility.Collapsed;
        _badge.IsHitTestVisible = false;

        // ---- resize grip (appears on hover, bottom-right of the sphere) ----
        var gripIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M2 8 L8 2 M5 8 L8 5"),
            Stroke = new SolidColorBrush(Color.FromRgb(70, 50, 140)), StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        _grip.Width = _grip.Height = 20;
        _grip.CornerRadius = new CornerRadius(10);
        _grip.Background = new RadialGradientBrush(Color.FromArgb(245, 255, 255, 255), Color.FromArgb(215, 220, 212, 255)) { GradientOrigin = new Point(.3, .25) };
        _grip.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        _grip.BorderThickness = new Thickness(1.2);
        _grip.Child = gripIcon;
        _grip.HorizontalAlignment = HorizontalAlignment.Right;
        _grip.VerticalAlignment = VerticalAlignment.Bottom;
        _grip.Cursor = Cursors.SizeNWSE;
        _grip.ToolTip = "Drag to resize  (or scroll over the bubble)";
        _grip.Opacity = 0;
        _grip.IsHitTestVisible = false;
        _grip.RenderTransformOrigin = new Point(.5, .5);
        _grip.RenderTransform = _gripScale;

        _circle.RenderTransformOrigin = new Point(.5, .5);
        _circle.RenderTransform = _scale;
        _circle.Children.Add(_sphereBox);
        _circle.Children.Add(_badge);
        _circle.Children.Add(_grip);

        _root.Children.Add(_shadow);
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

    /// <summary>Recolours the glass for an accent. The same recipe as docs/bubble.svg.</summary>
    void ApplyColors(AppSettings s)
    {
        var (a, b) = Palette(s.Accent);
        Color hi = Lighten(b, .62), rimc = Lighten(b, .8), deep = Darken(a, .58);

        _bodyBrush = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.33 + _tiltX * .08, .24 + _tiltY * .08), RadiusX = .78, RadiusY = .78 };
        _bodyBrush.GradientStops.Add(new GradientStop(WithAlpha(hi, .92), 0));
        _bodyBrush.GradientStops.Add(new GradientStop(WithAlpha(b, .92), .26));
        _bodyBrush.GradientStops.Add(new GradientStop(WithAlpha(a, .95), .62));
        _bodyBrush.GradientStops.Add(new GradientStop(WithAlpha(deep, .98), 1));
        _body.Fill = _bodyBrush;

        _edge.Fill = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), .78), new(Color.FromArgb(107, 5, 0, 26), 1),
        }) { Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };

        _refl.Fill = new RadialGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(rimc, .62), 0), new(WithAlpha(hi, .18), .6), new(WithAlpha(hi, 0), 1),
        }) { Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };

        _rim.Stroke = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(242, 255, 255, 255), 0), new(Color.FromArgb(15, 255, 255, 255), .42),
            new(Color.FromArgb(13, 255, 255, 255), .6), new(WithAlpha(rimc, .75), 1),
        }, new Point(0, 0), new Point(1, 1));

        _ring1.Stroke = new SolidColorBrush(a); _ring2.Stroke = new SolidColorBrush(b);
    }

    /// <summary>Applies <see cref="_size"/> to every element (pure layout - does not touch the window).</summary>
    void ApplyLayout()
    {
        var w = _size + 2 * Pad;
        _root.Width = _root.Height = w;
        _circle.Width = _circle.Height = _size;
        foreach (var e in new[] { _ring1, _ring2 }) e.Width = e.Height = _size;
        _shadow.Width = _size * .95; _shadow.Height = _size * .17;
        _shadowShift.Y = _size * .545;
        _ringTarget = (_size + Pad * 1.7) / _size;

        var k = Math.Clamp(_size / 64.0, .8, 1.3);                  // keep the badge in proportion
        _badge.LayoutTransform = new ScaleTransform(k, k);
        _badge.Margin = new Thickness(0, -7 * k, -7 * k, 0);
        var m = _size * 0.146 - 10;                                  // centre the grip on the circle's 45 degree point
        _grip.Margin = new Thickness(0, 0, m, m);
    }

    public void ApplySettings(AppSettings s, bool initial = false)
    {
        var oldSpx = SPx;
        var oldSize = _size;
        _s = s;
        _size = Math.Clamp(s.BubbleSize, MinSize, MaxSize);
        ApplyColors(s);
        ApplyLayout();

        if (initial) { Width = Height = _size + 2 * Pad; Opacity = _s.BubbleOpacity; return; }

        if (_hwnd != IntPtr.Zero && Math.Abs(_size - oldSize) > .01) ResizeWindow(oldSpx);
        if (_s.SnapToEdge) { if (_dock == 0) _dock = 1; } else { _dock = 0; Reveal(); }
        if (_hwnd != IntPtr.Zero && !_dragging && !_resizing) SnapAndPlace(animated: false);
        SetOpacityNow(_peeking ? _s.BubbleOpacity * 0.6 : _s.BubbleOpacity);
        RestartIdle();
    }

    /// <summary>Resizes the native window to match <see cref="_size"/>, keeping the circle's centre where it was.</summary>
    void ResizeWindow(int oldSpx)
    {
        var r = Native.GetRect(_hwnd);
        double cx = (r.Left + r.Right) / 2.0, cy = (r.Top + r.Bottom) / 2.0;
        var w = WinPx;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(cx - w / 2.0), (int)Math.Round(cy - w / 2.0), w, w, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        var d = (SPx - oldSpx) / 2;
        _circleX -= d; _circleY -= d;
    }

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
        (string Header, string Action)[] items =
        {
            ("Open reminders", "open"), ("New reminder", "new"), ("-", ""),
            ("Make bigger", "bigger"), ("Make smaller", "smaller"), ("Reset size", "reset"), ("-", ""),
            ("Settings", "settings"), ("Hide bubble", "hide"), ("-", ""), ("Quit Reminder Bubble", "quit"),
        };
        try
        {
            var dict = (ResourceDictionary)XamlReader.Parse(MenuXaml);
            Resources.MergedDictionaries.Add(dict);
            menu = new ContextMenu { Style = (Style)dict["RbMenu"] };
            foreach (var (header, action) in items)
            {
                if (header == "-") { menu.Items.Add(new Separator { Style = (Style)dict["RbSep"] }); continue; }
                var mi = new MenuItem { Header = header, Style = (Style)dict["RbItem"] };
                var a = action;
                mi.Click += (_, _) => OnMenu(a);
                menu.Items.Add(mi);
            }
        }
        catch (Exception ex)
        {
            // Styling is cosmetic - fall back to the stock menu rather than failing to start.
            Log.Write("Menu style failed: " + ex.Message);
            menu = new ContextMenu();
            foreach (var (header, action) in items)
            {
                if (header == "-") { menu.Items.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = header };
                var a = action;
                mi.Click += (_, _) => OnMenu(a);
                menu.Items.Add(mi);
            }
        }
        menu.Opened += (_, _) => { Activate(); _idle.Stop(); };
        menu.Closed += (_, _) => RestartIdle();
        _root.ContextMenu = menu;
    }

    void OnMenu(string action)
    {
        switch (action)
        {
            case "bigger": ChangeSize(_size + 8, commitNow: true); break;
            case "smaller": ChangeSize(_size - 8, commitNow: true); break;
            case "reset": ChangeSize(64, commitNow: true); break;
            default: MenuAction?.Invoke(action); break;
        }
    }

    // ======================================================================================
    // Input: hover (with a pointer-following highlight), press, click vs. drag, resize
    // ======================================================================================
    void WireInput()
    {
        _root.MouseEnter += (_, _) =>
        {
            _hover = true; _idle.Stop(); Reveal();
            if (!_pressed) ScaleTo(1.06, 220);
            ShowGrip(true);
        };
        _root.MouseLeave += (_, _) =>
        {
            if (_resizing) return;
            _hover = false;
            if (!_pressed) ScaleTo(1.0, 280);
            ShowGrip(false);
            TiltTo(0, 0);
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
            ScaleTo(0.94, 90);
            e.Handled = true;
        };
        _root.MouseMove += (_, e) =>
        {
            if (!_pressed)
            {
                if (!_resizing) FollowPointer(e.GetPosition(_circle));
                return;
            }
            Native.GetCursorPos(out var c);
            int dx = c.X - _pressCursor.X, dy = c.Y - _pressCursor.Y;
            if (!_dragging && Math.Abs(dx) + Math.Abs(dy) > 5)
            {
                _dragging = true;
                _moveTween?.Stop();
                _peeking = false;
                SetOpacityNow(_s.BubbleOpacity);
                ShowGrip(false);
            }
            if (_dragging) Native.Move(_hwnd, _pressWindow.X + dx, _pressWindow.Y + dy);   // anywhere, on any monitor
        };
        _root.MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed) return;
            _pressed = false;
            _root.ReleaseMouseCapture();
            if (_dragging)
            {
                _dragging = false;
                ScaleTo(_hover ? 1.06 : 1.0, 260);
                var r = Native.GetRect(_hwnd);
                _circleX = r.Left + PadPx; _circleY = r.Top + PadPx;
                SnapAndPlace(animated: true);          // free mode: just keeps it on a screen; snap mode: glides to the edge
                if (_hover) ShowGrip(true);
            }
            else
            {
                ScaleTo(1.06, 140);
                Clicked?.Invoke();
            }
            e.Handled = true;
        };
        _root.MouseRightButtonDown += (_, _) => Activate();

        // scroll over the bubble to resize it
        _root.MouseWheel += (_, e) =>
        {
            if (_peeking || _dragging) return;
            ChangeSize(_size + Math.Sign(e.Delta) * 4, commitNow: false);
            e.Handled = true;
        };

        // drag the grip to resize
        _grip.MouseLeftButtonDown += (_, e) =>
        {
            _resizing = true;
            _grip.CaptureMouse();
            _idle.Stop();
            e.Handled = true;
        };
        _grip.MouseMove += (_, e) =>
        {
            if (!_resizing) return;
            Native.GetCursorPos(out var c);
            var r = Native.GetRect(_hwnd);
            double dx = c.X - (r.Left + r.Right) / 2.0, dy = c.Y - (r.Top + r.Bottom) / 2.0;
            // The grip sits on the circle's edge, so its distance from the centre is the radius.
            ChangeSize(Math.Round(2 * Math.Sqrt(dx * dx + dy * dy) / Dpi / 2) * 2, commitNow: false);
            e.Handled = true;
        };
        _grip.MouseLeftButtonUp += (_, e) =>
        {
            if (!_resizing) return;
            _resizing = false;
            _grip.ReleaseMouseCapture();
            CommitSize();
            if (!_root.IsMouseOver) { _hover = false; ScaleTo(1.0, 280); ShowGrip(false); RestartIdle(); }
            e.Handled = true;
        };
    }

    // ---- resizing ---------------------------------------------------------------------------
    void ChangeSize(double size, bool commitNow)
    {
        size = Math.Clamp(Math.Round(size), MinSize, MaxSize);
        if (Math.Abs(size - _size) < .5 || _hwnd == IntPtr.Zero) return;
        var oldSpx = SPx;
        _size = size;
        ApplyLayout();
        ResizeWindow(oldSpx);
        ClampNow();
        if (commitNow) CommitSize(); else { _commit.Stop(); _commit.Start(); }   // persist once the gesture settles
    }

    void CommitSize()
    {
        _commit.Stop();
        SavePosition();
        SizeCommitted?.Invoke((int)_size);
    }

    /// <summary>Keeps the circle on screen after a resize, without animation or saving.</summary>
    void ClampNow()
    {
        var (x, y) = RestWindowPos(_dock, Area());
        _circleX = x + PadPx; _circleY = y + PadPx;
        if (!_peeking) Native.Move(_hwnd, x, y);
    }

    // ---- pointer-following highlight: the glass catches the "light" from where the mouse is ------
    void FollowPointer(Point p)
    {
        if (_size <= 0) return;
        _tiltTween?.Stop();
        var nx = Math.Clamp((p.X / _size - .5) * 2, -1, 1);
        var ny = Math.Clamp((p.Y / _size - .5) * 2, -1, 1);
        SetTilt(nx, ny);
    }

    void SetTilt(double nx, double ny)
    {
        _tiltX = nx; _tiltY = ny;
        _glossShift.X = nx * 5; _glossShift.Y = ny * 4;
        _bodyBrush.GradientOrigin = new Point(.33 + nx * .08, .24 + ny * .08);
    }

    void TiltTo(double nx, double ny)
    {
        _tiltTween?.Stop();
        double x0 = _tiltX, y0 = _tiltY;
        _tiltTween = Tween.Run(320, Ease.OutCubic, t => SetTilt(Tween.Lerp(x0, nx, t), Tween.Lerp(y0, ny, t)));
    }

    void ShowGrip(bool show)
    {
        _grip.IsHitTestVisible = show;
        var dur = TimeSpan.FromMilliseconds(show ? 200 : 140);
        var fade = new DoubleAnimation(show ? 1 : 0, dur) { EasingFunction = EaseOut };
        var grow = new DoubleAnimation(show ? 1 : .6, dur) { EasingFunction = show ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .8 } : EaseOut };
        _grip.BeginAnimation(OpacityProperty, fade);
        _gripScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _gripScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
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

    // ======================================================================================
    // Placement: free anywhere (default), or glued to a screen edge
    // ======================================================================================
    /// <summary>The area the circle may occupy: the whole monitor when floating free, the work area (above the taskbar) when snapping.</summary>
    Drawing.Rectangle Area()
    {
        var r = Native.GetRect(_hwnd);
        var screen = Forms.Screen.FromRectangle(new Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
        return _s.SnapToEdge ? screen.WorkingArea : screen.Bounds;
    }

    int Gap => _s.SnapToEdge ? (int)Math.Round(10 * Dpi) : 0;

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

    /// <summary>
    /// Moves the circle to its resting place for the current mode: floating free it only has to stay on a screen;
    /// snapping, it is glued to the nearest side edge.
    /// </summary>
    void SnapAndPlace(bool animated)
    {
        var w = Area();
        if (_s.SnapToEdge) _dock = (_circleX + SPx / 2) < (w.Left + w.Width / 2) ? -1 : 1; else _dock = 0;
        var (x, y) = RestWindowPos(_dock, w);
        _circleX = x + PadPx; _circleY = y + PadPx;
        _peeking = false;
        MoveWindowTo(x, y, animated ? (_s.SnapToEdge ? 460 : 260) : 0, _s.SnapToEdge ? Ease.OutBack : Ease.OutCubic, SavePosition);
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

    /// <summary>Called once at start-up: restores the saved position (or the right side) and plays the intro.</summary>
    public void ShowAtStartup(double? savedX, double? savedY)
    {
        Left = -10000; Top = -10000;     // placed for real below, once the HWND exists
        Show();
        _shown = true;
        var w = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Drawing.Rectangle(0, 0, 1280, 720);
        if (savedX is { } sx && savedY is { } sy && IsOnAnyScreen((int)sx, (int)sy))
        { _circleX = (int)sx; _circleY = (int)sy; }
        else
        { _circleX = w.Right - Gap - SPx - (int)(24 * Dpi); _circleY = w.Top + (int)(w.Height * 0.38); }
        // Put the window on the right monitor first so the area used for clamping is the right one.
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
        Forms.Screen.AllScreens.Any(s => s.Bounds.Contains(x + 8, y + 8));

    /// <summary>Re-clamps the bubble after monitor / resolution / taskbar changes.</summary>
    public void EnsureOnScreen()
    {
        if (!_shown || _hwnd == IntPtr.Zero || _dragging || _resizing) return;
        SnapAndPlace(animated: false);
        RestartIdle();
    }

    // ======================================================================================
    // Docking ("peek") behaviour - only when snapping to an edge
    // ======================================================================================
    void RestartIdle()
    {
        _idle.Stop();
        if (_s.SnapToEdge && _s.AutoHide && !_hover && !_alerting && !_panelOpen && !_dragging && !_resizing && _dock != 0 && _shown) _idle.Start();
    }

    void DockAway()
    {
        if (!_s.SnapToEdge || !_s.AutoHide || _hover || _alerting || _panelOpen || _dragging || _resizing || _dock == 0 || _peeking) return;
        var w = Area();
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
        var (x, y) = RestWindowPos(_dock, Area());
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
        a.KeyFrames.Add(new EasingDoubleKeyFrame(_hover ? 1.06 : 1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480)), new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }));
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
        _topmost.Stop(); _idle.Stop(); _commit.Stop(); _moveTween?.Stop(); _tiltTween?.Stop();
    }
}
