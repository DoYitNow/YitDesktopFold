using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Temporarily hides Explorer's icon presenter while organizer mirrors are
/// active. This changes only the live Shell view; no registry setting or file
/// attribute is touched, and the original visibility is restored on exit.
/// </summary>
public sealed class DesktopIconVisibilityService : IDisposable
{
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const string OwnershipProperty = "YitDesktopFold.IconViewHidden";
    private const string ExplorerAdvancedKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private readonly DispatcherTimer _recoveryTimer;
    private readonly Dictionary<IntPtr, bool> _originalVisibility = [];
    private bool _disposed;

    public DesktopIconVisibilityService()
    {
        HideCurrentView();
        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _recoveryTimer.Tick += RecoveryTimer_Tick;
        _recoveryTimer.Start();
    }

    private void RecoveryTimer_Tick(object? sender, EventArgs e) => HideCurrentView();

    private void HideCurrentView()
    {
        if (_disposed)
        {
            return;
        }

        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            return;
        }

        if (!_originalVisibility.ContainsKey(listView))
        {
            _originalVisibility[listView] =
                IsWindowVisible(listView) ||
                GetProp(listView, OwnershipProperty) != IntPtr.Zero ||
                ExplorerSettingsShowDesktopIcons();
            _ = SetProp(listView, OwnershipProperty, new IntPtr(1));
        }

        if (IsWindowVisible(listView))
        {
            _ = ShowWindow(listView, SwHide);
        }
    }

    private static IntPtr FindDesktopListView()
    {
        var shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var defView = FindWindowEx(shellWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            var worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    break;
                }
            }
        }

        return defView == IntPtr.Zero
            ? IntPtr.Zero
            : FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recoveryTimer.Stop();
        _recoveryTimer.Tick -= RecoveryTimer_Tick;
        foreach (var (window, wasVisible) in _originalVisibility)
        {
            if (!IsWindow(window))
            {
                continue;
            }

            _ = RemoveProp(window, OwnershipProperty);
            if (wasVisible)
            {
                _ = ShowWindow(window, SwShowNoActivate);
            }
        }

        _originalVisibility.Clear();
    }

    private static bool ExplorerSettingsShowDesktopIcons()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey, writable: false);
            return key?.GetValue("HideIcons") is not int hidden || hidden == 0;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProp(IntPtr window, string name, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr window, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr RemoveProp(IntPtr window, string name);
}
