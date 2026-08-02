using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// One-time schema-v2 recovery. Items previously moved into AppData are put
/// back on the real user Desktop before schema-v3 metadata is committed.
/// </summary>
public static class LegacyDesktopMigration
{
    public static bool RequiresMigration(OrganizerAppState state) =>
        state.SchemaVersion < 3 ||
        (state.Folders ?? [])
        .SelectMany(folder => folder.Shortcuts ?? [])
        .Any(IsLegacyManagedItem) ||
        HasLegacyOrphans();

    public static LegacyDesktopMigrationTransaction Begin(OrganizerAppState state)
    {
        var transaction = new LegacyDesktopMigrationTransaction();
        Directory.CreateDirectory(AppPaths.DesktopDirectory);
        AppendLegacyOrphans(state);

        try
        {
            foreach (var item in state.Folders.SelectMany(folder => folder.Shortcuts))
            {
                if (!IsLegacyManagedItem(item))
                {
                    continue;
                }

                transaction.Capture(item);
                var existingDesktopPath = ResolveExistingOriginal(item);
                if (existingDesktopPath is not null)
                {
                    if (File.Exists(item.LaunchPath) && AppPaths.IsUnderLegacyManagedDirectory(item.LaunchPath))
                    {
                        transaction.RecordCleanup(item.LaunchPath);
                    }

                    UpdateReference(item, existingDesktopPath);
                    continue;
                }

                if (!File.Exists(item.LaunchPath) && !Directory.Exists(item.LaunchPath))
                {
                    continue;
                }

                var desiredName = item.OriginalPath is { Length: > 0 }
                    ? Path.GetFileName(item.OriginalPath)
                    : Path.GetFileName(item.LaunchPath);
                var destination = CreateUniquePath(AppPaths.DesktopDirectory, desiredName);
                Move(item.LaunchPath, destination);
                transaction.RecordMove(item.LaunchPath, destination);
                UpdateReference(item, destination);
            }

            state.SchemaVersion = 3;
            return transaction;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static bool IsLegacyManagedItem(ShortcutItem item) =>
        item.IsManaged ||
        (!string.IsNullOrWhiteSpace(item.LaunchPath) &&
         AppPaths.IsUnderLegacyManagedDirectory(item.LaunchPath));

    private static bool HasLegacyOrphans()
    {
        try
        {
            return Directory.Exists(AppPaths.LegacyManagedShortcutsDirectory) &&
                   Directory.EnumerateFileSystemEntries(AppPaths.LegacyManagedShortcutsDirectory).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void AppendLegacyOrphans(OrganizerAppState state)
    {
        if (!Directory.Exists(AppPaths.LegacyManagedShortcutsDirectory))
        {
            return;
        }

        state.Folders ??= [];
        if (state.Folders.Count == 0)
        {
            state.Folders.Add(new OrganizerFolderState());
        }

        var referenced = state.Folders
            .SelectMany(folder => folder.Shortcuts ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.LaunchPath))
            .Select(item => Path.GetFullPath(item.LaunchPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFileSystemEntries(AppPaths.LegacyManagedShortcutsDirectory))
        {
            var fullPath = Path.GetFullPath(path);
            if (referenced.Add(fullPath))
            {
                state.Folders[0].Shortcuts.Add(new ShortcutItem
                {
                    Name = Directory.Exists(fullPath)
                        ? new DirectoryInfo(fullPath).Name
                        : Path.GetFileNameWithoutExtension(fullPath),
                    LaunchPath = fullPath,
                    IsManaged = true,
                });
            }
        }
    }

    private static string? ResolveExistingOriginal(ShortcutItem item)
    {
        if (item.OriginalPath is not { Length: > 0 } original ||
            !AppPaths.IsDirectDesktopItem(original))
        {
            return null;
        }

        return File.Exists(original) || Directory.Exists(original) ? Path.GetFullPath(original) : null;
    }

    private static void UpdateReference(ShortcutItem item, string path)
    {
        item.LaunchPath = Path.GetFullPath(path);
        item.DesktopIdentity = DesktopItemIdentityService.GetIdentity(path);
        item.Name = Directory.Exists(path)
            ? new DirectoryInfo(path).Name
            : Path.GetFileNameWithoutExtension(path);
        item.OriginalPath = null;
        item.IsManaged = false;
        item.WasMovedFromDesktop = false;
    }

    private static void Move(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static string CreateUniquePath(string directory, string? fileName)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "Recovered desktop item" : fileName;
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

public sealed class LegacyDesktopMigrationTransaction
{
    private readonly List<(string Source, string Destination)> _moves = [];
    private readonly List<string> _cleanupPaths = [];
    private readonly List<ItemSnapshot> _snapshots = [];
    private bool _completed;

    internal void Capture(ShortcutItem item) => _snapshots.Add(new ItemSnapshot(
        item,
        item.Name,
        item.LaunchPath,
        item.DesktopIdentity,
        item.OriginalPath,
        item.IsManaged,
        item.WasMovedFromDesktop));

    internal void RecordMove(string source, string destination) => _moves.Add((source, destination));

    internal void RecordCleanup(string path) => _cleanupPaths.Add(path);

    public void Commit()
    {
        foreach (var path in _cleanupPaths)
        {
            try
            {
                if (File.Exists(path) && AppPaths.IsUnderLegacyManagedDirectory(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // State no longer references this legacy duplicate; leaving it is safe.
            }
        }

        _completed = true;
    }

    public void Rollback()
    {
        if (_completed)
        {
            return;
        }

        for (var index = _moves.Count - 1; index >= 0; index--)
        {
            var (source, destination) = _moves[index];
            try
            {
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, source);
                }
                else if (File.Exists(destination))
                {
                    File.Move(destination, source);
                }
            }
            catch
            {
                // The destination is still on Desktop, which is safer than data loss.
            }
        }

        foreach (var snapshot in _snapshots)
        {
            snapshot.Restore();
        }

        _completed = true;
    }

    private sealed record ItemSnapshot(
        ShortcutItem Item,
        string Name,
        string LaunchPath,
        string DesktopIdentity,
        string? OriginalPath,
        bool IsManaged,
        bool WasMovedFromDesktop)
    {
        public void Restore()
        {
            Item.Name = Name;
            Item.LaunchPath = LaunchPath;
            Item.DesktopIdentity = DesktopIdentity;
            Item.OriginalPath = OriginalPath;
            Item.IsManaged = IsManaged;
            Item.WasMovedFromDesktop = WasMovedFromDesktop;
        }
    }
}
