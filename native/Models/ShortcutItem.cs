using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace YitDesktopFold.Native.Models;

public sealed class ShortcutItem : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private string _name = string.Empty;
    private string _launchPath = string.Empty;
    private string _desktopIdentity = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_name, value, StringComparison.Ordinal))
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public string LaunchPath
    {
        get => _launchPath;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_launchPath, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _launchPath = value;
            Icon = null;
            OnPropertyChanged();
        }
    }

    public string DesktopIdentity
    {
        get => _desktopIdentity;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_desktopIdentity, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _desktopIdentity = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsManaged { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WasMovedFromDesktop { get; set; }

    public int AccentIndex { get; set; }

    [JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            _icon = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
