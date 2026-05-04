using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenQuickHost;

public static class NativeShellContextMenu
{
    private const uint CmfNormal = 0x00000000;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint SwShownormal = 1;
    private const uint CmicUnicode = 0x00004000;
    private const uint CmicPtInvoke = 0x20000000;
    private const uint WmInitMenuPopup = 0x0117;
    private const uint WmDrawItem = 0x002B;
    private const uint WmMeasureItem = 0x002C;
    private const uint WmMenuChar = 0x0120;
    private const uint WmMenuSelect = 0x011F;
    private const uint WmUninitMenuPopup = 0x0125;
    private const uint WmNull = 0x0000;

    public static bool ShowForPath(Window owner, string path, System.Windows.Point screenPoint, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "路径为空。";
            return false;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            errorMessage = $"目标不存在：{path}";
            return false;
        }

        var ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
        IntPtr absolutePidl = IntPtr.Zero;
        IntPtr childPidl = IntPtr.Zero;
        IntPtr menuHandle = IntPtr.Zero;
        IShellFolder? shellFolder = null;
        IContextMenu? contextMenu = null;
        IContextMenu2? contextMenu2 = null;
        IContextMenu3? contextMenu3 = null;
        IntPtr contextMenuPtr = IntPtr.Zero;
        HwndSource? source = null;
        HwndSourceHook? hook = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out absolutePidl, 0, out _));
            var shellFolderGuid = ShellFolderGuid;
            Marshal.ThrowExceptionForHR(SHBindToParent(absolutePidl, ref shellFolderGuid, out shellFolder, out childPidl));

            var contextMenuGuid = ContextMenuGuid;
            shellFolder.GetUIObjectOf(ownerHandle, 1, [childPidl], ref contextMenuGuid, IntPtr.Zero, out contextMenuPtr);
            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            contextMenu2 = contextMenu as IContextMenu2;
            contextMenu3 = contextMenu as IContextMenu3;

            menuHandle = CreatePopupMenu();
            if (menuHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建 Shell 菜单句柄。");
            }

            contextMenu.QueryContextMenu(menuHandle, 0, 1, 0x7FFF, CmfNormal);
            source = HwndSource.FromHwnd(ownerHandle);
            if (source != null && (contextMenu2 != null || contextMenu3 != null))
            {
                hook = delegate (IntPtr _, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    return HandleMenuMessage(contextMenu2, contextMenu3, msg, wParam, lParam, ref handled);
                };
                source.AddHook(hook);
            }

            SetForegroundWindow(ownerHandle);
            var command = TrackPopupMenuEx(
                menuHandle,
                TpmReturnCmd | TpmRightButton,
                (int)screenPoint.X,
                (int)screenPoint.Y,
                ownerHandle,
                IntPtr.Zero);

            if (command == 0)
            {
                PostMessage(ownerHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            var invoke = new CminvokeCommandInfoEx
            {
                cbSize = Marshal.SizeOf<CminvokeCommandInfoEx>(),
                fMask = CmicUnicode | CmicPtInvoke,
                hwnd = ownerHandle,
                lpVerb = (IntPtr)(command - 1),
                lpVerbW = (IntPtr)(command - 1),
                nShow = SwShownormal,
                ptInvoke = new NativePoint((int)screenPoint.X, (int)screenPoint.Y)
            };

            contextMenu.InvokeCommand(ref invoke);
            PostMessage(ownerHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
        finally
        {
            if (source != null && hook != null)
            {
                source.RemoveHook(hook);
            }

            if (menuHandle != IntPtr.Zero)
            {
                DestroyMenu(menuHandle);
            }

            if (contextMenuPtr != IntPtr.Zero)
            {
                Marshal.Release(contextMenuPtr);
            }

            if (contextMenu != null)
            {
                Marshal.ReleaseComObject(contextMenu);
            }

            if (shellFolder != null)
            {
                Marshal.ReleaseComObject(shellFolder);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                CoTaskMemFree(absolutePidl);
            }
        }
    }

    private static Guid ShellFolderGuid => new("000214E6-0000-0000-C000-000000000046");

    private static Guid ContextMenuGuid => new("000214E4-0000-0000-C000-000000000046");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr bindingContext,
        out IntPtr pidl,
        uint sfgaoIn,
        out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPStruct)] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv,
        out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hmenu,
        uint flags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr tpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    private static IntPtr HandleMenuMessage(IContextMenu2? contextMenu2, IContextMenu3? contextMenu3, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case WmInitMenuPopup:
            case WmDrawItem:
            case WmMeasureItem:
            case WmMenuSelect:
            case WmUninitMenuPopup:
                if (contextMenu3 != null)
                {
                    contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out _);
                    handled = true;
                }
                else if (contextMenu2 != null)
                {
                    contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
                    handled = true;
                }

                break;
            case WmMenuChar:
                if (contextMenu3 != null)
                {
                    contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result);
                    handled = true;
                    return result;
                }

                if (contextMenu2 != null)
                {
                    contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
                    handled = true;
                }

                break;
        }

        return IntPtr.Zero;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CminvokeCommandInfoEx info);

        void GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pReserved, [MarshalAs(UnmanagedType.LPStr)] string? pszName, uint cchMax);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CminvokeCommandInfoEx info);

        void GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pReserved, [MarshalAs(UnmanagedType.LPStr)] string? pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCFCE0A0-EC17-11d0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CminvokeCommandInfoEx info);

        void GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pReserved, [MarshalAs(UnmanagedType.LPStr)] string? pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(int lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, [MarshalAs(UnmanagedType.LPStruct)] ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, out STRRET pName);

        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CminvokeCommandInfoEx
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;
        public uint nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTitleW;
        public NativePoint ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct STRRET
    {
        [FieldOffset(0)]
        public uint uType;

        [FieldOffset(4)]
        public IntPtr pOleStr;

        [FieldOffset(4)]
        public uint uOffset;
    }
}
