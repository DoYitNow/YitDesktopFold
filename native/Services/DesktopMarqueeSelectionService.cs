using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Restores desktop marquee selection while Explorer's duplicate icon presenter
/// is hidden. The low-level hook only observes left-button gestures that start
/// on Explorer's wallpaper windows and never consumes the native mouse input.
/// </summary>
public sealed class DesktopMarqueeSelectionService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int VkControl = 0x11;
    private const uint GaRoot = 2;
    private readonly MouseHookProcedure _hookProcedure;
    private readonly DesktopSelectionOverlayWindow _overlay = new();
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

    public event EventHandler<DesktopMarqueeEventArgs>? SelectionCompleted;

    public event EventHandler? ClearRequested;

    private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_disposed)
        {
            var mouseMessage = unchecked((int)message.ToInt64());
            var information = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
            switch (mouseMessage)
            {
                case WmLButtonDown when IsDesktopWallpaperPoint(information.Point):
                    _start = information.Point;
                    _tracking = true;
                    _moved = false;
                    _additive = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
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
        _overlay.ShowSelection(CreateRect(_start, current));
    }

    private void CompleteSelection(NativePoint current)
    {
        _tracking = false;
        _overlay.HideSelection();
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
        var window = WindowFromPoint(point);
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

        _overlay.Close();
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
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, char[] className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

public sealed record DesktopMarqueeEventArgs(Rect ScreenBounds, bool Additive);

internal sealed class DesktopSelectionOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private IntPtr _handle;
    private DesktopZOrderHost? _desktopHost;

    public DesktopSelectionOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Topmost = false;
        Content = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 72, 135, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 105, 162, 232)),
            BorderThickness = new Thickness(1),
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
            style |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            _ = SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(style));
            _desktopHost = new DesktopZOrderHost(this);
            _desktopHost.Attach();
            _desktopHost.UpdateBackdrop(
                enabled: false,
                opacity: 0,
                useLightBackground: false,
                hideSurface: true);
        };
        Closed += (_, _) =>
        {
            _desktopHost?.Dispose();
            _desktopHost = null;
        };
    }

    public void ShowSelection(Rect screenBounds)
    {
        var wasVisible = IsVisible;
        if (!wasVisible)
        {
            Show();
        }

        _ = SetWindowPos(
            _handle,
            IntPtr.Zero,
            (int)Math.Floor(screenBounds.Left),
            (int)Math.Floor(screenBounds.Top),
            Math.Max(1, (int)Math.Ceiling(screenBounds.Width)),
            Math.Max(1, (int)Math.Ceiling(screenBounds.Height)),
            SwpNoActivate | SwpNoZOrder | SwpShowWindow);
        if (!wasVisible)
        {
            _desktopHost?.RefreshZOrder();
        }
    }

    public void HideSelection()
    {
        if (_handle != IntPtr.Zero)
        {
            Hide();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
