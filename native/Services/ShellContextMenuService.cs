using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Requests the real item menu from the Windows Shell. This keeps organizer
/// items aligned with Explorer, including registered third-party extensions.
/// </summary>
public static class ShellContextMenuService
{
    private const uint CmfCanRename = 0x00000010;
    private const uint CmfExtendedVerbs = 0x00000100;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7FFF;
    private const int SwShowNormal = 1;
    private const int WmNull = 0x0000;
    private const int WmInitMenuPopup = 0x0117;
    private const int WmDrawItem = 0x002B;
    private const int WmMeasureItem = 0x002C;
    private const int WmMenuChar = 0x0120;

    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");

    public static bool TryInvokeVerb(string parsingPath, HwndSource ownerSource, string verb)
    {
        if (string.IsNullOrWhiteSpace(parsingPath) ||
            string.IsNullOrWhiteSpace(verb) ||
            ownerSource.Handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr absolutePidl = IntPtr.Zero;
        IntPtr childArray = IntPtr.Zero;
        IntPtr contextMenuPointer = IntPtr.Zero;
        IntPtr verbPointer = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;

        try
        {
            if (SHParseDisplayName(parsingPath, IntPtr.Zero, out absolutePidl, 0, out _) < 0 ||
                absolutePidl == IntPtr.Zero)
            {
                return false;
            }

            var shellFolderId = ShellFolderId;
            if (SHBindToParent(
                    absolutePidl,
                    ref shellFolderId,
                    out parentFolder,
                    out var childPidl) < 0 ||
                parentFolder is null ||
                childPidl == IntPtr.Zero)
            {
                return false;
            }

            childArray = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(childArray, childPidl);
            var contextMenuId = ContextMenuId;
            if (parentFolder.GetUIObjectOf(
                    ownerSource.Handle,
                    1,
                    childArray,
                    ref contextMenuId,
                    IntPtr.Zero,
                    out contextMenuPointer) < 0 ||
                contextMenuPointer == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPointer);
            Marshal.Release(contextMenuPointer);
            contextMenuPointer = IntPtr.Zero;
            verbPointer = Marshal.StringToCoTaskMemAnsi(verb);
            var invocation = new CommandInvocation
            {
                Size = Marshal.SizeOf<CommandInvocation>(),
                Owner = ownerSource.Handle,
                Verb = verbPointer,
                Show = SwShowNormal,
            };
            return contextMenu.InvokeCommand(ref invocation) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (verbPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(verbPointer);
            }

            if (contextMenu is not null && Marshal.IsComObject(contextMenu))
            {
                _ = Marshal.FinalReleaseComObject(contextMenu);
            }

            if (contextMenuPointer != IntPtr.Zero)
            {
                _ = Marshal.Release(contextMenuPointer);
            }

            if (parentFolder is not null && Marshal.IsComObject(parentFolder))
            {
                _ = Marshal.FinalReleaseComObject(parentFolder);
            }

            if (childArray != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(childArray);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(absolutePidl);
            }
        }
    }

    public static bool TryShow(
        string parsingPath,
        HwndSource ownerSource,
        System.Windows.Point? screenAnchor = null)
    {
        if (string.IsNullOrWhiteSpace(parsingPath) || ownerSource.Handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr absolutePidl = IntPtr.Zero;
        IntPtr childArray = IntPtr.Zero;
        IntPtr contextMenuPointer = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        var menu = IntPtr.Zero;

        try
        {
            if (SHParseDisplayName(parsingPath, IntPtr.Zero, out absolutePidl, 0, out _) < 0 ||
                absolutePidl == IntPtr.Zero)
            {
                return false;
            }

            var shellFolderId = ShellFolderId;
            if (SHBindToParent(
                    absolutePidl,
                    ref shellFolderId,
                    out parentFolder,
                    out var childPidl) < 0 ||
                parentFolder is null ||
                childPidl == IntPtr.Zero)
            {
                return false;
            }

            childArray = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(childArray, childPidl);
            var contextMenuId = ContextMenuId;
            if (parentFolder.GetUIObjectOf(
                    ownerSource.Handle,
                    1,
                    childArray,
                    ref contextMenuId,
                    IntPtr.Zero,
                    out contextMenuPointer) < 0 ||
                contextMenuPointer == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPointer);
            Marshal.Release(contextMenuPointer);
            contextMenuPointer = IntPtr.Zero;

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            var flags = CmfCanRename;
            if ((GetKeyState(0x10) & 0x8000) != 0)
            {
                flags |= CmfExtendedVerbs;
            }

            if (contextMenu.QueryContextMenu(
                    menu,
                    0,
                    FirstCommandId,
                    LastCommandId,
                    flags) < 0)
            {
                return false;
            }

            NativePoint cursor;
            if (screenAnchor is { } anchor)
            {
                cursor = new NativePoint
                {
                    X = (int)Math.Round(anchor.X),
                    Y = (int)Math.Round(anchor.Y),
                };
            }
            else if (!GetCursorPos(out cursor))
            {
                return false;
            }

            using var forwarder = new MenuMessageForwarder(ownerSource, contextMenu);
            var selected = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCommand,
                cursor.X,
                cursor.Y,
                ownerSource.Handle,
                IntPtr.Zero);
            _ = PostMessage(ownerSource.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (selected == 0)
            {
                return true;
            }

            var invocation = new CommandInvocation
            {
                Size = Marshal.SizeOf<CommandInvocation>(),
                Owner = ownerSource.Handle,
                Verb = new IntPtr(selected - FirstCommandId),
                Show = SwShowNormal,
            };
            return contextMenu.InvokeCommand(ref invocation) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                _ = DestroyMenu(menu);
            }

            if (contextMenu is not null && Marshal.IsComObject(contextMenu))
            {
                _ = Marshal.FinalReleaseComObject(contextMenu);
            }

            if (contextMenuPointer != IntPtr.Zero)
            {
                _ = Marshal.Release(contextMenuPointer);
            }

            if (parentFolder is not null && Marshal.IsComObject(parentFolder))
            {
                _ = Marshal.FinalReleaseComObject(parentFolder);
            }

            if (childArray != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(childArray);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(absolutePidl);
            }
        }
    }

    private sealed class MenuMessageForwarder : IDisposable
    {
        private readonly HwndSource _source;
        private readonly IContextMenu2? _menu2;
        private readonly IContextMenu3? _menu3;

        public MenuMessageForwarder(HwndSource source, IContextMenu menu)
        {
            _source = source;
            _menu3 = menu as IContextMenu3;
            _menu2 = _menu3 is null ? menu as IContextMenu2 : null;
            _source.AddHook(WindowProcedure);
        }

        public void Dispose() => _source.RemoveHook(WindowProcedure);

        private IntPtr WindowProcedure(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar))
            {
                return IntPtr.Zero;
            }

            if (_menu3 is not null &&
                _menu3.HandleMenuMsg2((uint)message, wParam, lParam, out var result) >= 0)
            {
                handled = message == WmMenuChar;
                return result;
            }

            if (_menu2 is not null && _menu2.HandleMenuMsg((uint)message, wParam, lParam) >= 0)
            {
                handled = message == WmMenuChar;
            }

            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandInvocation
    {
        public int Size;
        public uint Mask;
        public IntPtr Owner;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr owner, IntPtr bindContext, IntPtr displayName, IntPtr eaten, IntPtr itemIdList, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr owner, uint flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr parameter, IntPtr first, IntPtr second);
        [PreserveSig] int CreateViewObject(IntPtr owner, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, IntPtr itemIdLists, IntPtr attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr owner, uint count, IntPtr itemIdLists, ref Guid interfaceId, IntPtr reserved, out IntPtr result);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInvocation invocation);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInvocation invocation);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInvocation invocation);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr bindContext,
        out IntPtr itemIdList,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr itemIdList,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder parent,
        out IntPtr childItemIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr owner,
        IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
