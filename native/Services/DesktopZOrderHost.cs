using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace YitDesktopFold.Native.Services;

public sealed class DesktopZOrderHost : IDisposable
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExNoActivate = 0x08000000L;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsDisabled = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectReorder = 0x8004;
    private const uint GwHwndNext = 2;
    private const uint GwHwndPrev = 3;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int WmMouseActivate = 0x0021;
    private const int WmDpiChanged = 0x02E0;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmCompositionChanged = 0x031E;
    private const int MaNoActivate = 3;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaExcludedFromPeek = 12;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaRedirectionBitmapAlpha = 39;
    private const int DwmncrpEnabled = 2;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRound = 2;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private IntPtr _windowHandle;
    private HwndSource? _source;
    private WindowBackdropService? _backdropService;
    private WindowBackdropOptions _backdropOptions = WindowBackdropOptions.Default;
    private IntPtr _foregroundBeforeInteraction;
    private int _taskbarCreatedMessage;
    private bool _allowInteractionActivation;
    private bool _disposed;

    public DesktopZOrderHost(Window window)
    {
        _window = window;
        _dispatcher = window.Dispatcher;
    }

    public event EventHandler? WorkAreaChanged;

    public void Attach()
    {
        _windowHandle = new WindowInteropHelper(_window).Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowProcedure);
        _backdropService = new WindowBackdropService(_window, _windowHandle, _source);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        ApplyExtendedStyle();
        ApplyDwmAppearance();
        ForegroundCoordinator.Register(this);
        ForegroundCoordinator.RefreshAll();
    }

    public void RefreshZOrder() => ForegroundCoordinator.RefreshAll();

    /// <summary>
    /// Temporarily permits activation for an explicit keyboard interaction, such as
    /// a custom rename prompt. Normal organizer interaction should leave this false.
    /// </summary>
    public void SetInteractionActivation(bool allowActivation, bool restorePreviousForeground = true)
    {
        if (_allowInteractionActivation == allowActivation)
        {
            return;
        }

        if (allowActivation)
        {
            _foregroundBeforeInteraction = GetForegroundWindow();
        }

        _allowInteractionActivation = allowActivation;
        ApplyExtendedStyle();

        if (allowActivation)
        {
            _ = SetWindowPos(
                _windowHandle,
                HwndTop,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }
        else if (restorePreviousForeground)
        {
            RestorePreviousForeground();
        }
        else
        {
            _foregroundBeforeInteraction = IntPtr.Zero;
            ForegroundCoordinator.RefreshAll();
        }
    }

    private void RestorePreviousForeground()
    {
        var previous = _foregroundBeforeInteraction;
        _foregroundBeforeInteraction = IntPtr.Zero;
        if (previous != IntPtr.Zero && previous != _windowHandle && IsWindow(previous))
        {
            _ = SetForegroundWindow(previous);
        }

        ForegroundCoordinator.RefreshAll();
    }

    /// <summary>
    /// Updates the Windows backdrop without changing the opacity of shortcut icons.
    /// The values are retained as one settings contract; the organizer's single
    /// smoke surface renders the tint while DWM owns the actual blur kernel.
    /// </summary>
    public void UpdateBackdrop(
        bool enabled,
        double opacity,
        bool useLightBackground,
        bool hideSurface = false)
    {
        var usePerPixelAlpha = UpdateDwmTransparencyMode(hideSurface);
        _backdropOptions = new WindowBackdropOptions(
            enabled,
            opacity,
            useLightBackground,
            hideSurface,
            usePerPixelAlpha).Normalized();
        _backdropService?.Apply(_backdropOptions);
        // Accent changes can make DWM rebuild the outer frame, so apply the
        // pure-icon non-client policy last.
        ApplyDwmFramePresentation(hideSurface);
    }

    private void ApplyExtendedStyle()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
        extendedStyle |= WsExToolWindow;
        if (_allowInteractionActivation)
        {
            extendedStyle &= ~WsExNoActivate;
        }
        else
        {
            extendedStyle |= WsExNoActivate;
        }

        _ = SetWindowLongPtr(_windowHandle, GwlExStyle, new IntPtr(extendedStyle));
        _ = SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoOwnerZOrder);
    }

    private void PlaceAtFallbackBottom()
    {
        if (_disposed || _windowHandle == IntPtr.Zero || !_window.IsVisible)
        {
            return;
        }

        _ = SetWindowPos(
            _windowHandle,
            HwndBottom,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private void ApplyDwmAppearance()
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaExcludedFromPeek,
            ref enabled,
            Marshal.SizeOf<int>());

        var usePerPixelAlpha = UpdateDwmTransparencyMode(_backdropOptions.HideSurface);
        _backdropOptions = _backdropOptions with { UsePerPixelAlpha = usePerPixelAlpha };
        _backdropService?.Apply(_backdropOptions, forceNativeRefresh: true);
        ApplyDwmFramePresentation(_backdropOptions.HideSurface);
    }

    private bool UpdateDwmTransparencyMode(bool hideSurface)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return false;
        }

        // Windows 11 24H2+ can honor the premultiplied alpha already present
        // in WPF's redirected bitmap. This keeps icons fully opaque while the
        // rest of the HWND becomes genuinely transparent, without disabling
        // DWM composition and falling back to a white client area.
        var enabled = hideSurface ? 1 : 0;
        var result = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaRedirectionBitmapAlpha,
            ref enabled,
            Marshal.SizeOf<int>());
        return hideSurface && result == 0;
    }

    private void ApplyDwmFramePresentation(bool hideSurface)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        // Keep DWM composition enabled in both modes. Disabling non-client
        // rendering also breaks transparent client composition on some light
        // Windows themes and produces an opaque white rectangle.
        var nonClientRendering = DwmncrpEnabled;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaNcRenderingPolicy,
            ref nonClientRendering,
            Marshal.SizeOf<int>());

        // Let DWM own the anti-aliased rounded clip only while the organizer
        // surface is visible. A transparent pure-icon window needs no corner.
        var cornerPreference = hideSurface ? DwmwcpDoNotRound : DwmwcpRound;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());

        var noSystemBorder = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaBorderColor,
            ref noSystemBorder,
            Marshal.SizeOf<int>());

        _ = SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoOwnerZOrder);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmMouseActivate && !_allowInteractionActivation)
        {
            // MA_NOACTIVATE deliberately keeps the original mouse message, so a
            // shortcut still launches on the first click without taking focus.
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        if (message == _taskbarCreatedMessage)
        {
            ApplyDwmAppearance();
            RefreshZOrder();
        }
        else if (message is WmThemeChanged or WmDwmCompositionChanged)
        {
            ApplyDwmAppearance();
        }
        else if (message is WmDisplayChange or WmSettingChange or WmDpiChanged)
        {
            if (message == WmSettingChange)
            {
                ApplyDwmAppearance();
            }

            WorkAreaChanged?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ForegroundCoordinator.Unregister(this);
        _source?.RemoveHook(WindowProcedure);
        _backdropService?.Dispose();
    }

    private static class ForegroundCoordinator
    {
        private static readonly object SyncRoot = new();
        private static readonly HashSet<DesktopZOrderHost> Hosts = [];
        private static readonly WinEventDelegate ForegroundCallback = OnForegroundChanged;
        private static readonly WinEventDelegate DesktopReorderCallback = OnDesktopReordered;
        private static IntPtr _foregroundHook;
        private static IntPtr _desktopReorderHook;
        private static Dispatcher? _dispatcher;
        private static HwndSource? _desktopStateSensor;
        private static HwndSource? _desktopLayerAnchor;
        private static DispatcherTimer? _desktopStateTimer;
        private static bool _showDesktop;

        public static void Register(DesktopZOrderHost host)
        {
            lock (SyncRoot)
            {
                Hosts.Add(host);
                _dispatcher ??= host._dispatcher;
                EnsureDesktopLayerInfrastructure();
                if (_foregroundHook == IntPtr.Zero)
                {
                    _foregroundHook = SetWinEventHook(
                        EventSystemForeground,
                        EventSystemForeground,
                        IntPtr.Zero,
                        ForegroundCallback,
                        0,
                        0,
                        WineventOutOfContext | WineventSkipOwnProcess);
                }

                if (_desktopReorderHook == IntPtr.Zero)
                {
                    _desktopReorderHook = SetWinEventHook(
                        EventObjectReorder,
                        EventObjectReorder,
                        IntPtr.Zero,
                        DesktopReorderCallback,
                        0,
                        0,
                        WineventOutOfContext | WineventSkipOwnProcess);
                }
            }
        }

        public static void Unregister(DesktopZOrderHost host)
        {
            lock (SyncRoot)
            {
                Hosts.Remove(host);
                if (Hosts.Count != 0)
                {
                    return;
                }

                if (_foregroundHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_foregroundHook);
                    _foregroundHook = IntPtr.Zero;
                }


                if (_desktopReorderHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_desktopReorderHook);
                    _desktopReorderHook = IntPtr.Zero;
                }

                _desktopStateTimer?.Stop();
                _desktopStateTimer = null;
                _desktopLayerAnchor?.Dispose();
                _desktopLayerAnchor = null;
                _desktopStateSensor?.Dispose();
                _desktopStateSensor = null;
                _showDesktop = false;
                _dispatcher = null;
            }
        }

        public static void RefreshAll() => RefreshAll(GetForegroundWindow());

        private static void OnForegroundChanged(
            IntPtr hook,
            uint eventType,
            IntPtr foregroundWindow,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime)
        {
            Dispatcher? dispatcher;
            lock (SyncRoot)
            {
                dispatcher = _dispatcher;
            }

            DispatchDesktopRefresh(
                dispatcher,
                () => RefreshAfterForegroundChange(foregroundWindow));
        }

        private static void OnDesktopReordered(
            IntPtr hook,
            uint eventType,
            IntPtr reorderedWindow,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime)
        {
            var desktopIconsHost = GetDesktopIconsHostWindow();
            if (reorderedWindow != desktopIconsHost)
            {
                return;
            }

            Dispatcher? dispatcher;
            lock (SyncRoot)
            {
                dispatcher = _dispatcher;
            }

            DispatchDesktopRefresh(
                dispatcher,
                () => RefreshAll(GetForegroundWindow()));
        }

        private static void DispatchDesktopRefresh(Dispatcher? dispatcher, Action refresh)
        {
            if (dispatcher is null)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                refresh();
                return;
            }

            dispatcher.BeginInvoke(refresh, DispatcherPriority.Send);
        }

        private static void RefreshAfterForegroundChange(IntPtr foregroundWindow)
        {
            var desktopIconsHost = GetDesktopIconsHostWindow();
            if (foregroundWindow == desktopIconsHost && desktopIconsHost != IntPtr.Zero)
            {
                // Explorer moves the desktop host first and completes the rest of
                // its Show Desktop Z-order transaction a few milliseconds later.
                // Probe inside the same compositor frame so organizers never wait
                // for the 250 ms fallback timer and visibly disappear/reappear.
                const int maximumAttempts = 5;
                for (var attempt = 0; attempt < maximumAttempts; attempt++)
                {
                    if (IsDesktopShown(desktopIconsHost))
                    {
                        RefreshAll(foregroundWindow);
                        return;
                    }

                    Thread.Sleep(2);
                }
            }

            RefreshAll(foregroundWindow);
        }

        private static void RefreshAll(IntPtr foregroundWindow)
        {
            DesktopZOrderHost[] hosts;
            lock (SyncRoot)
            {
                hosts = Hosts.ToArray();
            }

            var visibleHosts = hosts
                .Where(host => !host._disposed &&
                               host._windowHandle != IntPtr.Zero &&
                               host._window.IsVisible &&
                               !host._allowInteractionActivation)
                .ToArray();
            if (visibleHosts.Length == 0)
            {
                return;
            }

            var desktopIconsHost = GetDesktopIconsHostWindow();
            var showDesktop = IsDesktopShown(desktopIconsHost);
            _showDesktop = showDesktop;
            if (_desktopStateTimer is not null)
            {
                _desktopStateTimer.Interval = TimeSpan.FromMilliseconds(showDesktop ? 100 : 250);
            }
            if (showDesktop && desktopIconsHost != IntPtr.Zero && _desktopLayerAnchor is not null)
            {
                PlaceAboveShownDesktop(visibleHosts, desktopIconsHost);
                return;
            }

            PlaceAnchorAtBottom();
            var desktopWindow = GetShellWindow();
            if (desktopWindow == IntPtr.Zero)
            {
                foreach (var host in visibleHosts)
                {
                    host.PlaceAtFallbackBottom();
                }

                return;
            }

            var hostHandles = visibleHosts
                .Select(host => host._windowHandle)
                .ToHashSet();
            var insertionPoint = GetWindow(desktopWindow, GwHwndPrev);
            while (insertionPoint != IntPtr.Zero && hostHandles.Contains(insertionPoint))
            {
                insertionPoint = GetWindow(insertionPoint, GwHwndPrev);
            }

            foreach (var host in visibleHosts)
            {
                _ = SetWindowPos(
                    host._windowHandle,
                    insertionPoint == IntPtr.Zero ? HwndTop : insertionPoint,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
                insertionPoint = host._windowHandle;
            }
        }

        private static void EnsureDesktopLayerInfrastructure()
        {
            if (_desktopStateSensor is not null || _dispatcher is null)
            {
                return;
            }

            _desktopStateSensor = CreateHiddenDesktopWindow("YitDesktopFold.DesktopStateSensor");
            _desktopLayerAnchor = CreateHiddenDesktopWindow("YitDesktopFold.DesktopLayerAnchor");
            _ = SetWindowPos(
                _desktopStateSensor.Handle,
                HwndBottom,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
            PlaceAnchorAtBottom();

            _desktopStateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _desktopStateTimer.Tick += (_, _) =>
            {
                var desktopIconsHost = GetDesktopIconsHostWindow();
                var showDesktop = IsDesktopShown(desktopIconsHost);
                if (showDesktop != _showDesktop || showDesktop)
                {
                    RefreshAll(GetForegroundWindow());
                }
            };
            _desktopStateTimer.Start();
        }

        private static HwndSource CreateHiddenDesktopWindow(string name)
        {
            var parameters = new HwndSourceParameters(name)
            {
                WindowStyle = WsPopup | WsDisabled,
                ExtendedWindowStyle = (int)(WsExToolWindow | WsExNoActivate),
                PositionX = -32000,
                PositionY = -32000,
                Width = 1,
                Height = 1,
            };
            return new HwndSource(parameters);
        }

        private static void PlaceAboveShownDesktop(
            IReadOnlyList<DesktopZOrderHost> visibleHosts,
            IntPtr desktopIconsHost)
        {
            var anchor = _desktopLayerAnchor!.Handle;
            _ = SetWindowPos(
                anchor,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);

            var hostHandles = visibleHosts.Select(host => host._windowHandle).ToHashSet();
            var candidate = GetWindow(desktopIconsHost, GwHwndPrev);
            while (candidate != IntPtr.Zero)
            {
                if (candidate != anchor &&
                    candidate != _desktopStateSensor?.Handle &&
                    !hostHandles.Contains(candidate) &&
                    (GetWindowLongPtr(candidate, GwlExStyle).ToInt64() & WsExTopmost) != 0)
                {
                    _ = SetWindowPos(
                        anchor,
                        candidate,
                        0,
                        0,
                        0,
                        0,
                        SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
                    break;
                }

                candidate = GetWindow(candidate, GwHwndPrev);
            }

            var insertionPoint = anchor;
            foreach (var host in visibleHosts)
            {
                _ = SetWindowPos(
                    host._windowHandle,
                    insertionPoint,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
                insertionPoint = host._windowHandle;
            }
        }

        private static void PlaceAnchorAtBottom()
        {
            if (_desktopLayerAnchor is null)
            {
                return;
            }

            _ = SetWindowPos(
                _desktopLayerAnchor.Handle,
                HwndBottom,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }

        private static bool IsDesktopShown(IntPtr desktopIconsHost)
        {
            var sensorSource = _desktopStateSensor;
            if (desktopIconsHost == IntPtr.Zero ||
                !IsWindowVisible(desktopIconsHost) ||
                sensorSource is null)
            {
                return false;
            }

            var sensor = sensorSource.Handle;
            var candidate = GetWindow(desktopIconsHost, GwHwndNext);
            while (candidate != IntPtr.Zero)
            {
                if (candidate == sensor)
                {
                    return true;
                }

                candidate = GetWindow(candidate, GwHwndNext);
            }

            return false;
        }

        private static IntPtr GetDesktopIconsHostWindow()
        {
            var shellWindow = GetShellWindow();
            if (shellWindow == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (FindWindowEx(shellWindow, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                return shellWindow;
            }

            _ = GetWindowThreadProcessId(shellWindow, out var shellProcessId);
            var worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(worker, out var workerProcessId);
                if (workerProcessId == shellProcessId &&
                    IsWindowVisible(worker) &&
                    FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    return worker;
                }
            }

            return IntPtr.Zero;
        }
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr eventHookModule,
        WinEventDelegate eventHook,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
