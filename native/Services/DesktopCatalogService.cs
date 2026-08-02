using System.Security;
using System.Threading;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Mirrors the two real Windows Desktop directories into organizer metadata.
/// The service never copies, moves, or deletes a desktop item.
/// </summary>
public sealed class DesktopCatalogService : IDisposable
{
    private readonly object _sync = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounceTimer;
    private bool _disposed;

    public DesktopCatalogService(bool watchForChanges = true)
    {
        if (!watchForChanges)
        {
            return;
        }

        AddWatcher(AppPaths.DesktopDirectory);
        if (!string.Equals(
                AppPaths.DesktopDirectory,
                AppPaths.CommonDesktopDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            AddWatcher(AppPaths.CommonDesktopDirectory);
        }
    }

    public event EventHandler? CatalogChanged;

    public IReadOnlyList<DesktopCatalogEntry> GetSnapshot()
    {
        var entries = new List<DesktopCatalogEntry>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EnumerateDirectory(AppPaths.DesktopDirectory, entries, identities);
        EnumerateDirectory(AppPaths.CommonDesktopDirectory, entries, identities);
        if (AppPaths.UsesRealWindowsDesktop)
        {
            foreach (var entry in DesktopShellItemService.EnumerateVisibleItems())
            {
                if (identities.Add(entry.Identity))
                {
                    entries.Add(entry);
                }
            }
        }
        return entries;
    }

    public bool Reconcile(OrganizerAppState state)
    {
        state.Folders ??= [];
        if (state.Folders.Count == 0)
        {
            state.Folders.Add(new OrganizerFolderState());
        }

        var snapshot = GetSnapshot();
        var byIdentity = snapshot.ToDictionary(entry => entry.Identity, StringComparer.OrdinalIgnoreCase);
        var byPath = snapshot.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var folder in state.Folders)
        {
            folder.Shortcuts ??= [];
            for (var index = folder.Shortcuts.Count - 1; index >= 0; index--)
            {
                var item = folder.Shortcuts[index];
                DesktopCatalogEntry? entry = null;
                if (!string.IsNullOrWhiteSpace(item.DesktopIdentity))
                {
                    byIdentity.TryGetValue(item.DesktopIdentity, out entry);
                }

                if (entry is null && !string.IsNullOrWhiteSpace(item.LaunchPath))
                {
                    byPath.TryGetValue(NormalizePath(item.LaunchPath), out entry);
                }

                if (entry is null ||
                    DesktopShellItemService.IsShellNamespacePath(entry.Path) ||
                    !claimed.Add(entry.Identity))
                {
                    folder.Shortcuts.RemoveAt(index);
                    changed = true;
                    continue;
                }

                changed |= UpdateReference(item, entry);
            }
        }

        return changed;
    }

    public ShortcutItem CreateReference(string path, int accentIndex = 0)
    {
        var fullPath = NormalizePath(path);
        if (!AppPaths.IsDirectDesktopItem(fullPath) || !ShortcutService.IsSupportedPath(fullPath))
        {
            throw new InvalidOperationException("只能整理用户桌面或公共桌面根目录中的项目。");
        }

        var entry = new DesktopCatalogEntry(
            DesktopItemIdentityService.GetIdentity(fullPath),
            fullPath,
            GetDisplayName(fullPath));
        return CreateReference(entry, accentIndex);
    }

    private static ShortcutItem CreateReference(DesktopCatalogEntry entry, int accentIndex) => new()
    {
        Name = entry.DisplayName,
        LaunchPath = entry.Path,
        DesktopIdentity = entry.Identity,
        AccentIndex = accentIndex,
    };

    private static bool UpdateReference(ShortcutItem item, DesktopCatalogEntry entry)
    {
        var changed = false;
        if (!string.Equals(item.DesktopIdentity, entry.Identity, StringComparison.OrdinalIgnoreCase))
        {
            item.DesktopIdentity = entry.Identity;
            changed = true;
        }

        if (!string.Equals(item.LaunchPath, entry.Path, StringComparison.OrdinalIgnoreCase))
        {
            item.LaunchPath = entry.Path;
            changed = true;
        }

        if (!string.Equals(item.Name, entry.DisplayName, StringComparison.Ordinal))
        {
            item.Name = entry.DisplayName;
            changed = true;
        }

        if (item.IsManaged || item.WasMovedFromDesktop || item.OriginalPath is not null)
        {
            item.IsManaged = false;
            item.WasMovedFromDesktop = false;
            item.OriginalPath = null;
            changed = true;
        }

        return changed;
    }

    private static void EnumerateDirectory(
        string directory,
        ICollection<DesktopCatalogEntry> entries,
        ISet<string> identities)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (string.Equals(Path.GetFileName(path), "desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.System) != 0 &&
                        (attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }

                    var identity = DesktopItemIdentityService.GetIdentity(path);
                    if (identities.Add(identity))
                    {
                        entries.Add(new DesktopCatalogEntry(identity, NormalizePath(path), GetDisplayName(path)));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                {
                    // A single transient or protected desktop entry must not stop the catalog.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Public Desktop can be unavailable under restrictive policies.
        }
    }

    private void AddWatcher(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Attributes,
                EnableRaisingEvents = true,
            };
            watcher.Created += Watcher_Changed;
            watcher.Deleted += Watcher_Changed;
            watcher.Changed += Watcher_Changed;
            watcher.Renamed += Watcher_Changed;
            watcher.Error += Watcher_Error;
            _watchers.Add(watcher);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // The explicit refresh path remains available if a watcher is unavailable.
        }
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e) => ScheduleChanged();

    private void Watcher_Error(object sender, ErrorEventArgs e) => ScheduleChanged();

    private void ScheduleChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer ??= new Timer(
                _ => CatalogChanged?.Invoke(this, EventArgs.Empty),
                null,
                Timeout.Infinite,
                Timeout.Infinite);
            _debounceTimer.Change(180, Timeout.Infinite);
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static string GetDisplayName(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).Name
        : Path.GetFileNameWithoutExtension(path);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}

public sealed record DesktopCatalogEntry(string Identity, string Path, string DisplayName);
