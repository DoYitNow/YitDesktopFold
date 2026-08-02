using System.Security;
using System.Text.Json;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public OrganizerAppState Load()
    {
        try
        {
            AppPaths.EnsureCreated();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Keep the organizer usable for this session even if policy or a
            // transient profile issue makes its data directory unavailable.
            return new OrganizerAppState
            {
                Folders = [new OrganizerFolderState()],
            };
        }

        var state = TryLoad(AppPaths.StateFile);
        if (state is not null)
        {
            return Normalize(state);
        }

        state = TryLoad(AppPaths.BackupStateFile);
        if (state is not null)
        {
            BackupCorruptState();
            TryRestorePrimaryFromBackup();
            return Normalize(state);
        }

        BackupCorruptState();
        return new OrganizerAppState
        {
            Folders =
            [
                new OrganizerFolderState
                {
                    Shortcuts = RecoverManagedShortcuts(),
                },
            ],
        };
    }

    public void Save(OrganizerAppState state)
    {
        AppPaths.EnsureCreated();
        state.SchemaVersion = 2;
        var temporaryPath = AppPaths.StateFile + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(AppPaths.StateFile))
        {
            File.Copy(AppPaths.StateFile, AppPaths.BackupStateFile, overwrite: true);
        }

        File.Move(temporaryPath, AppPaths.StateFile, overwrite: true);
    }

    private static OrganizerAppState? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            var isMultiFolderState = document.RootElement
                .EnumerateObject()
                .Any(property => string.Equals(property.Name, "Folders", StringComparison.OrdinalIgnoreCase));

            if (isMultiFolderState)
            {
                return JsonSerializer.Deserialize<OrganizerAppState>(json, JsonOptions);
            }

            var legacy = JsonSerializer.Deserialize<LegacyOrganizerState>(json, JsonOptions);
            if (legacy is null)
            {
                return null;
            }

            return new OrganizerAppState
            {
                StartWithWindows = legacy.StartWithWindows,
                Folders =
                [
                    new OrganizerFolderState
                    {
                        Name = "文件夹 1",
                        Left = legacy.Left,
                        Top = legacy.Top,
                        Width = legacy.Width,
                        Height = legacy.Height,
                        Shortcuts = legacy.Shortcuts ?? [],
                    },
                ],
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
    }

    private static OrganizerAppState Normalize(OrganizerAppState state)
    {
        state.SchemaVersion = 2;
        state.Folders ??= [];
        state.Appearance ??= new OrganizerAppearanceState();
        state.Appearance.BackgroundOpacity = Math.Clamp(
            double.IsFinite(state.Appearance.BackgroundOpacity)
                ? state.Appearance.BackgroundOpacity
                : new OrganizerAppearanceState().BackgroundOpacity,
            0,
            1);
        if (!Enum.IsDefined(state.Appearance.BackgroundTone))
        {
            state.Appearance.BackgroundTone = OrganizerBackgroundTone.Dark;
        }
        var folderIds = new HashSet<Guid>();

        foreach (var folder in state.Folders)
        {
            if (folder.Id == Guid.Empty || !folderIds.Add(folder.Id))
            {
                do
                {
                    folder.Id = Guid.NewGuid();
                }
                while (!folderIds.Add(folder.Id));
            }

            folder.Name = string.IsNullOrWhiteSpace(folder.Name) ? "未命名文件夹" : folder.Name.Trim();
            if (!Enum.IsDefined(folder.IconLayoutMode))
            {
                folder.IconLayoutMode = OrganizerIconLayoutMode.Medium;
            }
            folder.Width = Math.Max(
                OrganizerLayoutMetrics.GetMinimumWidth(folder.IconLayoutMode),
                folder.Width);
            folder.Height = Math.Max(120, folder.Height);
            folder.Shortcuts ??= [];
        }

        return state;
    }

    private static void BackupCorruptState()
    {
        if (!File.Exists(AppPaths.StateFile))
        {
            return;
        }

        var corruptPath = Path.Combine(
            AppPaths.RootDirectory,
            $"state.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        try
        {
            File.Copy(AppPaths.StateFile, corruptPath, overwrite: false);
        }
        catch (IOException)
        {
            // Recovery still proceeds from the backup or managed shortcut directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Recovery still proceeds from the backup or managed shortcut directory.
        }
        catch (SecurityException)
        {
            // Recovery still proceeds from the backup or managed shortcut directory.
        }
    }

    private static void TryRestorePrimaryFromBackup()
    {
        try
        {
            File.Copy(AppPaths.BackupStateFile, AppPaths.StateFile, overwrite: true);
        }
        catch (IOException)
        {
            // The in-memory backup is still usable for this session.
        }
        catch (UnauthorizedAccessException)
        {
            // The in-memory backup is still usable for this session.
        }
        catch (SecurityException)
        {
            // The in-memory backup is still usable for this session.
        }
    }

    private static List<ShortcutItem> RecoverManagedShortcuts()
    {
        try
        {
            if (!Directory.Exists(AppPaths.ManagedShortcutsDirectory))
            {
                return [];
            }

            return Directory.EnumerateFiles(AppPaths.ManagedShortcutsDirectory)
                .Where(ShortcutService.IsSupportedPath)
                .Select((path, index) => new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    LaunchPath = path,
                    IsManaged = true,
                    AccentIndex = index,
                })
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }

    private sealed class LegacyOrganizerState
    {
        public double? Left { get; set; }

        public double? Top { get; set; }

        public double Width { get; set; } = 444;

        public double Height { get; set; } = 340;

        public bool StartWithWindows { get; set; }

        public List<ShortcutItem>? Shortcuts { get; set; }
    }
}
