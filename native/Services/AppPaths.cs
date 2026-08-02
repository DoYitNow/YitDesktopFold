namespace YitDesktopFold.Native.Services;

public static class AppPaths
{
    private const string DataDirectoryOverride = "YITDESKTOPFOLD_DATA_DIR";

    public static string RootDirectory { get; } = ResolveRootDirectory();

    public static string ManagedShortcutsDirectory { get; } = Path.Combine(RootDirectory, "Shortcuts");

    public static string StateFile { get; } = Path.Combine(RootDirectory, "state.json");

    public static string BackupStateFile { get; } = Path.Combine(RootDirectory, "state.backup.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ManagedShortcutsDirectory);
    }

    private static string ResolveRootDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryOverride);
        return string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YitDesktopFold")
            : Path.GetFullPath(overrideDirectory);
    }
}
