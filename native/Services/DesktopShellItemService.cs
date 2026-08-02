using Microsoft.Win32;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Mirrors the standard virtual icons that Explorer can place on the Desktop.
/// They have no filesystem path but remain launchable Shell namespace items.
/// </summary>
public static class DesktopShellItemService
{
    private const string VisibilityKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    private static readonly ShellDesktopDefinition[] Definitions =
    [
        new("{645FF040-5081-101B-9F08-00AA002F954E}", "回收站", true),
        new("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "此电脑", false),
        new("{59031A47-3F72-44A7-89C5-5595FE6B30EE}", "用户文件", false),
        new("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "网络", false),
        new("{26EE0668-A00A-44D7-9371-BEB064C98683}", "控制面板", false),
    ];

    public static bool IsShellNamespacePath(string path) =>
        path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<DesktopCatalogEntry> EnumerateVisibleItems()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VisibilityKey, writable: false);
        foreach (var definition in Definitions)
        {
            var configured = key?.GetValue(definition.ClassId);
            var visible = configured is int hidden
                ? hidden == 0
                : definition.VisibleByDefault;
            if (visible)
            {
                yield return new DesktopCatalogEntry(
                    "shell:" + definition.ClassId,
                    "shell:::" + definition.ClassId,
                    definition.DisplayName);
            }
        }
    }

    private sealed record ShellDesktopDefinition(
        string ClassId,
        string DisplayName,
        bool VisibleByDefault);
}
