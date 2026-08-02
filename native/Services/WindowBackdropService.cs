using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YitDesktopFold.Native.Services;

public readonly record struct WindowBackdropOptions(
    bool Enabled,
    double Opacity,
    bool UseLightBackground,
    bool HideSurface = false,
    bool UsePerPixelAlpha = false)
{
    public static WindowBackdropOptions Default => new(true, 0.72, false, false, false);

    public WindowBackdropOptions Normalized() => new(
        Enabled,
        Math.Clamp(double.IsFinite(Opacity) ? Opacity : Default.Opacity, 0, 1),
        UseLightBackground,
        HideSurface,
        UsePerPixelAlpha);
}

/// <summary>
/// Applies the lightweight compositor acrylic that is known to render visibly
/// on the organizer's ownerless, no-activate desktop windows.
/// </summary>
public sealed class WindowBackdropService : IDisposable
{
    private const int WcaAccentPolicy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const uint AcrylicAccentFlags = 0x2;

    private static readonly Color TransparentBlack = Color.FromArgb(0, 0, 0, 0);
    private static readonly Color DarkSmoke = Color.FromRgb(0x30, 0x35, 0x3A);
    private static readonly Color LightSmoke = Color.FromRgb(0xF2, 0xF2, 0xF0);

    private readonly Window _window;
    private readonly IntPtr _windowHandle;
    private readonly HwndSource? _source;
    private bool _hasApplied;
    private bool _isBackdropActive;
    private bool _disposed;

    public WindowBackdropService(Window window, IntPtr windowHandle, HwndSource? source)
    {
        _window = window;
        _windowHandle = windowHandle;
        _source = source;
    }

    public WindowBackdropOptions Options { get; private set; } = WindowBackdropOptions.Default;

    public bool IsBackdropActive => _isBackdropActive;

    public void Apply(WindowBackdropOptions options, bool forceNativeRefresh = false)
    {
        if (_disposed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        var previousOptions = Options;
        Options = options.Normalized();
        if (_hasApplied && !forceNativeRefresh && Options == previousOptions)
        {
            return;
        }

        _hasApplied = true;
        var darkMode = Options.UseLightBackground ? 0 : 1;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaUseImmersiveDarkMode,
            ref darkMode,
            Marshal.SizeOf<int>());

        PrepareTransparentClient();
        if (Options.HideSurface && Options.UsePerPixelAlpha)
        {
            // Windows 11 24H2 can composite WPF's premultiplied redirect
            // bitmap directly. Disabling the accent layer here leaves truly
            // transparent pixels around the opaque shortcut artwork.
            DisableAccent();
            return;
        }

        var tint = Options.UseLightBackground ? LightSmoke : DarkSmoke;
        var accentState = Options.Enabled
            ? Options.UseLightBackground
                ? AccentState.BlurBehind
                : AccentState.AcrylicBlurBehind
            : AccentState.TransparentGradient;
        // Acrylic's built-in light luminosity layer stays nearly opaque even
        // when its tint alpha approaches zero. In light glass mode the native
        // layer therefore performs blur only; MainWindow supplies the thin,
        // independently controlled white tint as a WPF surface.
        var nativeTint = accentState is AccentState.BlurBehind ? TransparentBlack : tint;
        var tintAlpha = Options.HideSurface || accentState is AccentState.BlurBehind
            ? (byte)0
            : MapTintAlpha(Options.Opacity);
        _isBackdropActive = TrySetAccent(
            accentState,
            accentState is AccentState.AcrylicBlurBehind ? AcrylicAccentFlags : 0,
            tintAlpha,
            nativeTint);
        if (!_isBackdropActive)
        {
            DisableAccent();
            if (Options.HideSurface)
            {
                PrepareTransparentClient();
            }
            else
            {
                RestoreOpaqueClient(tint);
            }
        }
    }

    private void PrepareTransparentClient()
    {
        if (_source?.CompositionTarget is not null)
        {
            _source.CompositionTarget.BackgroundColor = TransparentBlack;
        }

        _window.Background = Brushes.Transparent;
    }

    private void RestoreOpaqueClient(Color fallback)
    {
        if (_source?.CompositionTarget is not null)
        {
            _source.CompositionTarget.BackgroundColor = fallback;
        }

        _window.Background = CreateFrozenBrush(fallback);
    }

    private void DisableAccent()
    {
        _ = TrySetAccent(AccentState.Disabled, 0, 0, TransparentBlack);
        _isBackdropActive = false;
    }

    private bool TrySetAccent(AccentState state, uint flags, byte alpha, Color tint)
    {
        var policy = new AccentPolicy
        {
            State = state,
            Flags = flags,
            GradientColor = PackAbgr(alpha, tint),
            AnimationId = 0,
        };
        var policyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, fDeleteOld: false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = Marshal.SizeOf<AccentPolicy>(),
            };
            return SetWindowCompositionAttribute(_windowHandle, ref data);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    private static uint PackAbgr(byte alpha, Color color) =>
        ((uint)alpha << 24) |
        ((uint)color.B << 16) |
        ((uint)color.G << 8) |
        color.R;

    private static byte MapTintAlpha(double opacity)
    {
        var normalizedOpacity = Math.Clamp(opacity, 0, 1);
        if (normalizedOpacity <= 0)
        {
            return 1;
        }

        if (normalizedOpacity >= 1)
        {
            return 255;
        }

        return (byte)Math.Clamp(Math.Round(normalizedOpacity * 255), 1, 255);
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisableAccent();
        _disposed = true;
    }

    private enum AccentState
    {
        Disabled = 0,
        TransparentGradient = 2,
        BlurBehind = 3,
        AcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public uint Flags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(
        IntPtr window,
        ref WindowCompositionAttributeData data);
}
