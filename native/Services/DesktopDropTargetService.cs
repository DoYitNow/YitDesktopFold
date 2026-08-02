using System.Runtime.InteropServices;
using System.Windows;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Recognizes a release over Explorer's real desktop without registering a
/// system-wide OLE drop target. Organizer drags remain metadata-only, so the
/// Shell never receives a same-folder file move for an item already on Desktop.
/// </summary>
public static class DesktopDropTargetService
{
    private const uint GaRoot = 2;

    public static bool TryGetCurrentDesktopPoint(out Point screenPoint)
    {
        screenPoint = default;
        if (!GetCursorPos(out var cursorPoint))
        {
            return false;
        }

        screenPoint = new Point(cursorPoint.X, cursorPoint.Y);
        var window = WindowFromPoint(cursorPoint);
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var root = GetAncestor(window, GaRoot);
        if (root == IntPtr.Zero)
        {
            root = window;
        }

        _ = GetWindowThreadProcessId(root, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        var className = new char[64];
        var length = GetClassName(root, className, className.Length);
        if (length <= 0)
        {
            return false;
        }

        var name = new string(className, 0, length);
        return string.Equals(name, "Progman", StringComparison.Ordinal) ||
               string.Equals(name, "WorkerW", StringComparison.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, char[] className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
