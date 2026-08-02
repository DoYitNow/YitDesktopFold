using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using YitDesktopFold.Native.Models;
using YitDesktopFold.Native.Services;

namespace YitDesktopFold.Native;

public partial class SettingsWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private readonly OrganizerSettingsSessionSnapshot _session;
    private bool _syncingControls;
    private bool _completed;

    public SettingsWindow(OrganizerSettingsSessionSnapshot session)
    {
        InitializeComponent();
        _session = new OrganizerSettingsSessionSnapshot(
            session.Id,
            session.TargetFolderId,
            session.TargetFolderName,
            session.OriginalAppearance.Copy(),
            session.OriginalShowFolderName,
            session.OriginalShowIconNames,
            session.OriginalIconsOnly,
            session.OriginalIconLayoutMode);
        CurrentFolderNameText.Text = _session.TargetFolderName;
        CurrentFolderNameText.ToolTip = _session.TargetFolderName;
        LoadControls(
            _session.OriginalAppearance,
            _session.OriginalShowFolderName,
            _session.OriginalShowIconNames,
            _session.OriginalIconsOnly,
            _session.OriginalIconLayoutMode);
        RefreshHiddenOrganizers();
    }

    private App AppHost => (App)Application.Current;

    public void PositionNear(Window? source, IReadOnlyList<MainWindow> organizerWindows)
    {
        var workArea = source is null
            ? SystemParameters.WorkArea
            : WorkAreaService.GetCurrentWorkArea(source);
        Height = Math.Min(
            610,
            Math.Max(MinHeight, workArea.Height - WorkAreaService.EdgeGap * 2));

        Point ClampCandidate(double left, double top) => new(
            Math.Clamp(
                left,
                workArea.Left + WorkAreaService.EdgeGap,
                Math.Max(workArea.Left + WorkAreaService.EdgeGap, workArea.Right - Width - WorkAreaService.EdgeGap)),
            Math.Clamp(
                top,
                workArea.Top + WorkAreaService.EdgeGap,
                Math.Max(workArea.Top + WorkAreaService.EdgeGap, workArea.Bottom - Height - WorkAreaService.EdgeGap)));

        var candidates = new List<Point>();
        if (source is not null)
        {
            candidates.Add(ClampCandidate(source.Left + source.Width + 12, source.Top));
            candidates.Add(ClampCandidate(source.Left - Width - 12, source.Top));
            candidates.Add(ClampCandidate(source.Left, source.Top + source.Height + 12));
            candidates.Add(ClampCandidate(source.Left, source.Top - Height - 12));
        }

        candidates.Add(ClampCandidate(
            workArea.Left + (workArea.Width - Width) / 2,
            workArea.Top + (workArea.Height - Height) / 2));

        var organizerBounds = organizerWindows
            .Where(window => window.IsVisible)
            .Select(window => new Rect(window.Left, window.Top, window.Width, window.Height))
            .ToArray();
        var selected = candidates
            .Select((point, index) => new
            {
                Point = point,
                Index = index,
                Overlap = organizerBounds.Sum(bounds =>
                {
                    var intersection = Rect.Intersect(new Rect(point.X, point.Y, Width, Height), bounds);
                    return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
                }),
            })
            .OrderBy(candidate => candidate.Overlap)
            .ThenBy(candidate => candidate.Index)
            .First();
        Left = selected.Point.X;
        Top = selected.Point.Y;
    }

    public void BringForward()
    {
        RefreshHiddenOrganizers();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Focus();
    }

    public void RefreshHiddenOrganizers()
    {
        if (_completed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(RefreshHiddenOrganizers);
            return;
        }

        var hiddenOrganizers = AppHost.GetHiddenOrganizerSnapshots();
        HiddenOrganizerList.Children.Clear();
        HiddenOrganizerCountText.Text = hiddenOrganizers.Count == 0
            ? "暂无"
            : $"{hiddenOrganizers.Count} 个";

        if (hiddenOrganizers.Count == 0)
        {
            HiddenOrganizerList.Children.Add(new TextBlock
            {
                Height = 28,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x98, 0x9C)),
                FontSize = 10.5,
                Text = "暂无隐藏整理块",
            });
            return;
        }

        foreach (var organizer in hiddenOrganizers)
        {
            var row = new Grid
            {
                Height = 32,
                Margin = new Thickness(2, 0, 2, 0),
            };
            var name = new TextBlock
            {
                Margin = new Thickness(6, 0, 66, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCC, 0xCE)),
                FontSize = 11.5,
                Text = organizer.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = organizer.Name,
            };
            var restoreButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("HiddenRestoreButtonStyle"),
                Content = "显示",
                Tag = organizer.Id,
            };
            AutomationProperties.SetName(restoreButton, $"显示隐藏的整理块 {organizer.Name}");
            restoreButton.Click += RestoreHiddenOrganizer_Click;
            row.Children.Add(name);
            row.Children.Add(restoreButton);
            HiddenOrganizerList.Children.Add(row);
        }
    }

    public void CancelAndClose()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        AppHost.CancelSettingsPreview(_session.Id);
        Close();
    }

    private void LoadControls(
        OrganizerAppearanceState appearance,
        bool? showFolderName = null,
        bool? showIconNames = null,
        bool? iconsOnly = null,
        OrganizerIconLayoutMode? iconLayoutMode = null)
    {
        _syncingControls = true;
        GlassToggle.IsChecked = appearance.GlassEnabled;
        MagnetToggle.IsChecked = appearance.MagneticSnapEnabled;
        BackgroundTransparencySlider.Value = Math.Clamp(
            Math.Round((1 - appearance.BackgroundOpacity) * 100),
            BackgroundTransparencySlider.Minimum,
            BackgroundTransparencySlider.Maximum);
        DarkToneOption.IsChecked = appearance.BackgroundTone is OrganizerBackgroundTone.Dark;
        LightToneOption.IsChecked = appearance.BackgroundTone is OrganizerBackgroundTone.Light;
        if (showFolderName.HasValue)
        {
            ShowFolderNameToggle.IsChecked = showFolderName.Value;
        }

        if (showIconNames.HasValue)
        {
            ShowIconNamesToggle.IsChecked = showIconNames.Value;
        }

        if (iconsOnly.HasValue)
        {
            IconsOnlyToggle.IsChecked = iconsOnly.Value;
        }

        if (iconLayoutMode.HasValue)
        {
            LargeLayoutOption.IsChecked = iconLayoutMode.Value is OrganizerIconLayoutMode.Large;
            MediumLayoutOption.IsChecked = iconLayoutMode.Value is OrganizerIconLayoutMode.Medium;
            SmallLayoutOption.IsChecked = iconLayoutMode.Value is OrganizerIconLayoutMode.Small;
            ListLayoutOption.IsChecked = iconLayoutMode.Value is OrganizerIconLayoutMode.List;
        }

        SyncTextFromSliders();
        UpdateTogglePresentation();
        UpdateIconLayoutPresentation();
        InlineStatus.Text = string.Empty;
        _syncingControls = false;
    }

    private OrganizerSettingsDraft CreateDraft() => new()
    {
        Appearance = new OrganizerAppearanceState
        {
            GlassEnabled = GlassToggle.IsChecked == true,
            BackgroundOpacity = Math.Clamp(
                1 - BackgroundTransparencySlider.Value / 100d,
                0,
                1),
            BackgroundTone = LightToneOption.IsChecked == true
                ? OrganizerBackgroundTone.Light
                : OrganizerBackgroundTone.Dark,
            MagneticSnapEnabled = MagnetToggle.IsChecked == true,
        },
        ShowFolderName = ShowFolderNameToggle.IsChecked == true,
        ShowIconNames = ShowIconNamesToggle.IsChecked == true,
        IconsOnly = IconsOnlyToggle.IsChecked == true,
        IconLayoutMode = GetSelectedIconLayoutMode(),
    };

    private void PreviewDraft()
    {
        if (_syncingControls || _completed)
        {
            return;
        }

        InlineStatus.Text = string.Empty;
        if (!AppHost.PreviewSettings(_session.Id, CreateDraft()))
        {
            InlineStatus.Text = "当前整理块已不可用，请重新打开设置。";
        }
    }

    private void UpdateTogglePresentation()
    {
        var glassEnabled = GlassToggle.IsChecked == true;
        GlassStateText.Text = glassEnabled ? "已开启" : "已关闭";
        MagnetStateText.Text = MagnetToggle.IsChecked == true ? "已开启" : "已关闭";
    }

    private OrganizerIconLayoutMode GetSelectedIconLayoutMode()
    {
        if (LargeLayoutOption.IsChecked == true)
        {
            return OrganizerIconLayoutMode.Large;
        }

        if (SmallLayoutOption.IsChecked == true)
        {
            return OrganizerIconLayoutMode.Small;
        }

        if (ListLayoutOption.IsChecked == true)
        {
            return OrganizerIconLayoutMode.List;
        }

        return OrganizerIconLayoutMode.Medium;
    }

    private void UpdateIconLayoutPresentation()
    {
        var iconsOnly = IconsOnlyToggle.IsChecked == true;
        var listMode = GetSelectedIconLayoutMode() is OrganizerIconLayoutMode.List;
        ShowFolderNameToggle.IsEnabled = !iconsOnly;
        ShowFolderNameToggle.Opacity = iconsOnly ? 0.34 : 1;
        FolderNameModeHint.Text = iconsOnly ? "纯图标模式隐藏" : string.Empty;
        ShowIconNamesToggle.IsEnabled = !iconsOnly && !listMode;
        ShowIconNamesToggle.Opacity = iconsOnly || listMode ? 0.34 : 1;
        IconNamesModeHint.Text = iconsOnly
            ? "纯图标模式隐藏"
            : listMode
                ? "列表固定显示"
                : string.Empty;
    }

    private void SyncTextFromSliders()
    {
        BackgroundTransparencyTextBox.Text = Math.Round(BackgroundTransparencySlider.Value)
            .ToString(CultureInfo.InvariantCulture);
    }

    private void SetSliderValue(Slider slider, TextBox textBox, double value)
    {
        _syncingControls = true;
        slider.Value = Math.Clamp(Math.Round(value), slider.Minimum, slider.Maximum);
        textBox.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture);
        textBox.Background = Brushes.Transparent;
        _syncingControls = false;
        PreviewDraft();
    }

    private static bool TryReadValue(TextBox textBox, Slider slider, out double value)
    {
        if (double.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            value = Math.Clamp(value, slider.Minimum, slider.Maximum);
            return true;
        }

        value = slider.Value;
        return false;
    }

    private void NormalizeTextBox(TextBox textBox)
    {
        _ = TryReadValue(textBox, BackgroundTransparencySlider, out var value);
        SetSliderValue(BackgroundTransparencySlider, textBox, value);
    }

    private void AdjustValue(TextBox textBox, int direction, bool coarse)
    {
        var step = coarse ? 5 : 1;
        SetSliderValue(
            BackgroundTransparencySlider,
            textBox,
            BackgroundTransparencySlider.Value + direction * step);
        textBox.Focus();
        textBox.SelectAll();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var darkMode = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, Marshal.SizeOf<int>());
        var smallRoundedCorner = 3;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref smallRoundedCorner, Marshal.SizeOf<int>());
        var noSystemBorder = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noSystemBorder, Marshal.SizeOf<int>());
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshHiddenOrganizers();
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        Opacity = 1;
        WindowScale.ScaleX = 1;
        WindowScale.ScaleY = 1;
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            {
                FillBehavior = FillBehavior.Stop,
            });
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        WindowScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        WindowScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingControls)
        {
            return;
        }

        UpdateTogglePresentation();
        PreviewDraft();
    }

    private void BackgroundTone_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingControls || !IsLoaded)
        {
            return;
        }

        PreviewDraft();
    }

    private void DisplayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingControls || !IsLoaded)
        {
            return;
        }

        UpdateIconLayoutPresentation();
        PreviewDraft();
    }

    private void IconLayoutMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingControls || !IsLoaded)
        {
            return;
        }

        UpdateIconLayoutPresentation();
        PreviewDraft();
    }

    private void RestoreHiddenOrganizer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid folderId })
        {
            return;
        }

        if (!AppHost.ShowHiddenOrganizer(folderId))
        {
            InlineStatus.Text = "该整理块已不存在。";
            RefreshHiddenOrganizers();
            return;
        }

        InlineStatus.Text = "已恢复整理块。";
        RefreshHiddenOrganizers();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingControls || !IsLoaded)
        {
            return;
        }

        _syncingControls = true;
        SyncTextFromSliders();
        _syncingControls = false;
        PreviewDraft();
    }

    private void NumericTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingControls || sender is not TextBox textBox)
        {
            return;
        }

        if (!double.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < BackgroundTransparencySlider.Minimum || value > BackgroundTransparencySlider.Maximum)
        {
            textBox.Background = new SolidColorBrush(Color.FromArgb(0x38, 0x65, 0x3F, 0x3A));
            return;
        }

        textBox.Background = Brushes.Transparent;
        _syncingControls = true;
        BackgroundTransparencySlider.Value = value;
        _syncingControls = false;
        PreviewDraft();
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private void NumericTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            NormalizeTextBox(textBox);
            textBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            NormalizeTextBox(textBox);
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            AdjustValue(textBox, e.Key == Key.Up ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void NumericTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            NormalizeTextBox(textBox);
        }
    }

    private void NumericTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocused: true } textBox)
        {
            AdjustValue(textBox, e.Delta > 0 ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var direction))
        {
            return;
        }

        if (parts[0] == "Background")
        {
            AdjustValue(
                BackgroundTransparencyTextBox,
                direction,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadControls(new OrganizerAppearanceState());
        PreviewDraft();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        NormalizeTextBox(BackgroundTransparencyTextBox);
        if (!AppHost.TryCommitSettings(_session.Id, CreateDraft(), out var error))
        {
            InlineStatus.Text = string.IsNullOrWhiteSpace(error) ? "暂时无法保存设置，请重试。" : $"无法保存：{error}";
            return;
        }

        _completed = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelAndClose();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CancelAndClose();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelAndClose();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        AppHost.CancelSettingsPreview(_session.Id);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
