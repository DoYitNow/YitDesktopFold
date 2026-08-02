using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YitDesktopFold.Native.Services;

public sealed class ShellIconService
{
    private const int MaximumCachedIcons = 96;
    private const int ShellCanvasFrameDepth = 3;
    private const byte ShellCanvasFrameMaxAlpha = 80;
    private const byte ShellCanvasContentAlpha = 24;
    private const double ShellCanvasContentFill = 0.82;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint SiigbfBiggerSizeOk = 0x00000001;
    private const uint SiigbfIconOnly = 0x00000004;
    private const uint SiigbfScaleUp = 0x00000100;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkedListNode<string>> _cacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cacheOrder = [];

    public ImageSource? GetIcon(string path)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                TouchCacheEntry(path);
                return cached;
            }
        }

        var extracted = ExtractIcon(path);
        // Do not cache extraction failures: ConcurrentDictionary does not accept
        // null values, and a shortcut target can become available later.
        if (extracted is null)
        {
            return null;
        }

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                TouchCacheEntry(path);
                return cached;
            }

            _cache.Add(path, extracted);
            var node = _cacheOrder.AddLast(path);
            _cacheNodes.Add(path, node);
            while (_cache.Count > MaximumCachedIcons && _cacheOrder.First is { } oldest)
            {
                RemoveCacheEntry(oldest.Value);
            }
        }

        return extracted;
    }

    public void Forget(string path)
    {
        lock (_cacheLock)
        {
            RemoveCacheEntry(path);
        }
    }

    public void RetainOnly(IEnumerable<string> activePaths)
    {
        var retained = activePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_cacheLock)
        {
            foreach (var cachedPath in _cache.Keys.Where(path => !retained.Contains(path)).ToArray())
            {
                RemoveCacheEntry(cachedPath);
            }
        }
    }

    public void Clear()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
            _cacheNodes.Clear();
            _cacheOrder.Clear();
        }
    }

    private void TouchCacheEntry(string path)
    {
        if (!_cacheNodes.TryGetValue(path, out var node) || ReferenceEquals(node, _cacheOrder.Last))
        {
            return;
        }

        _cacheOrder.Remove(node);
        _cacheOrder.AddLast(node);
    }

    private void RemoveCacheEntry(string path)
    {
        _cache.Remove(path);
        if (_cacheNodes.Remove(path, out var node))
        {
            _cacheOrder.Remove(node);
        }
    }

    private static ImageSource? ExtractIcon(string path)
    {
        var cleanIconPath = ResolveShortcutTarget(path) ?? path;
        return GetHighResolutionShellImage(cleanIconPath)
            ?? GetHighResolutionShellImage(path)
            ?? GetFileInfoIcon(cleanIconPath);
    }

    private static ImageSource? GetHighResolutionShellImage(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path) &&
            !DesktopShellItemService.IsShellNamespacePath(path))
        {
            return null;
        }

        IShellItemImageFactory? factory = null;
        try
        {
            var interfaceId = typeof(IShellItemImageFactory).GUID;
            var result = SHCreateItemFromParsingName(path, IntPtr.Zero, ref interfaceId, out factory);
            if (result < 0 || factory is null)
            {
                return null;
            }

            result = factory.GetImage(
                new NativeSize(128, 128),
                SiigbfIconOnly | SiigbfBiggerSizeOk | SiigbfScaleUp,
                out var bitmapHandle);
            if (result < 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                var cleanedSource = RemoveLowAlphaCanvasFrame(source);
                cleanedSource.Freeze();
                return cleanedSource;
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (factory is not null && Marshal.IsComObject(factory))
            {
                Marshal.FinalReleaseComObject(factory);
            }
        }
    }

    private static BitmapSource RemoveLowAlphaCanvasFrame(BitmapSource source)
    {
        if (source.PixelWidth <= ShellCanvasFrameDepth * 2 ||
            source.PixelHeight <= ShellCanvasFrameDepth * 2)
        {
            return source;
        }

        var normalized = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = normalized.PixelWidth * 4;
        var pixels = new byte[stride * normalized.PixelHeight];
        normalized.CopyPixels(pixels, stride, 0);

        if (!HasLowAlphaCanvasFrame(
                pixels,
                normalized.PixelWidth,
                normalized.PixelHeight,
                stride))
        {
            return source;
        }

        for (var y = 0; y < normalized.PixelHeight; y++)
        {
            for (var x = 0; x < normalized.PixelWidth; x++)
            {
                if (x >= ShellCanvasFrameDepth &&
                    x < normalized.PixelWidth - ShellCanvasFrameDepth &&
                    y >= ShellCanvasFrameDepth &&
                    y < normalized.PixelHeight - ShellCanvasFrameDepth)
                {
                    continue;
                }

                var offset = y * stride + x * 4;
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = 0;
            }
        }

        var frameCleaned = BitmapSource.Create(
            normalized.PixelWidth,
            normalized.PixelHeight,
            normalized.DpiX,
            normalized.DpiY,
            PixelFormats.Pbgra32,
            null,
            pixels,
            stride);
        return CropExcessCanvasPadding(frameCleaned, pixels, stride);
    }

    private static BitmapSource CropExcessCanvasPadding(
        BitmapSource source,
        byte[] pixels,
        int stride)
    {
        var minX = source.PixelWidth;
        var minY = source.PixelHeight;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < source.PixelHeight; y++)
        {
            for (var x = 0; x < source.PixelWidth; x++)
            {
                if (pixels[y * stride + x * 4 + 3] < ShellCanvasContentAlpha)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return source;
        }

        var contentWidth = maxX - minX + 1;
        var contentHeight = maxY - minY + 1;
        var contentSize = Math.Max(contentWidth, contentHeight);
        var cropSize = Math.Clamp(
            (int)Math.Ceiling(contentSize / ShellCanvasContentFill),
            contentSize,
            Math.Min(source.PixelWidth, source.PixelHeight));
        if (cropSize >= Math.Min(source.PixelWidth, source.PixelHeight))
        {
            return source;
        }

        var centerX = (minX + maxX) / 2d;
        var centerY = (minY + maxY) / 2d;
        var cropX = Math.Clamp(
            (int)Math.Round(centerX - cropSize / 2d),
            0,
            source.PixelWidth - cropSize);
        var cropY = Math.Clamp(
            (int)Math.Round(centerY - cropSize / 2d),
            0,
            source.PixelHeight - cropSize);

        return new CroppedBitmap(
            source,
            new Int32Rect(cropX, cropY, cropSize, cropSize));
    }

    private static bool HasLowAlphaCanvasFrame(
        byte[] pixels,
        int width,
        int height,
        int stride)
    {
        var sampledPixels = 0;
        var lowAlphaPixels = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= ShellCanvasFrameDepth &&
                    x < width - ShellCanvasFrameDepth &&
                    y >= ShellCanvasFrameDepth &&
                    y < height - ShellCanvasFrameDepth)
                {
                    continue;
                }

                sampledPixels++;
                var alpha = pixels[y * stride + x * 4 + 3];
                if (alpha > ShellCanvasFrameMaxAlpha)
                {
                    return false;
                }

                if (alpha > 0)
                {
                    lowAlphaPixels++;
                }
            }
        }

        return lowAlphaPixels >= sampledPixels * 0.92;
    }

    private static string? ResolveShortcutTarget(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        object? shellLinkObject = null;
        try
        {
            shellLinkObject = new ShellLink();
            var shellLink = (IShellLinkW)shellLinkObject;
            ((IPersistFile)shellLinkObject).Load(path, 0);
            var target = new StringBuilder(32768);
            shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0x00000004);
            var expandedTarget = Environment.ExpandEnvironmentVariables(target.ToString());
            return File.Exists(expandedTarget) || Directory.Exists(expandedTarget)
                ? expandedTarget
                : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }

    private static ImageSource? GetFileInfoIcon(string path)
    {
        var result = SHGetFileInfo(
            path,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiLargeIcon);

        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
        {
            result = SHGetFileInfo(
                path,
                FileAttributeNormal,
                out fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
        }

        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(fileInfo.IconHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, uint flags, out IntPtr bitmapHandle);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
        void GetIdList(out IntPtr itemIdList);
        void SetIdList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCommand(out int showCommand);
        void SetShowCommand(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        out SHFILEINFO fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
