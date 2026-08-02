using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using YitDesktopFold.Native.Models;
using YitDesktopFold.Native.Services;

namespace YitDesktopFold.Native;

public partial class MainWindow : Window
{
    private const string DesktopItemDragFormat = "YitDesktopFold/DesktopItem";
    private const double DefaultWidth = 444;
    private const double DefaultHeight = 340;
    private const double DefaultRightGap = 116;
    private const double DefaultTopGap = 98;

    public static readonly DependencyProperty ShowIconNamesProperty = DependencyProperty.Register(
        nameof(ShowIconNames),
        typeof(bool),
        typeof(MainWindow),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IconLayoutModeProperty = DependencyProperty.Register(
        nameof(IconLayoutMode),
        typeof(OrganizerIconLayoutMode),
        typeof(MainWindow),
        new PropertyMetadata(OrganizerIconLayoutMode.Medium));

    public static readonly DependencyProperty IconsOnlyModeProperty = DependencyProperty.Register(
        nameof(IconsOnlyMode),
        typeof(bool),
        typeof(MainWindow),
        new PropertyMetadata(false));

    private readonly OrganizerFolderState _folderState;
    private readonly ShellIconService _iconService;
    private readonly SolidColorBrush _surfaceBrush = new(Color.FromArgb(0, 0x2D, 0x32, 0x38));
    private OrganizerAppearanceState _currentAppearance = new();
    private Color _surfaceColor = Color.FromArgb(0, 0x2D, 0x32, 0x38);
    private Color _surfaceHoverColor = Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF);
    private DesktopZOrderHost? _desktopHost;
    private int _toastVersion;
    private int _nameAnimationVersion;
    private bool _displayPreviewActive;
    private bool _previewShowName;
    private bool _previewShowIconNames;
    private bool _previewIconsOnly;
    private OrganizerIconLayoutMode _previewIconLayoutMode;
    private bool _suppressPlacementSave;
    private bool _allowPermanentClose;
    private bool _dialogOpen;
    private bool _isOrganizerHidden;
    private bool _isClosed;
    private bool _keyboardMode;
    private DispatcherTimer? _scrollAnimationTimer;
    private Stopwatch? _scrollAnimationClock;
    private double _scrollAnimationStart;
    private double _scrollAnimationTarget;
    private int _boundsAnimationVersion;
    private double _rawResizeWidth;
    private double _rawResizeHeight;
    private Rect[] _resizeSnapTargets = [];
    private Point _shortcutDragStart;
    private ShortcutItem? _shortcutDragCandidate;
    private bool _shortcutDragCancelled;
    private bool _shortcutDragReleased;
    private IDataObject? _cachedDesktopDropData;
    private string[] _cachedDesktopDropPaths = [];
    private bool _suppressNextShortcutClick;
    private bool _synchronizingItems;

    public MainWindow(
        OrganizerFolderState folderState,
        ShellIconService iconService)
    {
        InitializeComponent();

        _folderState = folderState;
        _iconService = iconService;
        OrganizerSurface.Background = _surfaceBrush;
        ShowIconNames = _folderState.ShowIconNames;
        IconsOnlyMode = _folderState.IconsOnly;
        IconLayoutMode = _folderState.IconLayoutMode;
        MinWidth = OrganizerLayoutMetrics.GetMinimumWidth(IconLayoutMode);

        Items = new ObservableCollection<ShortcutItem>(_folderState.Shortcuts);
        Items.CollectionChanged += Items_CollectionChanged;
        DataContext = this;

        Width = Math.Max(MinWidth, _folderState.Width);
        Height = Math.Max(MinHeight, _folderState.Height);
        var defaultWorkArea = SystemParameters.WorkArea;
        Left = _folderState.Left ?? Math.Max(
            defaultWorkArea.Left + WorkAreaService.EdgeGap,
            defaultWorkArea.Right - Width - DefaultRightGap);
        Top = _folderState.Top ?? defaultWorkArea.Top + DefaultTopGap;

        UpdateFolderNamePresentation(animate: false);
        LocationChanged += (_, _) => QueueSave();
        SizeChanged += (_, _) => QueueSave();
        Deactivated += (_, _) => ExitKeyboardMode(restorePreviousForeground: false);
    }

    public ObservableCollection<ShortcutItem> Items { get; }

    public OrganizerFolderState FolderState => _folderState;

    public bool IsOrganizerHidden => _isOrganizerHidden;

    public void SynchronizeItemsFromState()
    {
        _synchronizingItems = true;
        try
        {
            Items.Clear();
            foreach (var item in _folderState.Shortcuts)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _synchronizingItems = false;
        }

        _ = LoadMissingIconsAsync(Items);
    }

    public void CollectMarqueeIntersections(Rect screenBounds, ISet<string> intersections)
    {
        if (!IsVisible || _isOrganizerHidden)
        {
            return;
        }

        foreach (var item in Items)
        {
            if (ShortcutItems.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container ||
                !container.IsVisible || container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                continue;
            }

            var topLeft = container.PointToScreen(new Point(0, 0));
            var bottomRight = container.PointToScreen(new Point(container.ActualWidth, container.ActualHeight));
            var itemBounds = new Rect(topLeft, bottomRight);
            if (screenBounds.IntersectsWith(itemBounds))
            {
                intersections.Add(item.DesktopIdentity);
            }
        }
    }

    private bool EffectiveIconsOnly =>
        _displayPreviewActive ? _previewIconsOnly : _folderState.IconsOnly;

    private bool EffectiveShowName =>
        !EffectiveIconsOnly &&
        (_displayPreviewActive ? _previewShowName : _folderState.ShowName);

    private bool EffectiveShowIconNames =>
        !EffectiveIconsOnly &&
        (_displayPreviewActive ? _previewShowIconNames : _folderState.ShowIconNames);

    private OrganizerIconLayoutMode EffectiveIconLayoutMode =>
        _displayPreviewActive ? _previewIconLayoutMode : _folderState.IconLayoutMode;

    public bool ShowIconNames
    {
        get => (bool)GetValue(ShowIconNamesProperty);
        private set => SetValue(ShowIconNamesProperty, value);
    }

    public OrganizerIconLayoutMode IconLayoutMode
    {
        get => (OrganizerIconLayoutMode)GetValue(IconLayoutModeProperty);
        private set => SetValue(IconLayoutModeProperty, value);
    }

    public bool IconsOnlyMode
    {
        get => (bool)GetValue(IconsOnlyModeProperty);
        private set => SetValue(IconsOnlyModeProperty, value);
    }

    public void ApplyDisplayPreview(
        bool showFolderName,
        bool showIconNames,
        bool iconsOnly,
        OrganizerIconLayoutMode iconLayoutMode,
        bool animate = true)
    {
        _displayPreviewActive = true;
        _previewShowName = showFolderName;
        _previewShowIconNames = showIconNames;
        _previewIconsOnly = iconsOnly;
        _previewIconLayoutMode = iconLayoutMode;
        ShowIconNames = EffectiveShowIconNames;
        IconsOnlyMode = EffectiveIconsOnly;
        IconLayoutMode = EffectiveIconLayoutMode;
        UpdateFolderNamePresentation(animate);
        ApplyAppearance(_currentAppearance, animate);
    }

    public void ClearDisplayPreview(bool animate = false)
    {
        _displayPreviewActive = false;
        ShowIconNames = _folderState.ShowIconNames;
        IconsOnlyMode = _folderState.IconsOnly;
        IconLayoutMode = _folderState.IconLayoutMode;
        UpdateFolderNamePresentation(animate);
        ApplyAppearance(_currentAppearance, animate);
    }

    public void ApplyCommittedDisplayState(
        bool showFolderName,
        bool showIconNames,
        bool iconsOnly,
        OrganizerIconLayoutMode iconLayoutMode,
        bool animate = false)
    {
        _folderState.ShowName = showFolderName;
        _folderState.ShowIconNames = showIconNames;
        _folderState.IconsOnly = iconsOnly;
        _folderState.IconLayoutMode = iconLayoutMode;
        _displayPreviewActive = false;
        ShowIconNames = showIconNames;
        IconsOnlyMode = iconsOnly;
        IconLayoutMode = iconLayoutMode;
        ApplyMinimumWidth(iconLayoutMode);
        UpdateFolderNamePresentation(animate);
        ApplyAppearance(_currentAppearance, animate);
    }

    private void ApplyMinimumWidth(OrganizerIconLayoutMode mode)
    {
        MinWidth = OrganizerLayoutMetrics.GetMinimumWidth(mode);
        if (Width < MinWidth)
        {
            Width = MinWidth;
        }
    }

    public void RestoreWidthAfterFailedSettingsCommit(double width) =>
        Width = Math.Max(MinWidth, width);

    private App AppHost => (App)Application.Current;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _desktopHost = new DesktopZOrderHost(this);
        _desktopHost.WorkAreaChanged += DesktopHost_WorkAreaChanged;
        _desktopHost.Attach();
        ApplyAppearance(_currentAppearance, animate: false);
    }

    private void DesktopHost_WorkAreaChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(EnsureVisible, DispatcherPriority.Background);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureVisible();
        PlayEntranceAnimation();
        await LoadMissingIconsAsync(Items);
    }

    private void PlayEntranceAnimation()
    {
        BeginAnimation(OpacityProperty, null);
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }

        Opacity = 1;
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void EnsureVisible()
    {
        _suppressPlacementSave = true;
        try
        {
            WorkAreaService.ClampToWorkArea(this);
        }
        finally
        {
            _suppressPlacementSave = false;
        }

        QueueSave();
        _desktopHost?.RefreshZOrder();
        UpdateContentForeground(
            Math.Clamp(_currentAppearance.BackgroundOpacity, 0, 1),
            _currentAppearance.BackgroundTone is OrganizerBackgroundTone.Light);
    }

    private async Task LoadMissingIconsAsync(IEnumerable<ShortcutItem> items)
    {
        foreach (var item in items.Where(item => item.Icon is null).ToArray())
        {
            var icon = await Task.Run(() => _iconService.GetIcon(item.LaunchPath));
            if (_isClosed)
            {
                return;
            }

            item.Icon = icon;
        }
    }

    private async void OrganizerSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        AppHost.MarkActive(this);
        if (_dialogOpen)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        _boundsAnimationVersion++;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        await ApplyMagneticSnapAsync();
        EnsureVisible();
        TrySaveState(showError: false);
    }

    private async Task ApplyMagneticSnapAsync()
    {
        if (!_currentAppearance.MagneticSnapEnabled)
        {
            return;
        }

        var movingBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        var nearbyBounds = AppHost.OrganizerWindows
            .Where(window => !ReferenceEquals(window, this) && window.IsVisible)
            .Select(window => new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight))
            .ToArray();
        if (nearbyBounds.Length == 0)
        {
            return;
        }

        var result = MagneticSnapService.Snap(movingBounds, nearbyBounds);
        if (!result.SnappedX && !result.SnappedY)
        {
            return;
        }

        await AnimateWindowBoundsAsync(result.Bounds);
    }

    private async Task AnimateWindowBoundsAsync(Rect target)
    {
        var animationVersion = ++_boundsAnimationVersion;
        target.Width = Math.Max(MinWidth, target.Width);
        target.Height = Math.Max(MinHeight, target.Height);
        var start = new Rect(Left, Top, ActualWidth, ActualHeight);
        if (!SystemParameters.ClientAreaAnimation ||
            (Math.Abs(start.X - target.X) < 0.5 &&
             Math.Abs(start.Y - target.Y) < 0.5 &&
             Math.Abs(start.Width - target.Width) < 0.5 &&
             Math.Abs(start.Height - target.Height) < 0.5))
        {
            Left = target.X;
            Top = target.Y;
            Width = target.Width;
            Height = target.Height;
            return;
        }

        var completion = new TaskCompletionSource<bool>();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        timer.Tick += (_, _) =>
        {
            if (animationVersion != _boundsAnimationVersion || _isClosed)
            {
                timer.Stop();
                completion.TrySetResult(false);
                return;
            }

            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / 115d, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            Left = start.X + (target.X - start.X) * eased;
            Top = start.Y + (target.Y - start.Y) * eased;
            Width = start.Width + (target.Width - start.Width) * eased;
            Height = start.Height + (target.Height - start.Height) * eased;
            if (progress < 1)
            {
                return;
            }

            timer.Stop();
            completion.TrySetResult(true);
        };
        timer.Start();
        await completion.Task;
    }

    private static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase or Thumb or TextBoxBase or ScrollBar)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void ShortcutScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_dialogOpen || ShortcutScroller.ScrollableHeight <= 0.5)
        {
            return;
        }

        var baseline = _scrollAnimationTimer?.IsEnabled == true
            ? _scrollAnimationTarget
            : ShortcutScroller.VerticalOffset;
        var target = Math.Clamp(
            baseline - e.Delta * 0.55,
            0,
            ShortcutScroller.ScrollableHeight);
        if (Math.Abs(target - ShortcutScroller.VerticalOffset) <= 0.5)
        {
            e.Handled = true;
            return;
        }

        AnimateShortcutScrollTo(target);
        e.Handled = true;
    }

    private void AnimateShortcutScrollTo(double target)
    {
        target = Math.Clamp(target, 0, ShortcutScroller.ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation)
        {
            ShortcutScroller.ScrollToVerticalOffset(target);
            return;
        }

        _scrollAnimationStart = ShortcutScroller.VerticalOffset;
        _scrollAnimationTarget = target;
        _scrollAnimationClock = Stopwatch.StartNew();
        _scrollAnimationTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _scrollAnimationTimer.Tick -= ScrollAnimationTimer_Tick;
        _scrollAnimationTimer.Tick += ScrollAnimationTimer_Tick;
        _scrollAnimationTimer.Start();
    }

    private void ScrollAnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_scrollAnimationClock is null || _scrollAnimationTimer is null)
        {
            return;
        }

        var progress = Math.Clamp(_scrollAnimationClock.Elapsed.TotalMilliseconds / 130d, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        ShortcutScroller.ScrollToVerticalOffset(
            _scrollAnimationStart + (_scrollAnimationTarget - _scrollAnimationStart) * eased);
        if (progress < 1)
        {
            return;
        }

        _scrollAnimationTimer.Stop();
        ShortcutScroller.ScrollToVerticalOffset(_scrollAnimationTarget);
    }

    private void OrganizerSurface_DragOver(object sender, DragEventArgs e)
    {
        if (_dialogOpen)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DesktopItemDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var paths = GetDesktopDropPaths(e.Data);
        e.Effects = paths.Any(ShortcutService.IsDesktopItem)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OrganizerSurface_Drop(object sender, DragEventArgs e)
    {
        if (_dialogOpen)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        if (e.Data.GetDataPresent(DesktopItemDragFormat) &&
            e.Data.GetData(DesktopItemDragFormat) is string payload &&
            TryParseDesktopDragPayload(payload, out var sourceFolderId, out var identity))
        {
            var beforeIdentity = FindShortcutItem(e.OriginalSource as DependencyObject)?.DesktopIdentity;
            AppHost.MoveDesktopItem(sourceFolderId, _folderState.Id, identity, beforeIdentity);
            return;
        }

        var paths = GetDesktopDropPaths(e.Data);
        _cachedDesktopDropData = null;
        _cachedDesktopDropPaths = [];
        await ImportPathsAsync(paths);
    }

    public async void OpenShortcutPicker()
    {
        AppHost.MarkActive(this);
        var dialog = new OpenFileDialog
        {
            Title = "归类桌面项目",
            Filter = "桌面文件|*.*",
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = AppPaths.DesktopDirectory,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ImportPathsAsync(dialog.FileNames);
        }

        _desktopHost?.RefreshZOrder();
    }

    private async Task ImportPathsAsync(IEnumerable<string> sourcePaths)
    {
        var paths = sourcePaths
            .Where(ShortcutService.IsDesktopItem)
            .Select(path => DesktopShellItemService.IsShellNamespacePath(path)
                ? path
                : Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            await ShowAlertAsync(
                "无法加入这个项目",
                "整理块只分类用户桌面或公共桌面根目录中的真实项目，不会复制或移动其他位置的文件。");
            return;
        }

        var assigned = AppHost.AssignDesktopItems(_folderState.Id, paths);
        ShowToast($"已归类 {assigned} 个桌面项目");
    }

    private string[] GetDesktopDropPaths(IDataObject data)
    {
        if (ReferenceEquals(_cachedDesktopDropData, data))
        {
            return _cachedDesktopDropPaths;
        }

        _cachedDesktopDropData = data;
        _cachedDesktopDropPaths = DesktopShellItemService
            .ExtractDesktopDropPaths(data)
            .ToArray();
        return _cachedDesktopDropPaths;
    }

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressNextShortcutClick)
        {
            _suppressNextShortcutClick = false;
            return;
        }

        if (sender is not FrameworkElement { DataContext: ShortcutItem item })
        {
            return;
        }

        AppHost.MarkActive(this);
        try
        {
            ShortcutService.Launch(item);
            ExitKeyboardMode(restorePreviousForeground: false);
            ShowToast($"正在启动 · {item.Name}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            ShowToast($"无法启动 · {item.Name}");
            LiveStatus.Text = exception.Message;
        }
    }

    private void RemoveShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutItem item })
        {
            return;
        }

        var restored = AppHost.MoveDesktopItemsToDesktop(_folderState.Id, item.DesktopIdentity);
        if (restored > 0)
        {
            ShowToast(restored == 1 ? "已移到桌面" : $"已将 {restored} 个项目移到桌面");
        }
        else
        {
            ShowToast("暂时无法恢复桌面显示");
        }
    }

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_dialogOpen)
        {
            return;
        }

        _boundsAnimationVersion++;
        _rawResizeWidth = ActualWidth;
        _rawResizeHeight = ActualHeight;
        _resizeSnapTargets = _currentAppearance.MagneticSnapEnabled
            ? AppHost.OrganizerWindows
                .Where(window => !ReferenceEquals(window, this) && window.IsVisible)
                .Select(window => new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight))
                .ToArray()
            : [];
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_dialogOpen)
        {
            return;
        }

        var workArea = WorkAreaService.GetCurrentWorkArea(this);
        var maximumWidth = Math.Max(MinWidth, workArea.Right - Left - WorkAreaService.EdgeGap);
        var maximumHeight = Math.Max(MinHeight, workArea.Bottom - Top - WorkAreaService.EdgeGap);
        _rawResizeWidth = Math.Clamp(_rawResizeWidth + e.HorizontalChange, MinWidth, maximumWidth);
        _rawResizeHeight = Math.Clamp(_rawResizeHeight + e.VerticalChange, MinHeight, maximumHeight);

        Width = _rawResizeWidth;
        Height = _rawResizeHeight;
    }

    private async void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_dialogOpen)
        {
            return;
        }

        var snapTargets = _resizeSnapTargets;
        _resizeSnapTargets = [];
        if (!e.Canceled && _currentAppearance.MagneticSnapEnabled && snapTargets.Length > 0)
        {
            var workArea = WorkAreaService.GetCurrentWorkArea(this);
            var maximumWidth = Math.Max(MinWidth, workArea.Right - Left - WorkAreaService.EdgeGap);
            var maximumHeight = Math.Max(MinHeight, workArea.Bottom - Top - WorkAreaService.EdgeGap);
            var sizeSnap = MagneticSnapService.SnapSizeToNearbyNeighbor(
                new Rect(Left, Top, ActualWidth, ActualHeight),
                snapTargets);
            var targetWidth = sizeSnap.SnappedWidth &&
                              sizeSnap.Size.Width >= MinWidth &&
                              sizeSnap.Size.Width <= maximumWidth
                ? sizeSnap.Size.Width
                : ActualWidth;
            var targetHeight = sizeSnap.SnappedHeight &&
                               sizeSnap.Size.Height >= MinHeight &&
                               sizeSnap.Size.Height <= maximumHeight
                ? sizeSnap.Size.Height
                : ActualHeight;
            if (Math.Abs(targetWidth - ActualWidth) >= 0.5 ||
                Math.Abs(targetHeight - ActualHeight) >= 0.5)
            {
                await AnimateWindowBoundsAsync(new Rect(
                    Left,
                    Top,
                    targetWidth,
                    targetHeight));
            }
        }

        EnsureVisible();
        TrySaveState(showError: false);
        LiveStatus.Text = $"整理块尺寸：宽 {Math.Round(Width)}，高 {Math.Round(Height)}";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_dialogOpen)
        {
            return;
        }

        if (_keyboardMode && e.Key == Key.Escape)
        {
            ExitKeyboardMode(restorePreviousForeground: true);
            e.Handled = true;
            return;
        }

        var direction = e.Key switch
        {
            Key.Left => new Vector(-1, 0),
            Key.Right => new Vector(1, 0),
            Key.Up => new Vector(0, -1),
            Key.Down => new Vector(0, 1),
            _ => default,
        };
        if (direction == default)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            MoveWithKeyboard(direction);
            e.Handled = true;
            return;
        }

        if (ReferenceEquals(e.OriginalSource, ResizeThumb) || ResizeThumb.IsKeyboardFocused)
        {
            ResizeWithKeyboard(direction);
            e.Handled = true;
        }
    }

    private void MoveWithKeyboard(Vector direction)
    {
        var workArea = WorkAreaService.GetCurrentWorkArea(this);
        Left = Math.Clamp(
            Left + direction.X * 8,
            workArea.Left + WorkAreaService.EdgeGap,
            Math.Max(workArea.Left + WorkAreaService.EdgeGap, workArea.Right - Width - WorkAreaService.EdgeGap));
        Top = Math.Clamp(
            Top + direction.Y * 8,
            workArea.Top + WorkAreaService.EdgeGap,
            Math.Max(workArea.Top + WorkAreaService.EdgeGap, workArea.Bottom - Height - WorkAreaService.EdgeGap));
        LiveStatus.Text = $"整理块位置：横向 {Math.Round(Left)}，纵向 {Math.Round(Top)}";
        QueueSave();
    }

    public void EnterKeyboardMode()
    {
        if (_dialogOpen)
        {
            return;
        }

        if (_keyboardMode)
        {
            ExitKeyboardMode(restorePreviousForeground: true);
            return;
        }

        ShowOrganizer(activate: false);
        AppHost.MarkActive(this);
        _keyboardMode = true;
        _desktopHost?.SetInteractionActivation(true);
        Activate();
        Dispatcher.BeginInvoke(() =>
        {
            if (!_keyboardMode || _isClosed)
            {
                return;
            }

            if (ShortcutItems.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject container &&
                FindVisualChild<Button>(container) is { } firstShortcut)
            {
                firstShortcut.Focus();
            }
            else
            {
                ResizeThumb.Focus();
            }

            LiveStatus.Text = "键盘操作模式已开启；Tab 选择，Enter 启动，Alt+方向键移动，Escape 退出。";
        }, DispatcherPriority.Input);
    }

    private void ExitKeyboardMode(bool restorePreviousForeground)
    {
        if (!_keyboardMode)
        {
            return;
        }

        _keyboardMode = false;
        _desktopHost?.SetInteractionActivation(false, restorePreviousForeground);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ResizeWithKeyboard(Vector direction)
    {
        var workArea = WorkAreaService.GetCurrentWorkArea(this);
        var maximumWidth = Math.Max(MinWidth, workArea.Right - Left - WorkAreaService.EdgeGap);
        var maximumHeight = Math.Max(MinHeight, workArea.Bottom - Top - WorkAreaService.EdgeGap);
        Width = Math.Clamp(Width + direction.X * 16, MinWidth, maximumWidth);
        Height = Math.Clamp(Height + direction.Y * 16, MinHeight, maximumHeight);
        LiveStatus.Text = $"整理块尺寸：宽 {Math.Round(Width)}，高 {Math.Round(Height)}";
        QueueSave();
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_synchronizingItems)
        {
            return;
        }

        _folderState.Shortcuts = Items.ToList();
        QueueSave();
    }

    private void QueueSave()
    {
        if (_suppressPlacementSave || !IsLoaded)
        {
            return;
        }

        AppHost.QueueSave();
    }

    public void CaptureState()
    {
        _folderState.Left = Left;
        _folderState.Top = Top;
        _folderState.Width = Width;
        _folderState.Height = Height;
        _folderState.Shortcuts = Items.ToList();
    }

    private bool TrySaveState(bool showError) => TrySaveState(showError, out _);

    private bool TrySaveState(bool showError, out string? error)
    {
        CaptureState();
        var saved = AppHost.TrySaveAll(out error);
        if (!saved && showError)
        {
            ShowSaveFailure(error);
        }

        return saved;
    }

    public void ShowSaveFailure(string? error)
    {
        ShowToast("暂时无法保存设置");
        LiveStatus.Text = error ?? "无法写入状态文件。";
    }

    private async void ShowToast(string message)
    {
        var version = ++_toastVersion;
        ToastText.Text = message;
        LiveStatus.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        var animationDuration = SystemParameters.ClientAreaAnimation
            ? TimeSpan.FromMilliseconds(120)
            : TimeSpan.Zero;
        ToastHost.Opacity = 1;
        ToastTranslate.Y = 0;
        ToastHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(animationDuration))
            {
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);
        ToastTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, new Duration(animationDuration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);

        await Task.Delay(1550);
        if (version != _toastVersion || _isClosed)
        {
            return;
        }

        var fadeDuration = SystemParameters.ClientAreaAnimation
            ? TimeSpan.FromMilliseconds(140)
            : TimeSpan.Zero;
        ToastHost.Opacity = 0;
        ToastHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, 0, new Duration(fadeDuration))
            {
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);
        await Task.Delay(fadeDuration);
        if (version == _toastVersion && !_isClosed)
        {
            ToastHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OrganizerSurface_MouseEnter(object sender, MouseEventArgs e) =>
        AnimateSurface(_surfaceHoverColor);

    private void OrganizerSurface_MouseLeave(object sender, MouseEventArgs e) =>
        AnimateSurface(_surfaceColor);

    private void AnimateSurface(Color background) => AnimateBrush(_surfaceBrush, background, 170);

    public void ApplyAppearance(OrganizerAppearanceState appearance, bool animate = true)
    {
        _currentAppearance = appearance.Copy();
        var opacity = Math.Clamp(_currentAppearance.BackgroundOpacity, 0, 1);
        var useLightBackground = _currentAppearance.BackgroundTone is OrganizerBackgroundTone.Light;
        var iconsOnly = EffectiveIconsOnly;
        var usesLightGlassSurface = !iconsOnly && useLightBackground && _currentAppearance.GlassEnabled;
        var lightSurfaceAlpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        // Light Acrylic adds a compositor luminosity layer that reads as a
        // solid white board. For light glass, native BlurBehind owns only the
        // blur and this thin WPF wash owns the requested white tint/opacity.
        // Other modes keep their tint entirely in the native backdrop.
        _surfaceColor = iconsOnly
            ? Color.FromArgb(0, 0, 0, 0)
            : usesLightGlassSurface
            ? Color.FromArgb(lightSurfaceAlpha, 0xF6, 0xF7, 0xF5)
            : Color.FromArgb(0, 0, 0, 0);
        _surfaceHoverColor = iconsOnly
            ? Color.FromArgb(0, 0, 0, 0)
            : usesLightGlassSurface
            ? Color.FromArgb(
                (byte)Math.Clamp(lightSurfaceAlpha + 8, 0, 255),
                0xEC,
                0xEE,
                0xEC)
            : useLightBackground
                ? Color.FromArgb(0x0C, 0, 0, 0)
                : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF);
        UpdateContentForeground(iconsOnly ? 0 : opacity, useLightBackground);
        var target = OrganizerSurface.IsMouseOver ? _surfaceHoverColor : _surfaceColor;
        if (animate)
        {
            AnimateSurface(target);
        }
        else
        {
            _surfaceBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _surfaceBrush.Color = target;
        }

        _desktopHost?.UpdateBackdrop(
            !iconsOnly && _currentAppearance.GlassEnabled,
            iconsOnly ? 0 : _currentAppearance.BackgroundOpacity,
            useLightBackground,
            hideSurface: iconsOnly);
        AutomationProperties.SetHelpText(
            this,
            $"全局外观：背景透明度 {Math.Round((1 - opacity) * 100)}%，" +
            $"{(useLightBackground ? "白色" : "黑色")}底色，毛玻璃{(_currentAppearance.GlassEnabled ? "已开启" : "已关闭")}，" +
            $"纯图标模式{(iconsOnly ? "已开启" : "已关闭")}。");
    }

    private void UpdateContentForeground(double opacity, bool useLightBackground)
    {
        var color = DesktopContrastService.ResolveForeground(
            this,
            preferLightForeground: !useLightBackground,
            opacity);
        var contentBrush = new SolidColorBrush(color);
        contentBrush.Freeze();
        Foreground = contentBrush;
        FolderNameText.Foreground = contentBrush;
    }

    private static void AnimateBrush(SolidColorBrush brush, Color target, int milliseconds)
    {
        var current = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = target;
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(current, target, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ShortcutButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button button && !_dialogOpen)
        {
            AnimateShortcut(button, 1.025, -2, 135);
        }
    }

    private void ShortcutButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button button)
        {
            AnimateShortcut(button, 1, 0, 150);
        }
    }

    private void ShortcutButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && e.ChangedButton == MouseButton.Left && !_dialogOpen)
        {
            _shortcutDragStart = e.GetPosition(this);
            _shortcutDragCandidate = button.DataContext as ShortcutItem;
            AnimateShortcut(button, 0.965, 0, 60);
        }
    }

    private void ShortcutButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dialogOpen || e.LeftButton != MouseButtonState.Pressed ||
            sender is not Button button || _shortcutDragCandidate is not { } item)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _shortcutDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _shortcutDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _shortcutDragCandidate = null;
        _suppressNextShortcutClick = true;
        _shortcutDragCancelled = false;
        _shortcutDragReleased = false;
        var data = new DataObject();
        data.SetData(
            DesktopItemDragFormat,
            $"{_folderState.Id:N}|{item.DesktopIdentity}",
            autoConvert: false);
        _ = DragDrop.DoDragDrop(button, data, DragDropEffects.Move);

        if (!_shortcutDragCancelled &&
            _shortcutDragReleased &&
            DesktopDropTargetService.TryGetCurrentDesktopPoint(out _))
        {
            var restored = AppHost.MoveDesktopItemsToDesktop(_folderState.Id, item.DesktopIdentity);
            if (restored > 0)
            {
                ShowToast(restored == 1 ? "已移到桌面" : $"已将 {restored} 个项目移到桌面");
            }
        }

        Dispatcher.BeginInvoke(
            () => _suppressNextShortcutClick = false,
            DispatcherPriority.Input);
    }

    private void ShortcutButton_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
        {
            _shortcutDragCancelled = true;
            return;
        }

        if ((e.KeyStates & DragDropKeyStates.LeftMouseButton) == 0)
        {
            _shortcutDragReleased = true;
        }
    }

    private void ShortcutButton_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (e.Effects == DragDropEffects.None &&
            DesktopDropTargetService.TryGetCurrentDesktopPoint(out _))
        {
            e.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.Hand);
            e.Handled = true;
            return;
        }

        e.UseDefaultCursors = true;
    }

    private void ShortcutButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _shortcutDragCandidate = null;
        if (sender is Button button && e.ChangedButton == MouseButton.Left)
        {
            AnimateShortcut(button, button.IsMouseOver ? 1.025 : 1, button.IsMouseOver ? -2 : 0, 95);
        }
    }

    private void ShortcutButton_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _shortcutDragCandidate = null;
        if (sender is Button button)
        {
            AnimateShortcut(button, button.IsMouseOver ? 1.025 : 1, button.IsMouseOver ? -2 : 0, 110);
        }
    }

    private static bool TryParseDesktopDragPayload(
        string payload,
        out Guid sourceFolderId,
        out string identity)
    {
        sourceFolderId = Guid.Empty;
        var separator = payload.IndexOf('|');
        identity = separator >= 0 ? payload[(separator + 1)..] : string.Empty;
        return separator > 0 &&
               Guid.TryParseExact(payload[..separator], "N", out sourceFolderId) &&
               !string.IsNullOrWhiteSpace(identity);
    }

    private static ShortcutItem? FindShortcutItem(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: ShortcutItem item })
            {
                return item;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static void AnimateShortcut(Button button, double scaleValue, double y, int milliseconds)
    {
        button.ApplyTemplate();
        if (button.Template.FindName("TileScale", button) is not ScaleTransform scale ||
            button.Template.FindName("TileTranslate", button) is not TranslateTransform translate)
        {
            return;
        }

        AnimateTransform(scale, ScaleTransform.ScaleXProperty, scaleValue, milliseconds);
        AnimateTransform(scale, ScaleTransform.ScaleYProperty, scaleValue, milliseconds);
        AnimateTransform(translate, TranslateTransform.YProperty, y, milliseconds);
    }

    private static void AnimateTransform(
        Animatable target,
        DependencyProperty property,
        double targetValue,
        int milliseconds)
    {
        var current = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, targetValue);
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        target.BeginAnimation(
            property,
            new DoubleAnimation(current, targetValue, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private async void UpdateFolderNamePresentation(bool animate)
    {
        var version = ++_nameAnimationVersion;
        FolderNameText.Text = _folderState.Name;
        FolderNameText.ToolTip = _folderState.Name;
        AutomationProperties.SetName(FolderNameText, _folderState.Name);
        AutomationProperties.SetName(this, $"{_folderState.Name}整理块");
        Title = $"{_folderState.Name} · Yit Desktop Fold";

        var showName = EffectiveShowName;
        var targetMargin = showName
            ? new Thickness(0, 24, 0, 0)
            : new Thickness(0);
        var currentMargin = ShortcutItems.Margin;
        ShortcutItems.BeginAnimation(MarginProperty, null);
        ShortcutItems.Margin = targetMargin;

        var duration = animate && SystemParameters.ClientAreaAnimation
            ? TimeSpan.FromMilliseconds(140)
            : TimeSpan.Zero;
        if (duration > TimeSpan.Zero)
        {
            ShortcutItems.BeginAnimation(
                MarginProperty,
                new ThicknessAnimation(currentMargin, targetMargin, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        FolderNameText.BeginAnimation(OpacityProperty, null);
        if (showName)
        {
            FolderNameText.Visibility = Visibility.Visible;
            FolderNameText.Opacity = 1;
            if (duration > TimeSpan.Zero)
            {
                FolderNameText.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0, 1, duration)
                    {
                        FillBehavior = FillBehavior.Stop,
                    },
                    HandoffBehavior.SnapshotAndReplace);
            }

            return;
        }

        if (FolderNameText.Visibility != Visibility.Visible || duration == TimeSpan.Zero)
        {
            FolderNameText.Opacity = 0;
            FolderNameText.Visibility = Visibility.Collapsed;
            return;
        }

        var currentOpacity = FolderNameText.Opacity;
        FolderNameText.Opacity = 0;
        FolderNameText.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(currentOpacity, 0, duration)
            {
                FillBehavior = FillBehavior.Stop,
            },
            HandoffBehavior.SnapshotAndReplace);
        await Task.Delay(duration);
        if (version == _nameAnimationVersion && !EffectiveShowName && !_isClosed)
        {
            FolderNameText.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<DialogChoice> ShowChoiceDialogAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        string? cancelText)
    {
        if (_dialogOpen)
        {
            return DialogChoice.Cancel;
        }

        var dialog = new GlobalDialogWindow(
            title,
            message,
            primaryText,
            secondaryText,
            cancelText);
        return MapDialogResult(await ShowGlobalDialogAsync(dialog));
    }

    private async Task<string?> ShowTextPromptAsync(string title, string message, string initialText)
    {
        if (_dialogOpen)
        {
            return null;
        }

        var dialog = new GlobalDialogWindow(
            title,
            message,
            primaryText: "保存",
            secondaryText: null,
            cancelText: "取消",
            usesInput: true,
            initialText: initialText);
        var result = await ShowGlobalDialogAsync(dialog);
        if (result != GlobalDialogResult.Primary)
        {
            return null;
        }

        var sanitized = new string(dialog.InputText.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        if (_dialogOpen)
        {
            ShowToast(title);
            LiveStatus.Text = message;
            return;
        }

        var dialog = new GlobalDialogWindow(title, message, "知道了", null, null);
        await ShowGlobalDialogAsync(dialog);
    }

    private async Task<GlobalDialogResult> ShowGlobalDialogAsync(GlobalDialogWindow dialog)
    {
        _dialogOpen = true;
        OrganizerMenu.IsOpen = false;
        OrganizerMenu.IsEnabled = false;
        OrganizerContent.IsEnabled = false;
        ResizeThumb.Visibility = Visibility.Collapsed;
        OrganizerSurface.AllowDrop = false;
        try
        {
            return await dialog.ShowAsync(this);
        }
        finally
        {
            if (!_isClosed)
            {
                OrganizerContent.IsEnabled = true;
                ResizeThumb.Visibility = Visibility.Visible;
                OrganizerSurface.AllowDrop = true;
                OrganizerMenu.IsEnabled = true;
            }

            _dialogOpen = false;
            _desktopHost?.RefreshZOrder();
        }
    }

    private static DialogChoice MapDialogResult(GlobalDialogResult result) => result switch
    {
        GlobalDialogResult.Primary => DialogChoice.Primary,
        GlobalDialogResult.Secondary => DialogChoice.Secondary,
        _ => DialogChoice.Cancel,
    };

    public void ToggleVisibility()
    {
        if (!_isOrganizerHidden)
        {
            HideOrganizer();
            return;
        }

        ShowOrganizer();
    }

    public void HideOrganizer()
    {
        if (_isOrganizerHidden)
        {
            return;
        }

        _isOrganizerHidden = true;
        if (IsVisible)
        {
            Hide();
        }

        AppHost.NotifyOrganizerVisibilityChanged(this);
    }

    public void ShowOrganizer(bool activate = false)
    {
        var visibilityChanged = _isOrganizerHidden || !IsVisible;
        if (!IsVisible)
        {
            Show();
        }

        _isOrganizerHidden = false;

        if (activate)
        {
            Activate();
            AppHost.MarkActive(this);
        }

        _desktopHost?.RefreshZOrder();
        if (visibilityChanged)
        {
            AppHost.NotifyOrganizerVisibilityChanged(this);
        }
    }

    public void OpenDesktopFolder() => ShortcutService.OpenDesktopDirectory();

    private void NewOrganizer_Click(object sender, RoutedEventArgs e) => AppHost.CreateOrganizer(this);

    private void AssignDesktopItems_Click(object sender, RoutedEventArgs e) => OpenShortcutPicker();

    private void OpenDesktopFolder_Click(object sender, RoutedEventArgs e) => OpenDesktopFolder();

    private void OrganizerMenu_Opened(object sender, RoutedEventArgs e)
    {
        AppHost.MarkActive(this);
        StartWithWindowsItem.IsChecked = StartupService.IsEnabled();
        DeleteOrganizerItem.IsEnabled = AppHost.OrganizerCount > 1;
    }

    private async void RenameOrganizer_Click(object sender, RoutedEventArgs e)
    {
        var name = await ShowTextPromptAsync(
            "重命名整理块",
            "名称只显示在整理块内部，也可以随时隐藏。",
            _folderState.Name);
        if (name is null || string.Equals(name, _folderState.Name, StringComparison.Ordinal))
        {
            return;
        }

        _folderState.Name = name;
        UpdateFolderNamePresentation(animate: true);
        QueueSave();
        ShowToast("名称已更新");
    }

    private void AppearanceSettings_Click(object sender, RoutedEventArgs e) =>
        AppHost.ShowAppearanceSettings(this);

    private async void DeleteOrganizer_Click(object sender, RoutedEventArgs e)
    {
        if (AppHost.OrganizerCount <= 1)
        {
            ShowToast("至少保留一个整理块");
            return;
        }

        var message = Items.Count == 0
            ? $"删除“{_folderState.Name}”？"
            : $"删除“{_folderState.Name}”？其中 {Items.Count} 个项目会恢复到原生桌面，真实文件不会移动。";
        var choice = await ShowChoiceDialogAsync(
            "删除整理块",
            message,
            primaryText: "删除",
            secondaryText: null,
            cancelText: "保留");
        if (choice == DialogChoice.Primary)
        {
            AppHost.RemoveOrganizer(this);
        }
    }

    private async void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupService.SetEnabled(StartWithWindowsItem.IsChecked);
            TrySaveState(showError: false);
            ShowToast(StartWithWindowsItem.IsChecked ? "已启用登录时启动" : "已关闭登录时启动");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or SecurityException)
        {
            StartWithWindowsItem.IsChecked = StartupService.IsEnabled();
            await ShowAlertAsync("无法更改启动设置", exception.Message);
        }
    }

    private void ResetSize_Click(object sender, RoutedEventArgs e)
    {
        Width = DefaultWidth;
        Height = DefaultHeight;
        EnsureVisible();
        TrySaveState(showError: false);
        ShowToast("已恢复默认大小");
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideOrganizer();

    private void Exit_Click(object sender, RoutedEventArgs e) => AppHost.RequestExit();

    public void CloseForRemoval()
    {
        _allowPermanentClose = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (Application.Current is App { IsExiting: false } && !_allowPermanentClose)
        {
            e.Cancel = true;
            HideOrganizer();
            return;
        }

        CaptureState();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _boundsAnimationVersion++;
        _scrollAnimationTimer?.Stop();
        ExitKeyboardMode(restorePreviousForeground: false);
        Items.CollectionChanged -= Items_CollectionChanged;
        if (_desktopHost is not null)
        {
            _desktopHost.WorkAreaChanged -= DesktopHost_WorkAreaChanged;
            _desktopHost.Dispose();
        }
    }

    private enum DialogChoice
    {
        Primary,
        Secondary,
        Cancel,
    }
}
