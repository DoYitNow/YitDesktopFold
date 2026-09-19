using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Observes Explorer's native desktop marquee without consuming mouse input.
/// Explorer remains visible and owns the original rectangle and icon layout;
/// the organizer mirrors receive the same live selection bounds.
/// </summary>
public sealed class DesktopMarqueeSelectionService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int VkControl = 0x11;
    private readonly MouseHookProcedure _hookProcedure;
    private IntPtr _hook;
    private NativePoint _start;
    private bool _tracking;
    private bool _moved;
    private bool _additive;
    private bool _disposed;

    public DesktopMarqueeSelectionService()
    {
        _hookProcedure = MouseHookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(
            WhMouseLl,
            _hookProcedure,
            module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName),
            0);
    }

    public event EventHandler<DesktopMarqueeStartedEventArgs>? SelectionStarted;

    public event EventHandler<DesktopMarqueeEventArgs>? SelectionChanged;

    public event EventHandler<DesktopMarqueeEventArgs>? SelectionCompleted;

    public event EventHandler? ClearRequested;

    public event EventHandler<DesktopPointerPressedEventArgs>? PointerPressed;

    private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_disposed)
        {
            var mouseMessage = unchecked((int)message.ToInt64());
            var information = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
            if (mouseMessage is WmLButtonDown or WmRButtonDown or WmMButtonDown)
            {
                PointerPressed?.Invoke(
                    this,
                    new DesktopPointerPressedEventArgs(new Point(information.Point.X, information.Point.Y)));
            }

            switch (mouseMessage)
            {
                case WmLButtonDown when IsDesktopWallpaperPoint(information.Point):
                    _start = information.Point;
                    _tracking = true;
                    _moved = false;
                    _additive = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
                    SelectionStarted?.Invoke(this, new DesktopMarqueeStartedEventArgs(_additive));
                    break;
                case WmMouseMove when _tracking:
                    UpdateSelection(information.Point);
                    break;
                case WmLButtonUp when _tracking:
                    CompleteSelection(information.Point);
                    break;
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private void UpdateSelection(NativePoint current)
    {
        if (!_moved &&
            Math.Abs(current.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _moved = true;
        SelectionChanged?.Invoke(
            this,
            new DesktopMarqueeEventArgs(CreateRect(_start, current), _additive));
    }

    private void CompleteSelection(NativePoint current)
    {
        _tracking = false;
        if (!_moved)
        {
            if (!_additive)
            {
                ClearRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        SelectionCompleted?.Invoke(
            this,
            new DesktopMarqueeEventArgs(CreateRect(_start, current), _additive));
    }

    private static Rect CreateRect(NativePoint first, NativePoint second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static bool IsDesktopWallpaperPoint(NativePoint point)
    {
        if (!DesktopDropTargetService.IsExplorerDesktopPoint(new Point(point.X, point.Y)))
        {
            return false;
        }

        try
        {
            var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
            return element.Current.ControlType != ControlType.ListItem;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr MouseHookProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        MouseHookProcedure procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

public sealed record DesktopMarqueeStartedEventArgs(bool Additive);

public sealed record DesktopMarqueeEventArgs(Rect ScreenBounds, bool Additive);

public sealed record DesktopPointerPressedEventArgs(Point ScreenPosition);
