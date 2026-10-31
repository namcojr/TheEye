using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TheEye.Windows.Services;

/// <summary>
/// Paints the non-client frame (dark/light system border, rounded corners) to
/// match the custom title bar.
/// </summary>
public static class WindowFrame
{
    private static readonly ConditionalWeakTable<Window, object> HookedWindows = new();

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = ThemeService.CreateMapPalette().IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var rounded = 2;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));

        var border = ThemeService.ToColorRef(ThemeService.GetColor("BorderColor"));
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));

        var caption = ThemeService.ToColorRef(ThemeService.GetColor("WindowBackgroundColor"));
        DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));

        var text = ThemeService.ToColorRef(ThemeService.GetColor("TextColor"));
        DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));

        // Re-applying the frame after a runtime theme switch must only refresh
        // the DWM attributes; the min/max hook is installed once per window.
        var source = HwndSource.FromHwnd(handle);
        if (source is not null && !HookedWindows.TryGetValue(window, out _))
        {
            source.AddHook(WindowProcedure);
            HookedWindows.Add(window, new object());
        }
    }

    private static IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        // WindowChrome by itself lets a borderless window maximize over the
        // taskbar. Constrain it to the work area of the monitor it is on.
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;

        info.MaxPosition.X = workArea.Left - monitorArea.Left;
        info.MaxPosition.Y = workArea.Top - monitorArea.Top;
        info.MaxSize.X = workArea.Right - workArea.Left;
        info.MaxSize.Y = workArea.Bottom - workArea.Top;
        info.MaxTrackSize.X = info.MaxSize.X;
        info.MaxTrackSize.Y = info.MaxSize.Y;

        Marshal.StructureToPtr(info, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public int Flags;
    }
}
