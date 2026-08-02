using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YitDesktopFold.Native.Services;

public static class WorkAreaService
{
    public const double EdgeGap = 22;

    public static Rect GetCurrentWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return SystemParameters.WorkArea;
        }

        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(monitorInfo.Work.Left, monitorInfo.Work.Top));
        var bottomRight = transform.Transform(new Point(monitorInfo.Work.Right, monitorInfo.Work.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public static void ClampToWorkArea(Window window)
    {
        var workArea = GetCurrentWorkArea(window);
        var maximumWidth = Math.Max(window.MinWidth, workArea.Width - EdgeGap * 2);
        var maximumHeight = Math.Max(window.MinHeight, workArea.Height - EdgeGap * 2);
        window.Width = Math.Clamp(window.Width, window.MinWidth, maximumWidth);
        window.Height = Math.Clamp(window.Height, window.MinHeight, maximumHeight);
        window.Left = Math.Clamp(
            window.Left,
            workArea.Left + EdgeGap,
            Math.Max(workArea.Left + EdgeGap, workArea.Right - window.Width - EdgeGap));
        window.Top = Math.Clamp(
            window.Top,
            workArea.Top + EdgeGap,
            Math.Max(workArea.Top + EdgeGap, workArea.Bottom - window.Height - EdgeGap));
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
