using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using YitDesktopFold.Native.Models;
using YitDesktopFold.Native.Services;

namespace YitDesktopFold.Native;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\YitDesktopFold.Native.Instance";
    private const string ShowRequestEventName = @"Local\YitDesktopFold.Native.ShowRequest";
    private readonly StateStore _stateStore = new();
    private readonly ShortcutService _shortcutService = new();
    private readonly ShellIconService _iconService = new();
    private readonly List<MainWindow> _windows = [];
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showRequestEvent;
    private RegisteredWaitHandle? _showRequestRegistration;
    private DispatcherTimer? _saveTimer;
    private NativeTrayIconService? _trayIcon;
    private GlobalHotKeyService? _keyboardHotKey;
    private SettingsWindow? _settingsWindow;
    private DispatcherOperation? _appearancePreviewOperation;
    private OrganizerAppState _state = new();
    private OrganizerAppearanceState? _appearancePreview;
    private OrganizerAppearanceState? _pendingAppearancePreview;
    private OrganizerSettingsSessionSnapshot? _settingsSession;
    private MainWindow? _lastActiveWindow;
    private bool _ownsMutex;

    public bool IsExiting { get; private set; }

    public int OrganizerCount => _windows.Count;

    public OrganizerAppearanceState Appearance => _state.Appearance;

    public IReadOnlyList<MainWindow> OrganizerWindows => _windows;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            try
            {
                using var showRequest = EventWaitHandle.OpenExisting(ShowRequestEventName);
                showRequest.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first instance may still be completing startup; exiting is safe.
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);

        _state = _stateStore.Load();
        if (_state.Folders.Count == 0)
        {
            _state.Folders.Add(new OrganizerFolderState());
        }

        _saveTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (!TrySaveAll(out var error))
            {
                _lastActiveWindow?.ShowSaveFailure(error);
            }
        };

        foreach (var folder in _state.Folders)
        {
            CreateWindow(folder);
        }

        MainWindow = _windows[0];
        _lastActiveWindow = _windows[0];
        _showRequestEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShowRequestEventName);
        _showRequestRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showRequestEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowAllOrganizers),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        CreateTrayIcon();
        _keyboardHotKey = new GlobalHotKeyService();
        _keyboardHotKey.Pressed += KeyboardHotKey_Pressed;
        foreach (var window in _windows)
        {
            window.Show();
        }
    }

    public MainWindow CreateOrganizer(MainWindow? anchor = null)
    {
        anchor ??= GetTargetWindow();
        var folder = new OrganizerFolderState
        {
            Name = GetNextFolderName(),
        };

        if (anchor is not null)
        {
            folder.Width = anchor.Width;
            folder.Height = anchor.Height;
            var workArea = WorkAreaService.GetCurrentWorkArea(anchor);
            var proposedLeft = anchor.Left + 34;
            var proposedTop = anchor.Top + 34;
            folder.Left = proposedLeft + folder.Width + WorkAreaService.EdgeGap <= workArea.Right
                ? proposedLeft
                : Math.Max(workArea.Left + WorkAreaService.EdgeGap, anchor.Left - 34);
            folder.Top = proposedTop + folder.Height + WorkAreaService.EdgeGap <= workArea.Bottom
                ? proposedTop
                : Math.Max(workArea.Top + WorkAreaService.EdgeGap, anchor.Top - 34);
        }

        _state.Folders.Add(folder);
        var window = CreateWindow(folder);
        _lastActiveWindow = window;
        window.Show();
        window.ShowOrganizer(activate: false);
        QueueSave();
        return window;
    }

    public bool RemoveOrganizer(MainWindow window)
    {
        if (_windows.Count <= 1 || !_windows.Contains(window))
        {
            return false;
        }

        if (_settingsSession?.TargetFolderId == window.FolderState.Id)
        {
            _settingsWindow?.CancelAndClose();
            if (_settingsSession is { } lingeringSession)
            {
                CancelSettingsPreview(lingeringSession.Id);
            }
        }

        window.CaptureState();
        _windows.Remove(window);
        _state.Folders.Remove(window.FolderState);
        _iconService.RetainOnly(_state.Folders.SelectMany(folder => folder.Shortcuts).Select(item => item.LaunchPath));
        _lastActiveWindow = _windows.LastOrDefault();
        MainWindow = _windows[0];
        window.CloseForRemoval();
        if (!TrySaveAll(out var error))
        {
            _lastActiveWindow?.ShowSaveFailure(error);
        }

        return true;
    }

    public void MarkActive(MainWindow window)
    {
        if (_windows.Contains(window))
        {
            _lastActiveWindow = window;
        }
    }

    public IReadOnlyList<HiddenOrganizerSnapshot> GetHiddenOrganizerSnapshots() =>
        _windows
            .Where(window => window.IsOrganizerHidden)
            .Select(window => new HiddenOrganizerSnapshot(window.FolderState.Id, window.FolderState.Name))
            .ToArray();

    public bool ShowHiddenOrganizer(Guid folderId)
    {
        var window = FindWindow(folderId);
        if (window is null)
        {
            return false;
        }

        window.ShowOrganizer(activate: false);
        return true;
    }

    public void NotifyOrganizerVisibilityChanged(MainWindow window)
    {
        if (_windows.Contains(window))
        {
            _settingsWindow?.RefreshHiddenOrganizers();
        }
    }

    public bool PreviewSettings(Guid sessionId, OrganizerSettingsDraft draft)
    {
        if (_settingsSession is not { } session || session.Id != sessionId)
        {
            return false;
        }

        var target = FindWindow(session.TargetFolderId);
        if (target is null)
        {
            return false;
        }

        var snapshot = draft.Copy();
        target.ApplyDisplayPreview(
            snapshot.ShowFolderName,
            snapshot.ShowIconNames,
            snapshot.IconsOnly,
            snapshot.IconLayoutMode,
            animate: true);
        _appearancePreview = snapshot.Appearance.Copy();
        _pendingAppearancePreview = _appearancePreview.Copy();
        if (_appearancePreviewOperation is { Status: DispatcherOperationStatus.Pending })
        {
            return true;
        }

        _appearancePreviewOperation = Dispatcher.BeginInvoke(() =>
        {
            var appearance = _pendingAppearancePreview;
            _pendingAppearancePreview = null;
            _appearancePreviewOperation = null;
            if (_settingsSession?.Id == sessionId && appearance is not null)
            {
                ApplyAppearanceToAll(appearance, animate: false);
            }
        }, DispatcherPriority.Render);
        return true;
    }

    public void CancelSettingsPreview(Guid sessionId)
    {
        if (_settingsSession is not { } session || session.Id != sessionId)
        {
            return;
        }

        CancelPendingAppearanceBroadcast();
        _appearancePreview = null;
        ApplyAppearanceToAll(_state.Appearance, animate: false);
        FindWindow(session.TargetFolderId)?.ClearDisplayPreview(animate: false);
        _settingsSession = null;
    }

    public bool TryCommitSettings(Guid sessionId, OrganizerSettingsDraft draft, out string? error)
    {
        if (_settingsSession is not { } session || session.Id != sessionId)
        {
            error = "设置会话已结束，请重新打开设置。";
            return false;
        }

        var target = FindWindow(session.TargetFolderId);
        if (target is null)
        {
            error = "当前整理块已不存在。";
            return false;
        }

        var previousAppearance = _state.Appearance.Copy();
        var previousShowFolderName = target.FolderState.ShowName;
        var previousShowIconNames = target.FolderState.ShowIconNames;
        var previousIconsOnly = target.FolderState.IconsOnly;
        var previousIconLayoutMode = target.FolderState.IconLayoutMode;
        var previousWidth = target.Width;
        var snapshot = draft.Copy();
        CancelPendingAppearanceBroadcast();
        _state.Appearance = snapshot.Appearance.Copy();
        _appearancePreview = null;
        target.ApplyCommittedDisplayState(
            snapshot.ShowFolderName,
            snapshot.ShowIconNames,
            snapshot.IconsOnly,
            snapshot.IconLayoutMode,
            animate: false);
        ApplyAppearanceToAll(_state.Appearance, animate: false);
        if (TrySaveAll(out error))
        {
            _settingsSession = null;
            return true;
        }

        _state.Appearance = previousAppearance;
        target.ApplyCommittedDisplayState(
            previousShowFolderName,
            previousShowIconNames,
            previousIconsOnly,
            previousIconLayoutMode,
            animate: false);
        target.RestoreWidthAfterFailedSettingsCommit(previousWidth);
        _appearancePreview = snapshot.Appearance.Copy();
        ApplyAppearanceToAll(snapshot.Appearance, animate: false);
        target.ApplyDisplayPreview(
            snapshot.ShowFolderName,
            snapshot.ShowIconNames,
            snapshot.IconsOnly,
            snapshot.IconLayoutMode,
            animate: false);
        return false;
    }

    public void ShowAppearanceSettings(MainWindow? source = null)
    {
        source ??= GetSettingsTargetWindow();
        if (source is null)
        {
            return;
        }

        MarkActive(source);

        if (_settingsWindow is not null)
        {
            _settingsWindow.BringForward();
            return;
        }

        var session = new OrganizerSettingsSessionSnapshot(
            Guid.NewGuid(),
            source.FolderState.Id,
            source.FolderState.Name,
            _state.Appearance.Copy(),
            source.FolderState.ShowName,
            source.FolderState.ShowIconNames,
            source.FolderState.IconsOnly,
            source.FolderState.IconLayoutMode);
        _settingsSession = session;
        var settings = new SettingsWindow(session);
        settings.PositionNear(source, _windows);
        settings.Closed += (_, _) =>
        {
            if (_settingsSession?.Id == session.Id)
            {
                CancelSettingsPreview(session.Id);
            }

            if (ReferenceEquals(_settingsWindow, settings))
            {
                _settingsWindow = null;
            }
        };
        _settingsWindow = settings;
        settings.Show();
        settings.Activate();
    }

    private void ApplyAppearanceToAll(OrganizerAppearanceState appearance, bool animate)
    {
        var snapshot = appearance.Copy();
        foreach (var window in _windows)
        {
            window.ApplyAppearance(snapshot, animate);
        }
    }

    private void CancelPendingAppearanceBroadcast()
    {
        if (_appearancePreviewOperation is { Status: DispatcherOperationStatus.Pending } pending)
        {
            pending.Abort();
        }

        _appearancePreviewOperation = null;
        _pendingAppearancePreview = null;
    }

    public void QueueSave()
    {
        if (IsExiting || _saveTimer is null)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public bool TrySaveAll(out string? error)
    {
        _saveTimer?.Stop();
        _iconService.Clear();
        foreach (var window in _windows)
        {
            window.CaptureState();
        }

        try
        {
            _state.StartWithWindows = StartupService.IsEnabled();
            _stateStore.Save(_state);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = exception.Message;
            return false;
        }
    }

    public void ToggleAllOrganizers()
    {
        if (_windows.Any(window => !window.IsOrganizerHidden))
        {
            foreach (var window in _windows)
            {
                window.HideOrganizer();
            }

            return;
        }

        ShowAllOrganizers();
    }

    public void ShowAllOrganizers()
    {
        foreach (var window in _windows)
        {
            window.ShowOrganizer(activate: false);
        }

        _lastActiveWindow?.ShowOrganizer(activate: false);
    }

    private MainWindow CreateWindow(OrganizerFolderState folder)
    {
        var window = new MainWindow(folder, _shortcutService, _iconService);
        window.ApplyAppearance(_appearancePreview ?? _state.Appearance, animate: false);
        window.Activated += (_, _) => MarkActive(window);
        _windows.Add(window);
        return window;
    }

    private MainWindow? GetTargetWindow() =>
        _lastActiveWindow is { IsOrganizerHidden: false }
            ? _lastActiveWindow
            : _windows.FirstOrDefault(window => !window.IsOrganizerHidden) ?? _windows.FirstOrDefault();

    private MainWindow? GetSettingsTargetWindow() =>
        _lastActiveWindow is not null && _windows.Contains(_lastActiveWindow)
            ? _lastActiveWindow
            : _windows.FirstOrDefault();

    private MainWindow? FindWindow(Guid folderId) =>
        _windows.FirstOrDefault(window => window.FolderState.Id == folderId);

    private string GetNextFolderName()
    {
        var suffix = 1;
        while (_state.Folders.Any(folder =>
                   string.Equals(folder.Name, $"文件夹 {suffix}", StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
        }

        return $"文件夹 {suffix}";
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new NativeTrayIconService();
        _trayIcon.CommandInvoked += TrayIcon_CommandInvoked;
        _trayIcon.DoubleClicked += TrayIcon_DoubleClicked;
    }

    private void TrayIcon_CommandInvoked(object? sender, NativeTrayCommand command)
    {
        switch (command)
        {
            case NativeTrayCommand.CreateOrganizer:
                CreateOrganizer();
                break;
            case NativeTrayCommand.ToggleAllOrganizers:
                ToggleAllOrganizers();
                break;
            case NativeTrayCommand.KeyboardMode:
                EnterKeyboardMode();
                break;
            case NativeTrayCommand.AddShortcuts:
                GetTargetWindow()?.OpenShortcutPicker();
                break;
            case NativeTrayCommand.Settings:
                ShowAppearanceSettings();
                break;
            case NativeTrayCommand.OpenManagedDirectory:
                ShortcutService.OpenManagedDirectory();
                break;
            case NativeTrayCommand.Exit:
                RequestExit();
                break;
        }
    }

    private void TrayIcon_DoubleClicked(object? sender, EventArgs e) => ShowAllOrganizers();

    private void KeyboardHotKey_Pressed(object? sender, EventArgs e) => EnterKeyboardMode();

    private void EnterKeyboardMode()
    {
        var target = GetTargetWindow();
        target?.EnterKeyboardMode();
    }

    public void RequestExit()
    {
        if (IsExiting)
        {
            return;
        }

        _settingsWindow?.CancelAndClose();
        if (_settingsSession is { } lingeringSession)
        {
            CancelSettingsPreview(lingeringSession.Id);
        }

        if (!TrySaveAll(out var error))
        {
            GetTargetWindow()?.ShowSaveFailure(error);
            return;
        }

        IsExiting = true;
        foreach (var window in _windows.ToArray())
        {
            window.Close();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _saveTimer?.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.CommandInvoked -= TrayIcon_CommandInvoked;
            _trayIcon.DoubleClicked -= TrayIcon_DoubleClicked;
            _trayIcon.Dispose();
        }

        _showRequestRegistration?.Unregister(null);
        _showRequestEvent?.Dispose();
        if (_keyboardHotKey is not null)
        {
            _keyboardHotKey.Pressed -= KeyboardHotKey_Pressed;
            _keyboardHotKey.Dispose();
        }

        if (_ownsMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
