using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Mirrors the standard virtual icons that Explorer can place on the Desktop.
/// They have no filesystem path but remain launchable Shell namespace items.
/// </summary>
public static class DesktopShellItemService
{
    private const string ShellIdListFormat = "Shell IDList Array";
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;
    private const uint ShcneUpdateDir = 0x00001000;
    private const uint ShcnfIdList = 0x0000;
    private const uint ShcnfFlushNoWait = 0x2000;
    private const int CsidlDesktop = 0x0000;
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
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetCatalogEntry(string path, out DesktopCatalogEntry entry)
    {
        var definition = Definitions.FirstOrDefault(candidate =>
            path.Contains(candidate.ClassId, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            entry = default!;
            return false;
        }

        entry = new DesktopCatalogEntry(
            "shell:" + definition.ClassId,
            "shell:::" + definition.ClassId,
            definition.DisplayName);
        return true;
    }

    public static IReadOnlyList<string> ExtractDesktopDropPaths(IDataObject data)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] filePaths)
            {
                paths.UnionWith(filePaths);
            }
        }
        catch (ExternalException)
        {
            // Explorer can withdraw a delayed-rendered format while a drag is ending.
        }

        try
        {
            if (data.GetDataPresent(ShellIdListFormat, autoConvert: false))
            {
                var shellData = data.GetData(ShellIdListFormat, autoConvert: false);
                paths.UnionWith(ExtractShellNamespacePaths(shellData));
            }
        }
        catch (ExternalException)
        {
            // A stale COM data object is treated as a cancelled drag.
        }

        return paths
            .Where(ShortcutService.IsDesktopItem)
            .ToArray();
    }

    public static bool PrepareHideAssignedItem(ShortcutItem item)
    {
        if (!TryGetDefinition(item.LaunchPath, out var definition))
        {
            return false;
        }

        if (item.NativeVisibilityManaged)
        {
            return true;
        }

        using var key = Registry.CurrentUser.OpenSubKey(VisibilityKey, writable: false);
        var existing = key?.GetValue(definition.ClassId, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (existing is int hidden && hidden != 0)
        {
            return true;
        }

        item.NativeShellVisibilityRestoreValue = existing is int originalValue
            ? originalValue
            : null;
        item.NativeVisibilityManaged = true;
        return true;
    }

    public static bool ApplyPreparedHideAssignedItem(ShortcutItem item)
    {
        if (!TryGetDefinition(item.LaunchPath, out var definition))
        {
            return false;
        }

        using (var existingKey = Registry.CurrentUser.OpenSubKey(VisibilityKey, writable: false))
        {
            if (existingKey?.GetValue(definition.ClassId) is int hidden && hidden != 0)
            {
                return true;
            }
        }

        if (!item.NativeVisibilityManaged)
        {
            return false;
        }

        using var key = Registry.CurrentUser.CreateSubKey(VisibilityKey, writable: true);
        if (key is null)
        {
            return false;
        }

        key.SetValue(definition.ClassId, 1, RegistryValueKind.DWord);
        NotifyDesktopChanged();
        return true;
    }

    public static void ShowUnassignedItem(ShortcutItem item)
    {
        if (!item.NativeVisibilityManaged ||
            !TryGetDefinition(item.LaunchPath, out var definition))
        {
            return;
        }

        object? existing;
        using (var existingKey = Registry.CurrentUser.OpenSubKey(VisibilityKey, writable: false))
        {
            existing = existingKey?.GetValue(
                definition.ClassId,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        var needsChange = item.NativeShellVisibilityRestoreValue is int originalValue
            ? existing is not int currentValue || currentValue != originalValue
            : existing is not null;

        if (needsChange)
        {
            using var key = Registry.CurrentUser.CreateSubKey(VisibilityKey, writable: true);
            if (key is null)
            {
                throw new SecurityException("无法恢复 Shell 桌面图标的可见性。");
            }

            if (item.NativeShellVisibilityRestoreValue is int restoreValue)
            {
                key.SetValue(definition.ClassId, restoreValue, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(definition.ClassId, throwOnMissingValue: false);
            }
        }

        item.NativeVisibilityManaged = false;
        item.NativeShellVisibilityRestoreValue = null;
        if (needsChange)
        {
            NotifyDesktopChanged();
        }
    }

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

    private static bool TryGetDefinition(string path, out ShellDesktopDefinition definition)
    {
        var match = Definitions.FirstOrDefault(candidate =>
            path.Contains(candidate.ClassId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            definition = null!;
            return false;
        }

        definition = match;
        return true;
    }

    private static IEnumerable<string> ExtractShellNamespacePaths(object? data)
    {
        var bytes = data switch
        {
            MemoryStream stream => stream.ToArray(),
            byte[] value => value,
            _ => [],
        };
        if (bytes.Length < 8)
        {
            yield break;
        }

        var count = BitConverter.ToUInt32(bytes, 0);
        if (count == 0 || count > 256 || 4L + (count + 1L) * 4L > bytes.Length)
        {
            yield break;
        }

        var rawParentOffset = BitConverter.ToUInt32(bytes, 4);
        if (rawParentOffset > int.MaxValue)
        {
            yield break;
        }

        var parentOffset = (int)rawParentOffset;
        for (var index = 0; index < count; index++)
        {
            var rawChildOffset = BitConverter.ToUInt32(bytes, 8 + index * 4);
            if (rawChildOffset > int.MaxValue)
            {
                continue;
            }

            var childOffset = (int)rawChildOffset;
            var absolutePidl = CombinePidls(bytes, parentOffset, childOffset);
            if (absolutePidl.Length == 0)
            {
                continue;
            }

            var handle = GCHandle.Alloc(absolutePidl, GCHandleType.Pinned);
            try
            {
                if (SHGetNameFromIDList(
                        handle.AddrOfPinnedObject(),
                        SigdnDesktopAbsoluteParsing,
                        out var namePointer) < 0 ||
                    namePointer == IntPtr.Zero)
                {
                    continue;
                }

                string? parsingName;
                try
                {
                    parsingName = Marshal.PtrToStringUni(namePointer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(namePointer);
                }

                if (parsingName is not null &&
                    TryGetCatalogEntry(parsingName, out var entry))
                {
                    yield return entry.Path;
                }
            }
            finally
            {
                handle.Free();
            }
        }
    }

    private static byte[] CombinePidls(byte[] source, int parentOffset, int childOffset)
    {
        var parentLength = GetPidlLength(source, parentOffset);
        var childLength = GetPidlLength(source, childOffset);
        if (parentLength < 2 || childLength < 2)
        {
            return [];
        }

        var result = new byte[parentLength + childLength - 2];
        Buffer.BlockCopy(source, parentOffset, result, 0, parentLength - 2);
        Buffer.BlockCopy(source, childOffset, result, parentLength - 2, childLength);
        return result;
    }

    private static int GetPidlLength(byte[] source, int offset)
    {
        if (offset < 0 || offset + 2 > source.Length)
        {
            return 0;
        }

        var position = offset;
        while (position + 2 <= source.Length)
        {
            var itemLength = BitConverter.ToUInt16(source, position);
            if (itemLength == 0)
            {
                return position - offset + 2;
            }

            if (itemLength < 2 || position + itemLength > source.Length)
            {
                return 0;
            }

            position += itemLength;
        }

        return 0;
    }

    private static void NotifyDesktopChanged()
    {
        if (SHGetSpecialFolderLocation(IntPtr.Zero, CsidlDesktop, out var desktopPidl) < 0 ||
            desktopPidl == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SHChangeNotify(
                ShcneUpdateDir,
                ShcnfIdList | ShcnfFlushNoWait,
                desktopPidl,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeCoTaskMem(desktopPidl);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(
        IntPtr pidl,
        uint nameType,
        out IntPtr name);

    [DllImport("shell32.dll")]
    private static extern int SHGetSpecialFolderLocation(
        IntPtr owner,
        int folder,
        out IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);

    private sealed record ShellDesktopDefinition(
        string ClassId,
        string DisplayName,
        bool VisibleByDefault);
}
