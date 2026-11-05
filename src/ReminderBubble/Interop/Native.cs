using System.Runtime.InteropServices;

namespace ReminderBubble.Interop;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const uint LWA_ALPHA = 0x2;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 2;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    public static RECT GetRect(IntPtr hWnd) { GetWindowRect(hWnd, out var r); return r; }

    public static void AddExStyle(IntPtr hWnd, int style) =>
        SetWindowLong(hWnd, GWL_EXSTYLE, GetWindowLong(hWnd, GWL_EXSTYLE) | style);

    public static void Move(IntPtr hWnd, int x, int y) =>
        SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    public static void KeepOnTop(IntPtr hWnd) =>
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Whole-window opacity (including child HWNDs such as WebView2). 1.0 removes the layered style again.</summary>
    public static void SetOpacity(IntPtr hWnd, double opacity)
    {
        var ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        if (opacity >= 0.999)
        {
            if ((ex & WS_EX_LAYERED) != 0) SetWindowLong(hWnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
            return;
        }
        if ((ex & WS_EX_LAYERED) == 0) SetWindowLong(hWnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hWnd, 0, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), LWA_ALPHA);
    }

    /// <summary>True when the foreground window belongs to this process (our own popups, WebView2 pickers, ...).</summary>
    public static bool ForegroundIsOurs()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out var pid);
        return pid == (uint)Environment.ProcessId;
    }
}
