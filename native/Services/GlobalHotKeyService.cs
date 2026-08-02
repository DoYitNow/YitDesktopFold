using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace YitDesktopFold.Native.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int HotKeyId = 0x5944;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyD = 0x44;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _disposed;

    public GlobalHotKeyService()
    {
        var parameters = new HwndSourceParameters("YitDesktopFold.KeyboardHotKey")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
        IsRegistered = RegisterHotKey(
            _source.Handle,
            HotKeyId,
            ModControl | ModAlt | ModNoRepeat,
            VirtualKeyD);
    }

    public event EventHandler? Pressed;

    public bool IsRegistered { get; }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsRegistered)
        {
            _ = UnregisterHotKey(_source.Handle, HotKeyId);
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
