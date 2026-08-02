using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace YitDesktopFold.Native.Services;

public enum NativeTrayCommand
{
    CreateOrganizer = 1,
    ToggleAllOrganizers,
    KeyboardMode,
    AssignDesktopItems,
    Settings,
    OpenDesktopDirectory,
    Exit,
}

/// <summary>
/// Lightweight Shell_NotifyIcon host. Keeping the tray native avoids loading
/// the complete Windows Forms and System.Drawing UI stacks into this WPF app.
/// </summary>
public sealed class NativeTrayIconService : IDisposable
{
    private const uint WmAppTray = 0x8000 + 0x5944;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmNull = 0x0000;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 0x0001;
    private const uint NifIcon = 0x0002;
    private const uint NifTip = 0x0004;
    private const uint NifShowTip = 0x0080;
    private const uint NotifyIconVersion4 = 4;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int IdiApplication = 32512;

    private readonly HwndSource _source;
    private readonly int _taskbarCreatedMessage;
    private IntPtr _iconHandle;
    private bool _ownsIcon;
    private bool _disposed;

    public NativeTrayIconService()
    {
        var parameters = new HwndSourceParameters("YitDesktopFold.NativeTray")
        {
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        LoadApplicationIcon();
        AddIcon();
    }

    public event EventHandler<NativeTrayCommand>? CommandInvoked;

    public event EventHandler? DoubleClicked;

    private void LoadApplicationIcon()
    {
        if (Environment.ProcessPath is { } processPath)
        {
            _ = ExtractIconEx(processPath, 0, out var largeIcon, out var smallIcon, 1);
            if (smallIcon != IntPtr.Zero)
            {
                _iconHandle = smallIcon;
                _ownsIcon = true;
                if (largeIcon != IntPtr.Zero)
                {
                    _ = DestroyIcon(largeIcon);
                }

                return;
            }

            if (largeIcon != IntPtr.Zero)
            {
                _iconHandle = largeIcon;
                _ownsIcon = true;
                return;
            }
        }

        _iconHandle = LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
        _ownsIcon = false;
    }

    private void AddIcon()
    {
        if (_disposed || _source.Handle == IntPtr.Zero || _iconHandle == IntPtr.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData();
        if (!ShellNotifyIcon(NimAdd, ref data))
        {
            return;
        }

        data.VersionOrTimeout = NotifyIconVersion4;
        _ = ShellNotifyIcon(NimSetVersion, ref data);
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = _source.Handle,
        Id = 1,
        Flags = NifMessage | NifIcon | NifTip | NifShowTip,
        CallbackMessage = WmAppTray,
        Icon = _iconHandle,
        Tip = "Yit Desktop Fold",
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _taskbarCreatedMessage)
        {
            AddIcon();
            return IntPtr.Zero;
        }

        if (message != WmAppTray)
        {
            return IntPtr.Zero;
        }

        var notification = (uint)(lParam.ToInt64() & 0xFFFF);
        if (notification == WmLButtonDoubleClick)
        {
            handled = true;
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (notification is WmContextMenu or WmRButtonUp)
        {
            handled = true;
            ShowContextMenu();
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendCommand(menu, NativeTrayCommand.CreateOrganizer, "新建整理块");
            AppendCommand(menu, NativeTrayCommand.ToggleAllOrganizers, "显示 / 隐藏全部");
            AppendCommand(menu, NativeTrayCommand.KeyboardMode, "键盘操作模式\tCtrl+Alt+D");
            AppendCommand(menu, NativeTrayCommand.AssignDesktopItems, "归类桌面项目…");
            AppendCommand(menu, NativeTrayCommand.Settings, "外观与吸附…");
            AppendCommand(menu, NativeTrayCommand.OpenDesktopDirectory, "打开桌面目录");
            _ = AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
            AppendCommand(menu, NativeTrayCommand.Exit, "退出");

            if (!GetCursorPos(out var point))
            {
                return;
            }

            _ = SetForegroundWindow(_source.Handle);
            var selected = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmNonotify | TpmReturnCommand,
                point.X,
                point.Y,
                _source.Handle,
                IntPtr.Zero);
            _ = PostMessage(_source.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (selected != 0 && Enum.IsDefined(typeof(NativeTrayCommand), (int)selected))
            {
                CommandInvoked?.Invoke(this, (NativeTrayCommand)selected);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static void AppendCommand(IntPtr menu, NativeTrayCommand command, string text) =>
        _ = AppendMenu(menu, MfString, new UIntPtr((uint)command), text);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var data = CreateNotifyIconData();
        _ = ShellNotifyIcon(NimDelete, ref data);
        _disposed = true;
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        if (_ownsIcon && _iconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_iconHandle);
        }

        _iconHandle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint VersionOrTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string file,
        int iconIndex,
        out IntPtr largeIcon,
        out IntPtr smallIcon,
        uint icons);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr item, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr window,
        IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);
}
