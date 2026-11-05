using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReminderBubble.Interop;
using Drawing = System.Drawing;
using Shapes = System.Windows.Shapes;

namespace ReminderBubble.Windows;

/// <summary>
/// The macOS "genie" effect. A transparent, click-through window that shows a snapshot of the panel cut into thin
/// vertical strips. Strips nearest the bubble collapse first - each one slides toward the bubble and squeezes
/// vertically - and the rest follow, so the panel funnels into the bubble (or, run backwards, pours out of it).
/// </summary>
public sealed class GenieOverlay : Window
{
    const int Strips = 96;
    /// <summary>How far the collapse is staggered along the panel (0 = everything at once, 1 = fully sequential).</summary>
    const double Spread = 0.75;

    readonly Canvas _canvas = new() { IsHitTestVisible = false };
    readonly Shapes.Rectangle[] _rects = new Shapes.Rectangle[Strips];
    readonly MatrixTransform[] _tf = new MatrixTransform[Strips];
    Tween? _tween;
    bool _closed;
    double _panelLeft, _panelCx, _panelCy, _w, _h, _ax, _ay, _endHalf, _stripW, _rectW;
    int _side;

    public GenieOverlay()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Content = _canvas;
    }

    /// <summary>
    /// Shows the effect. <paramref name="open"/>: bubble -> panel; otherwise panel -> bubble.
    /// <paramref name="onFirstFrame"/> runs once the first frame is on screen (the real window can be swapped out then);
    /// <paramref name="onDone"/> when the animation has finished.
    /// </summary>
    public void Start(BitmapSource snapshot, Drawing.Rectangle panelPx, Drawing.Point anchorPx, int bubbleRadiusPx,
        bool open, double ms, double dpi, Action onFirstFrame, Action onDone)
    {
        // The overlay covers the panel plus the bubble (and a little air around both), in screen pixels.
        const int margin = 24;
        var left = Math.Min(panelPx.Left, anchorPx.X - bubbleRadiusPx) - margin;
        var top = Math.Min(panelPx.Top, anchorPx.Y - bubbleRadiusPx) - margin;
        var right = Math.Max(panelPx.Right, anchorPx.X + bubbleRadiusPx) + margin;
        var bottom = Math.Max(panelPx.Bottom, anchorPx.Y + bubbleRadiusPx) + margin;

        // Everything below is in DIPs, relative to the overlay's top-left.
        _w = panelPx.Width / dpi; _h = panelPx.Height / dpi;
        _panelLeft = (panelPx.Left - left) / dpi;
        _panelCx = _panelLeft + _w / 2;
        _panelCy = (panelPx.Top - top) / dpi + _h / 2;
        _ax = (anchorPx.X - left) / dpi; _ay = (anchorPx.Y - top) / dpi;
        _side = _ax < _panelCx ? -1 : 1;                        // which side of the panel the bubble is on
        _endHalf = Math.Max(3, bubbleRadiusPx / dpi * 0.5);     // the funnel narrows to about the bubble's size
        _stripW = _w / Strips;
        _rectW = _stripW + 1.0;                                 // 1 DIP of overlap hides the seams between strips

        WindowStartupLocation = WindowStartupLocation.Manual;      // never let WPF re-centre it
        Left = left / dpi; Top = top / dpi;
        Width = (right - left) / dpi; Height = (bottom - top) / dpi;
        _canvas.Width = Width; _canvas.Height = Height;

        for (var i = 0; i < Strips; i++)
        {
            var from = i / (double)Strips;
            var brush = new ImageBrush(snapshot)
            {
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(from, 0, Math.Min(_rectW / _w, 1 - from), 1),
                Stretch = Stretch.Fill,
            };
            brush.Freeze();
            _tf[i] = new MatrixTransform();
            _rects[i] = new Shapes.Rectangle { Width = _rectW, Height = _h, Fill = brush, RenderTransform = _tf[i], IsHitTestVisible = false };
        }
        // Far strips first, near strips on top, so the funnel overlaps the way a folded sheet would.
        for (var k = 0; k < Strips; k++) _canvas.Children.Add(_rects[_side < 0 ? Strips - 1 - k : k]);

        Update(open ? 1 : 0);

        // Place it by pixel (per-monitor DPI safe), as a click-through tool window that never takes focus.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, left, top, right - left, bottom - top, Native.SWP_NOACTIVATE);

        ContentRendered += (_, _) =>
        {
            if (_closed) return;
            onFirstFrame();
            _tween = Tween.Run(ms, open ? Ease.OutCubic : Ease.InOutCubic,
                e => Update(open ? 1 - e : e),
                onDone);
        };
        Show();
    }

    /// <summary>p = 0: the panel as it is; p = 1: squeezed into the bubble.</summary>
    void Update(double p)
    {
        for (var i = 0; i < Strips; i++)
        {
            var u = _side < 0 ? 1 - (i + .5) / Strips : (i + .5) / Strips;           // 1 = nearest the bubble
            var q = Math.Clamp(p * (1 + Spread) - (1 - u) * Spread, 0, 1);            // near strips start first
            var e = q * q * (3 - 2 * q);

            var x0 = _panelLeft + i * _stripW + _rectW / 2;                            // centre of this strip at rest
            var cx = x0 + (_ax - x0) * e;
            var cy = _panelCy + (_ay - _panelCy) * e;
            var half = _h / 2 + (_endHalf - _h / 2) * e;
            var width = _rectW + (1.5 - _rectW) * e;

            _tf[i].Matrix = new Matrix(width / _rectW, 0, 0, 2 * half / _h, cx - width / 2, cy - half);
        }
        _canvas.Opacity = p > 0.93 ? (1 - p) / 0.07 : 1;                             // melt into the bubble at the very end
    }

    /// <summary>Stops immediately (a newer open/close took over).</summary>
    public void Abort()
    {
        _closed = true;
        _tween?.Stop();
        try { Close(); } catch { /* already closing */ }
    }

    /// <summary>Gives the snapshot the same softly rounded corners as the real window.</summary>
    public static BitmapSource RoundCorners(BitmapSource source, int radius)
    {
        var src = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);

        void Corner(int cx, int cy, int x0, int y0)
        {
            for (var y = y0; y < y0 + radius && y < h; y++)
                for (var x = x0; x < x0 + radius && x < w; x++)
                {
                    var dx = x + .5 - cx; var dy = y + .5 - cy;
                    var coverage = Math.Clamp(radius - Math.Sqrt(dx * dx + dy * dy) + .5, 0, 1);
                    if (coverage >= 1) continue;
                    var o = y * stride + x * 4;
                    for (var c = 0; c < 4; c++) px[o + c] = (byte)(px[o + c] * coverage);
                }
        }
        Corner(radius, radius, 0, 0);
        Corner(w - radius, radius, w - radius, 0);
        Corner(radius, h - radius, 0, h - radius);
        Corner(w - radius, h - radius, w - radius, h - radius);

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        result.Freeze();
        return result;
    }
}
