namespace YitDesktopFold.Native.Services;

public static class AppPaths
{
    private const string DataDirectoryOverride = "YITDESKTOPFOLD_DATA_DIR";
    private const string DesktopDirectoryOverride = "YITDESKTOPFOLD_DESKTOP_DIR";
    private const string CommonDesktopDirectoryOverride = "YITDESKTOPFOLD_COMMON_DESKTOP_DIR";

    public static string RootDirectory { get; } = ResolveRootDirectory();

    /// <summary>
    /// Read-only compatibility location used while upgrading schema-v2 data.
    /// New desktop items are never copied here.
    /// </summary>
    public static string LegacyManagedShortcutsDirectory { get; } = Path.Combine(RootDirectory, "Shortcuts");

    public static string DesktopDirectory { get; } = ResolveSpecialFolder(
        DesktopDirectoryOverride,
        Environment.SpecialFolder.DesktopDirectory);

    public static string CommonDesktopDirectory { get; } = ResolveSpecialFolder(
        CommonDesktopDirectoryOverride,
        Environment.SpecialFolder.CommonDesktopDirectory);

    public static bool UsesRealWindowsDesktop { get; } =
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DesktopDirectoryOverride)) &&
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CommonDesktopDirectoryOverride));

    public static string StateFile { get; } = Path.Combine(RootDirectory, "state.json");

    public static string BackupStateFile { get; } = Path.Combine(RootDirectory, "state.backup.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
    }

    public static bool IsDirectDesktopItem(string path) =>
        IsDirectChild(path, DesktopDirectory) || IsDirectChild(path, CommonDesktopDirectory);

    public static bool IsUnderLegacyManagedDirectory(string path) =>
        IsUnderDirectory(path, LegacyManagedShortcutsDirectory);

    private static string ResolveRootDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryOverride);
        return string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YitDesktopFold")
            : Path.GetFullPath(overrideDirectory);
    }

    private static string ResolveSpecialFolder(string overrideName, Environment.SpecialFolder folder)
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(overrideName);
        var resolved = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Environment.GetFolderPath(folder)
            : overrideDirectory;
        return string.IsNullOrWhiteSpace(resolved) ? string.Empty : Path.GetFullPath(resolved);
    }

    private static bool IsDirectChild(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return parent is not null &&
                   string.Equals(
                       Path.TrimEndingDirectorySeparator(parent),
                       Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
