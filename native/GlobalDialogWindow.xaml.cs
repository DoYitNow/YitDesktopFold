using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using YitDesktopFold.Native.Services;

namespace YitDesktopFold.Native;

internal enum GlobalDialogResult
{
    Primary,
    Secondary,
    Cancel,
}

public partial class GlobalDialogWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    private readonly TaskCompletionSource<GlobalDialogResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _usesInput;
    private readonly GlobalDialogResult _closeResult;
    private Window? _anchor;
    private bool _completing;

    public GlobalDialogWindow(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        string? cancelText,
        bool usesInput = false,
        string initialText = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        SecondaryButton.Content = secondaryText;
        SecondaryButton.Visibility = secondaryText is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = cancelText;
        CancelButton.Visibility = cancelText is null ? Visibility.Collapsed : Visibility.Visible;
        _usesInput = usesInput;
        _closeResult = cancelText is null ? GlobalDialogResult.Primary : GlobalDialogResult.Cancel;
        InputShell.Visibility = usesInput ? Visibility.Visible : Visibility.Collapsed;
        InputTextBox.Text = initialText;
        PrimaryButton.IsEnabled = !usesInput || !string.IsNullOrWhiteSpace(initialText);
    }

    public string InputText => InputTextBox.Text;

    internal Task<GlobalDialogResult> ShowAsync(Window anchor)
    {
        _anchor = anchor;
        Show();
        Activate();
        return _completion.Task;
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
        PositionNearAnchor();
        Opacity = 1;
        WindowScale.ScaleX = 1;
        WindowScale.ScaleY = 1;
        if (SystemParameters.ClientAreaAnimation)
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(125))
                {
                    FillBehavior = FillBehavior.Stop,
                });
            WindowScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(125))
                {
                    EasingFunction = easing,
                    FillBehavior = FillBehavior.Stop,
                });
            WindowScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(125))
                {
                    EasingFunction = easing,
                    FillBehavior = FillBehavior.Stop,
                });
        }

        if (_usesInput)
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }
        else if (CancelButton.Visibility == Visibility.Visible)
        {
            CancelButton.Focus();
        }
        else
        {
            PrimaryButton.Focus();
        }
    }

    private void PositionNearAnchor()
    {
        var anchor = _anchor;
        var workArea = anchor is null ? SystemParameters.WorkArea : WorkAreaService.GetCurrentWorkArea(anchor);
        var anchorLeft = anchor?.Left ?? workArea.Left + workArea.Width / 2;
        var anchorTop = anchor?.Top ?? workArea.Top + workArea.Height / 2;
        var anchorWidth = anchor?.ActualWidth ?? 0;
        var anchorHeight = anchor?.ActualHeight ?? 0;
        Left = Math.Clamp(
            anchorLeft + (anchorWidth - ActualWidth) / 2,
            workArea.Left + WorkAreaService.EdgeGap,
            Math.Max(workArea.Left + WorkAreaService.EdgeGap,
                workArea.Right - ActualWidth - WorkAreaService.EdgeGap));
        Top = Math.Clamp(
            anchorTop + (anchorHeight - ActualHeight) / 2,
            workArea.Top + WorkAreaService.EdgeGap,
            Math.Max(workArea.Top + WorkAreaService.EdgeGap,
                workArea.Bottom - ActualHeight - WorkAreaService.EdgeGap));
    }

    private async void Complete(GlobalDialogResult result)
    {
        if (_completing)
        {
            return;
        }

        if (result is GlobalDialogResult.Primary && _usesInput && string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            ErrorText.Text = "请输入文件夹名称";
            ErrorText.Visibility = Visibility.Visible;
            InputTextBox.Focus();
            return;
        }

        _completing = true;
        PrimaryButton.IsEnabled = false;
        SecondaryButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        if (SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90))
                {
                    FillBehavior = FillBehavior.Stop,
                });
            await Task.Delay(90);
        }

        _completion.TrySetResult(result);
        Close();
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e) => Complete(GlobalDialogResult.Primary);

    private void SecondaryButton_Click(object sender, RoutedEventArgs e) => Complete(GlobalDialogResult.Secondary);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Complete(GlobalDialogResult.Cancel);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Complete(_closeResult);

    private void InputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_usesInput)
        {
            return;
        }

        var valid = !string.IsNullOrWhiteSpace(InputTextBox.Text);
        PrimaryButton.IsEnabled = valid;
        ErrorText.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = valid ? string.Empty : "请输入文件夹名称";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Complete(_closeResult);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            Complete(GlobalDialogResult.Primary);
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_completing)
        {
            _completion.TrySetResult(_closeResult);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
