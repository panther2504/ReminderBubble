using System.Diagnostics;
using System.Windows.Media;

namespace ReminderBubble.Interop;

public static class Ease
{
    public static double Linear(double t) => t;
    public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double InCubic(double t) => t * t * t;
    public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double OutExpo(double t) => t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);
    /// <summary>Overshoots slightly then settles - gives the bubble its springy snap.</summary>
    public static double OutBack(double t)
    {
        const double c1 = 1.35, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}

/// <summary>
/// Frame-synchronised tween driven by WPF's render loop. Used to animate native window positions and opacity,
/// which WPF's own animation system cannot do for HWND-hosted content.
/// </summary>
public sealed class Tween
{
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly double _ms;
    readonly Func<double, double> _ease;
    readonly Action<double> _frame;
    readonly Action? _done;
    bool _stopped;

    Tween(double ms, Func<double, double> ease, Action<double> frame, Action? done)
    {
        _ms = Math.Max(1, ms); _ease = ease; _frame = frame; _done = done;
        CompositionTarget.Rendering += OnRender;
    }

    public static Tween Run(double ms, Func<double, double> ease, Action<double> frame, Action? done = null) =>
        new(ms, ease, frame, done);

    void OnRender(object? sender, EventArgs e)
    {
        if (_stopped) return;
        var t = Math.Min(1.0, _clock.Elapsed.TotalMilliseconds / _ms);
        _frame(_ease(t));
        if (t < 1.0) return;
        Stop();
        _done?.Invoke();
    }

    /// <summary>Cancels without running the completion callback.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        CompositionTarget.Rendering -= OnRender;
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
