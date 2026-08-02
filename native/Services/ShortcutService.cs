using System.Diagnostics;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Launch and desktop-location validation only. Organizer classification is
/// metadata; this service deliberately performs no import/copy/move operation.
/// </summary>
public static class ShortcutService
{
    public static bool IsSupportedPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (Directory.Exists(path) || File.Exists(path) || DesktopShellItemService.IsShellNamespacePath(path));

    public static bool IsDesktopItem(string path) =>
        IsSupportedPath(path) &&
        (DesktopShellItemService.IsShellNamespacePath(path) || AppPaths.IsDirectDesktopItem(path));

    public static void Launch(ShortcutItem item)
    {
        if (!File.Exists(item.LaunchPath) && !Directory.Exists(item.LaunchPath) &&
            !DesktopShellItemService.IsShellNamespacePath(item.LaunchPath))
        {
            throw new FileNotFoundException("桌面项目已不存在。", item.LaunchPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = item.LaunchPath,
            UseShellExecute = true,
            WorkingDirectory = Directory.Exists(item.LaunchPath)
                ? item.LaunchPath
                : Path.GetDirectoryName(item.LaunchPath) ?? string.Empty,
        });
    }

    public static void OpenDesktopDirectory()
    {
        if (!Directory.Exists(AppPaths.DesktopDirectory))
        {
            Directory.CreateDirectory(AppPaths.DesktopDirectory);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.DesktopDirectory,
            UseShellExecute = true,
        });
    }

}
