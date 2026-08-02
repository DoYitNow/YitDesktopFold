using System.Diagnostics;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

public sealed class ShortcutService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".appref-ms", ".exe", ".com", ".bat", ".cmd", ".msc",
    };

    private static readonly HashSet<string> ManagedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".appref-ms",
    };

    public ShortcutService()
    {
        AppPaths.EnsureCreated();
    }

    public static bool IsSupportedPath(string path) =>
        Directory.Exists(path) ||
        (File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path)));

    public bool IsDesktopShortcut(string path) =>
        IsManagedShortcutFile(path) && IsUnderDesktopDirectory(path);

    public ShortcutItem Import(string sourcePath, bool moveDesktopShortcut, int accentIndex)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!IsSupportedPath(fullSourcePath))
        {
            throw new NotSupportedException("仅支持 Windows 快捷方式、网址、应用程序和文件夹。");
        }

        if (!IsManagedShortcutFile(fullSourcePath))
        {
            return new ShortcutItem
            {
                Name = GetDisplayName(fullSourcePath),
                LaunchPath = fullSourcePath,
                OriginalPath = fullSourcePath,
                AccentIndex = accentIndex,
            };
        }

        var destination = CreateUniquePath(
            AppPaths.ManagedShortcutsDirectory,
            Path.GetFileName(fullSourcePath));
        var canMove = moveDesktopShortcut && IsUnderUserDesktopDirectory(fullSourcePath);

        if (canMove)
        {
            File.Move(fullSourcePath, destination);
        }
        else
        {
            File.Copy(fullSourcePath, destination);
        }

        return new ShortcutItem
        {
            Name = GetDisplayName(fullSourcePath),
            LaunchPath = destination,
            OriginalPath = fullSourcePath,
            IsManaged = true,
            WasMovedFromDesktop = canMove,
            AccentIndex = accentIndex,
        };
    }

    public string RemoveAndRecover(ShortcutItem item)
    {
        if (!item.IsManaged || !File.Exists(item.LaunchPath))
        {
            return "已从整理块移除";
        }

        var originalStillExists = item.OriginalPath is { Length: > 0 } && File.Exists(item.OriginalPath);
        if (!item.WasMovedFromDesktop && originalStillExists)
        {
            File.Delete(item.LaunchPath);
            return "已从整理块移除，原快捷方式仍保留";
        }

        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktopDirectory);
        var desiredName = item.OriginalPath is { Length: > 0 }
            ? Path.GetFileName(item.OriginalPath)
            : Path.GetFileName(item.LaunchPath);
        var restoredPath = CreateUniquePath(desktopDirectory, desiredName);
        File.Move(item.LaunchPath, restoredPath);
        return $"已移回桌面 · {Path.GetFileNameWithoutExtension(restoredPath)}";
    }

    public static void Launch(ShortcutItem item)
    {
        if (!File.Exists(item.LaunchPath) && !Directory.Exists(item.LaunchPath))
        {
            throw new FileNotFoundException("快捷方式或目标已不存在。", item.LaunchPath);
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

    public static void OpenManagedDirectory()
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.ManagedShortcutsDirectory,
            UseShellExecute = true,
        });
    }

    private static bool IsManagedShortcutFile(string path) =>
        File.Exists(path) && ManagedExtensions.Contains(Path.GetExtension(path));

    private static bool IsUnderDesktopDirectory(string path) =>
        IsUnderDirectory(path, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)) ||
        IsUnderDirectory(path, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

    private static bool IsUnderUserDesktopDirectory(string path) =>
        IsUnderDirectory(path, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayName(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).Name;
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static string CreateUniquePath(string directory, string fileName)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "快捷方式.lnk" : fileName;
        var extension = Path.GetExtension(safeFileName);
        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var candidate = Path.Combine(directory, safeFileName);
        var suffix = 2;

        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({suffix++}){extension}");
        }

        return candidate;
    }
}
