using System.Runtime.InteropServices;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Selectively suppresses only filesystem items assigned to organizer blocks.
/// The item stays in the real Desktop directory; its original Hidden state is
/// preserved by tracking whether this application added the attribute.
/// </summary>
public sealed class DesktopNativeVisibilityService
{
    private const uint ShcneAttributes = 0x00000800;
    private const uint ShcnfPathW = 0x0005;
    private const uint ShcnfFlushNoWait = 0x2000;

    public bool Synchronize(OrganizerAppState state)
    {
        var changed = false;
        foreach (var item in state.Folders.SelectMany(folder => folder.Shortcuts))
        {
            try
            {
                var wasManaged = item.NativeVisibilityManaged;
                HideAssignedItem(item);
                changed |= wasManaged != item.NativeVisibilityManaged;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Public Desktop policy can prevent selective suppression.
            }
        }

        return changed;
    }

    public bool HideAssignedItem(ShortcutItem item)
    {
        if (DesktopShellItemService.IsShellNamespacePath(item.LaunchPath))
        {
            return DesktopShellItemService.HideAssignedItem(item);
        }

        if (!File.Exists(item.LaunchPath) && !Directory.Exists(item.LaunchPath))
        {
            return false;
        }

        var attributes = File.GetAttributes(item.LaunchPath);
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            return true;
        }

        File.SetAttributes(item.LaunchPath, attributes | FileAttributes.Hidden);
        NotifyShellAttributesChanged(item.LaunchPath);
        item.NativeVisibilityManaged = true;
        return true;
    }

    public void ShowUnassignedItem(ShortcutItem item)
    {
        if (DesktopShellItemService.IsShellNamespacePath(item.LaunchPath))
        {
            DesktopShellItemService.ShowUnassignedItem(item);
            return;
        }

        if (!item.NativeVisibilityManaged)
        {
            return;
        }

        if (File.Exists(item.LaunchPath) || Directory.Exists(item.LaunchPath))
        {
            var attributes = File.GetAttributes(item.LaunchPath);
            File.SetAttributes(item.LaunchPath, attributes & ~FileAttributes.Hidden);
            NotifyShellAttributesChanged(item.LaunchPath);
        }

        item.NativeVisibilityManaged = false;
    }

    private static void NotifyShellAttributesChanged(string path) =>
        SHChangeNotify(ShcneAttributes, ShcnfPathW | ShcnfFlushNoWait, path, IntPtr.Zero);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string item1,
        IntPtr item2);
}
