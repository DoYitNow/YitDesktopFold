using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Builds an identity that survives a rename or move within the same volume.
/// A normalized path is retained only as a fallback for providers that do not
/// expose a Win32 file ID (some cloud and virtual filesystem implementations).
/// </summary>
public static class DesktopItemIdentityService
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public static string GetIdentity(string path)
    {
        var fullPath = Path.GetFullPath(path);
        using var handle = CreateFile(
            fullPath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (!handle.IsInvalid && GetFileInformationByHandle(handle, out var information))
        {
            var fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            return $"win32:{information.VolumeSerialNumber:x8}:{fileIndex:x16}";
        }

        return GetPathFallbackIdentity(fullPath);
    }

    public static string GetPathFallbackIdentity(string path) =>
        "path:" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);
}
