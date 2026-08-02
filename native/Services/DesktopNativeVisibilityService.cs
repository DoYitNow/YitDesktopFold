using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Selectively suppresses only filesystem items assigned to organizer blocks.
/// The item stays in the real Desktop directory; its original Hidden state is
/// preserved by tracking whether this application added the attribute.
/// </summary>
public sealed class DesktopNativeVisibilityService
{
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Public Desktop policy can prevent selective suppression.
            }
        }

        return changed;
    }

    public bool HideAssignedItem(ShortcutItem item)
    {
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
        item.NativeVisibilityManaged = true;
        return true;
    }

    public void ShowUnassignedItem(ShortcutItem item)
    {
        if (!item.NativeVisibilityManaged)
        {
            return;
        }

        if (File.Exists(item.LaunchPath) || Directory.Exists(item.LaunchPath))
        {
            var attributes = File.GetAttributes(item.LaunchPath);
            File.SetAttributes(item.LaunchPath, attributes & ~FileAttributes.Hidden);
        }

        item.NativeVisibilityManaged = false;
    }
}
