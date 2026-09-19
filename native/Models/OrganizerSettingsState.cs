namespace YitDesktopFold.Native.Models;

/// <summary>
/// A settings draft combines the synchronized organizer appearance with the
/// display preferences that belong only to the selected organizer.
/// </summary>
public sealed class OrganizerSettingsDraft
{
    public OrganizerAppearanceState Appearance { get; init; } = new();

    public bool ShowFolderName { get; init; }

    public bool ShowIconNames { get; init; }

    public bool IconsOnly { get; init; }

    public OrganizerIconLayoutMode IconLayoutMode { get; init; } = OrganizerIconLayoutMode.Medium;

    public bool StartWithWindows { get; init; }

    public OrganizerSettingsDraft Copy() => new()
    {
        Appearance = Appearance.Copy(),
        ShowFolderName = ShowFolderName,
        ShowIconNames = ShowIconNames,
        IconsOnly = IconsOnly,
        IconLayoutMode = IconLayoutMode,
        StartWithWindows = StartWithWindows,
    };
}

/// <summary>
/// Runtime-only snapshot for one ownerless settings window. It deliberately
/// stays outside the persisted app state so previews cannot leak into JSON.
/// </summary>
public sealed record OrganizerSettingsSessionSnapshot(
    Guid Id,
    Guid TargetFolderId,
    string TargetFolderName,
    OrganizerAppearanceState OriginalAppearance,
    bool OriginalShowFolderName,
    bool OriginalShowIconNames,
    bool OriginalIconsOnly,
    OrganizerIconLayoutMode OriginalIconLayoutMode,
    bool OriginalStartWithWindows);

/// <summary>
/// Lightweight runtime projection used by SettingsWindow to recover hidden
/// organizers without exposing their Window instances to the view.
/// </summary>
public sealed record HiddenOrganizerSnapshot(Guid Id, string Name, int ItemCount);
