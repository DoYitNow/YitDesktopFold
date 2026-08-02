using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace YitDesktopFold.Native.Services;

public static class DesktopContrastService
{
    private static readonly Color LightForeground = Color.FromArgb(0xE8, 0xF2, 0xF3, 0xF4);
    private static readonly Color DarkForeground = Color.FromArgb(0xE8, 0x1C, 0x21, 0x26);

    public static Color ResolveForeground(
        Window window,
        bool preferLightForeground,
        double backgroundOpacity)
    {
        if (backgroundOpacity > 0.12 || !window.IsLoaded || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return preferLightForeground ? LightForeground : DarkForeground;
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return preferLightForeground ? LightForeground : DarkForeground;
        }

        try
        {
            var points = new[]
            {
                new Point(10, 10),
                new Point(Math.Max(10, window.ActualWidth - 10), 10),
                new Point(10, Math.Max(10, window.ActualHeight - 10)),
                new Point(Math.Max(10, window.ActualWidth - 10), Math.Max(10, window.ActualHeight - 10)),
                new Point(window.ActualWidth * 0.5, 10),
                new Point(window.ActualWidth * 0.5, Math.Max(10, window.ActualHeight - 10)),
            };
            var luminanceTotal = 0d;
            var samples = 0;
            foreach (var point in points)
            {
                var devicePoint = window.PointToScreen(point);
                var pixel = GetPixel(screen, (int)Math.Round(devicePoint.X), (int)Math.Round(devicePoint.Y));
                if (pixel == uint.MaxValue)
                {
                    continue;
                }

                var red = pixel & 0xFF;
                var green = (pixel >> 8) & 0xFF;
                var blue = (pixel >> 16) & 0xFF;
                luminanceTotal += (red * 0.2126 + green * 0.7152 + blue * 0.0722) / 255d;
                samples++;
            }

            if (samples == 0)
            {
                return preferLightForeground ? LightForeground : DarkForeground;
            }

            return luminanceTotal / samples < 0.52 ? LightForeground : DarkForeground;
        }
        catch (InvalidOperationException)
        {
            return preferLightForeground ? LightForeground : DarkForeground;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);
}
