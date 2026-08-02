using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace YitDesktopFold.Native.Models;

public sealed class ShortcutItem : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string LaunchPath { get; set; } = string.Empty;

    public string? OriginalPath { get; set; }

    public bool IsManaged { get; set; }

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
