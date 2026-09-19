using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Persists organizer bounds relative to a physical monitor work area. The
/// monitor PnP identity keeps layouts tied to the same display, while ratios
/// allow the layout to survive resolution, scaling, and taskbar changes.
/// </summary>
public static class DisplayPlacementService
{
    public static bool TryRestorePreferred(Window window, OrganizerFolderState folder)
    {
        if (string.IsNullOrWhiteSpace(folder.PreferredMonitorId))
        {
            return false;
        }

        var monitor = EnumerateMonitors().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, folder.PreferredMonitorId, StringComparison.OrdinalIgnoreCase));
        var placement = folder.DisplayPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.MonitorId, folder.PreferredMonitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null || placement is null)
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var bounds = RestoreBounds(
            monitor.WorkArea,
            placement,
            GetMonitorScale(monitor.Handle),
            window.MinWidth,
            window.MinHeight);
        return SetWindowPos(
            handle,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SwpNoZOrder | SwpNoActivate);
    }

    public static void Capture(Window window, OrganizerFolderState folder)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var windowBounds))
        {
            return;
        }

        var monitorHandle = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitor = GetMonitor(monitorHandle);
        if (monitor is null)
        {
            return;
        }

        var placement = CaptureBounds(
            monitor.WorkArea,
            windowBounds,
            GetMonitorScale(monitor.Handle));
        placement.MonitorId = monitor.Id;
        var existing = folder.DisplayPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            folder.DisplayPlacements.Add(placement);
        }
        else
        {
            existing.LeftRatio = placement.LeftRatio;
            existing.TopRatio = placement.TopRatio;
            existing.WidthRatio = placement.WidthRatio;
            existing.HeightRatio = placement.HeightRatio;
        }

        folder.PreferredMonitorId = monitor.Id;
    }

    public static bool IsPreferredMonitorAvailable(OrganizerFolderState folder) =>
        !string.IsNullOrWhiteSpace(folder.PreferredMonitorId) &&
        EnumerateMonitors().Any(candidate =>
            string.Equals(candidate.Id, folder.PreferredMonitorId, StringComparison.OrdinalIgnoreCase));

    public static OrganizerDisplayPlacementState CaptureBounds(
        PhysicalRect workArea,
        PhysicalRect windowBounds,
        double monitorScale = 1)
    {
        var workWidth = Math.Max(1, workArea.Width);
        var workHeight = Math.Max(1, workArea.Height);
        var width = Math.Clamp(windowBounds.Width, 1, workWidth);
        var height = Math.Clamp(windowBounds.Height, 1, workHeight);
        var scale = double.IsFinite(monitorScale) && monitorScale > 0 ? monitorScale : 1;
        var edgeGap = (int)Math.Round(WorkAreaService.EdgeGap * scale);
        return new OrganizerDisplayPlacementState
        {
            LeftRatio = NormalizePositionRatio(
                windowBounds.Left - workArea.Left - edgeGap,
                workWidth - width - edgeGap * 2),
            TopRatio = NormalizePositionRatio(
                windowBounds.Top - workArea.Top - edgeGap,
                workHeight - height - edgeGap * 2),
            WidthRatio = (double)width / workWidth,
            HeightRatio = (double)height / workHeight,
        };
    }

    public static PhysicalRect RestoreBounds(
        PhysicalRect workArea,
        OrganizerDisplayPlacementState placement,
        double monitorScale,
        double minimumWidthDip,
        double minimumHeightDip)
    {
        var scale = double.IsFinite(monitorScale) && monitorScale > 0 ? monitorScale : 1;
        var edgeGap = (int)Math.Round(WorkAreaService.EdgeGap * scale);
        var maximumWidth = Math.Max(1, workArea.Width - edgeGap * 2);
        var maximumHeight = Math.Max(1, workArea.Height - edgeGap * 2);
        var minimumWidth = Math.Min(maximumWidth, Math.Max(1, (int)Math.Ceiling(minimumWidthDip * scale)));
        var minimumHeight = Math.Min(maximumHeight, Math.Max(1, (int)Math.Ceiling(minimumHeightDip * scale)));
        var width = Math.Clamp(
            (int)Math.Round(NormalizeSizeRatio(placement.WidthRatio) * workArea.Width),
            minimumWidth,
            maximumWidth);
        var height = Math.Clamp(
            (int)Math.Round(NormalizeSizeRatio(placement.HeightRatio) * workArea.Height),
            minimumHeight,
            maximumHeight);
        var availableWidth = Math.Max(0, workArea.Width - width - edgeGap * 2);
        var availableHeight = Math.Max(0, workArea.Height - height - edgeGap * 2);
        var left = workArea.Left + edgeGap +
                   (int)Math.Round(NormalizePositionRatio(placement.LeftRatio, 1) * availableWidth);
        var top = workArea.Top + edgeGap +
                  (int)Math.Round(NormalizePositionRatio(placement.TopRatio, 1) * availableHeight);
        return new PhysicalRect(left, top, left + width, top + height);
    }

    private static double NormalizePositionRatio(double value, double denominator) =>
        denominator <= 0 ? 0 : Math.Clamp(value / denominator, 0, 1);

    private static double NormalizeSizeRatio(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 0.25, 0.01, 1);

    private static IReadOnlyList<MonitorDescriptor> EnumerateMonitors()
    {
        var monitors = new List<MonitorDescriptor>();
        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (handle, _, _, _) =>
            {
                var monitor = GetMonitor(handle);
                if (monitor is not null)
                {
                    monitors.Add(monitor);
                }

                return true;
            },
            IntPtr.Zero);
        return monitors;
    }

    private static MonitorDescriptor? GetMonitor(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var information = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty,
        };
        if (!GetMonitorInfo(handle, ref information))
        {
            return null;
        }

        return new MonitorDescriptor(
            handle,
            GetStableMonitorId(information.DeviceName),
            information.Work);
    }

    private static string GetStableMonitorId(string displayName)
    {
        var device = new DisplayDevice
        {
            Size = Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty,
        };
        return EnumDisplayDevices(displayName, 0, ref device, 0) &&
               !string.IsNullOrWhiteSpace(device.DeviceId)
            ? device.DeviceId
            : displayName;
    }

    private static double GetMonitorScale(IntPtr monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var dpiX, out _) >= 0
                ? dpiX / 96d
                : 1;
        }
        catch (DllNotFoundException)
        {
            return 1;
        }
        catch (EntryPointNotFoundException)
        {
            return 1;
        }
    }

    public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    private sealed record MonitorDescriptor(IntPtr Handle, string Id, PhysicalRect WorkArea);

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int MonitorDpiTypeEffective = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public PhysicalRect Monitor;
        public PhysicalRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private delegate bool MonitorEnumProcedure(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorRectangle,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRectangle,
        MonitorEnumProcedure callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string device,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out PhysicalRect rectangle);

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

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
