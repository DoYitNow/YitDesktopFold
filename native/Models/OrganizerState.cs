using System.Text.Json.Serialization;

namespace YitDesktopFold.Native.Models;

public sealed class OrganizerAppState
{
    public int SchemaVersion { get; set; } = 2;

    public bool StartWithWindows { get; set; }

    public OrganizerAppearanceState Appearance { get; set; } = new();

    public List<OrganizerFolderState> Folders { get; set; } = [];
}

public sealed class OrganizerAppearanceState
{
    public bool GlassEnabled { get; set; } = true;

    public double BackgroundOpacity { get; set; } = 0.76;

    public OrganizerBackgroundTone BackgroundTone { get; set; } = OrganizerBackgroundTone.Dark;

    public bool MagneticSnapEnabled { get; set; } = true;

    public OrganizerAppearanceState Copy() => new()
    {
        GlassEnabled = GlassEnabled,
        BackgroundOpacity = BackgroundOpacity,
        BackgroundTone = BackgroundTone,
        MagneticSnapEnabled = MagneticSnapEnabled,
    };
}

public enum OrganizerBackgroundTone
{
    Dark,
    Light,
}

public enum OrganizerIconLayoutMode
{
    Medium,
    Large,
    Small,
    List,
}

public static class OrganizerLayoutMetrics
{
    public static double GetMinimumWidth(OrganizerIconLayoutMode mode) => mode switch
    {
        OrganizerIconLayoutMode.Large => 114,
        OrganizerIconLayoutMode.Small => 76,
        OrganizerIconLayoutMode.List => 76,
        _ => 94,
    };
}

public sealed class OrganizerFolderState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "文件夹 1";

    public bool ShowName { get; set; }

    public bool ShowIconNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IconsOnly { get; set; }

    public OrganizerIconLayoutMode IconLayoutMode { get; set; } = OrganizerIconLayoutMode.Medium;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 444;

    public double Height { get; set; } = 340;

    public List<ShortcutItem> Shortcuts { get; set; } = [];
}
