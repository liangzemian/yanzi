using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace OpenQuickHost;

public static class KeyboardDoubleTapService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const uint LlkhfInjected = 0x00000010;

    private static readonly LowLevelKeyboardProc Proc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;
    private static Action<string>? _onDoubleTap;
    private static ModifierTapKind _lastTapKind = ModifierTapKind.None;
    private static long _lastTapTimestamp;
    private static bool _sequenceDirty;
    private static bool _leftCtrlDown;
    private static bool _rightCtrlDown;
    private static bool _leftAltDown;
    private static bool _rightAltDown;
    private static bool _leftShiftDown;
    private static bool _rightShiftDown;
    private static bool _leftWinDown;
    private static bool _rightWinDown;
    private static bool _doubleCtrlEnabled = true;
    private static bool _doubleAltEnabled = true;
    private static bool _suppressCurrentAltTap;

    public static bool IsRunning => _hookId != IntPtr.Zero;

    public static void Start(Action<string> onDoubleTap)
    {
        if (IsRunning)
        {
            HostAssets.AppendLog("Keyboard double tap: start skipped because hook is already running.");
            return;
        }

        _onDoubleTap = onDoubleTap;
        _sequenceDirty = false;
        _lastTapKind = ModifierTapKind.None;
        _lastTapTimestamp = 0;
        ApplyConfiguredShortcut(AppSettingsStore.Load().LauncherHotkey);
        _hookId = SetHook(Proc);
        if (_hookId == IntPtr.Zero)
        {
            HostAssets.AppendLog($"Keyboard double tap: failed to install hook, lastError={Marshal.GetLastWin32Error()}.");
            return;
        }

        HostAssets.AppendLog($"Keyboard double tap: started. hook=0x{_hookId.ToInt64():X}, triggers=DoubleCtrl,DoubleAlt.");
    }

    public static void ApplyConfiguredShortcut(string? shortcut)
    {
        _doubleCtrlEnabled = string.Equals(shortcut, "DoubleCtrl", StringComparison.OrdinalIgnoreCase);
        _doubleAltEnabled = string.Equals(shortcut, "DoubleAlt", StringComparison.OrdinalIgnoreCase);
        _suppressCurrentAltTap = false;
    }

    public static void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        var unhooked = UnhookWindowsHookEx(_hookId);
        HostAssets.AppendLog($"Keyboard double tap: stopped. unhooked={unhooked}.");
        _hookId = IntPtr.Zero;
        _onDoubleTap = null;
        _lastTapKind = ModifierTapKind.None;
        _sequenceDirty = false;
        ResetKeyState();
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        var moduleHandle = GetModuleHandle(currentModule.ModuleName);
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
        if (hook != IntPtr.Zero)
        {
            return hook;
        }

        HostAssets.AppendLog($"Keyboard double tap: SetWindowsHookEx failed with module handle, module={currentModule.ModuleName}, lastError={Marshal.GetLastWin32Error()}; retrying with hMod=0.");
        return SetWindowsHookEx(WhKeyboardLl, proc, IntPtr.Zero, 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((info.flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            var vkCode = (int)info.vkCode;
            var suppress = false;

            if (message is WmKeyDown or WmSysKeyDown)
            {
                suppress = HandleKeyDown(vkCode);
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                suppress = HandleKeyUp(vkCode);
            }

            if (suppress)
            {
                return 1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool HandleKeyDown(int vkCode)
    {
        switch (vkCode)
        {
            case VkLControl:
                _leftCtrlDown = true;
                return false;
            case VkRControl:
                _rightCtrlDown = true;
                return false;
            case VkLMenu:
                if (ShouldSuppressCurrentAltTap())
                {
                    _leftAltDown = true;
                    _suppressCurrentAltTap = true;
                    return true;
                }

                _leftAltDown = true;
                return false;
            case VkRMenu:
                if (ShouldSuppressCurrentAltTap())
                {
                    _rightAltDown = true;
                    _suppressCurrentAltTap = true;
                    return true;
                }

                _rightAltDown = true;
                return false;
            case VkLShift:
                _leftShiftDown = true;
                return false;
            case VkRShift:
                _rightShiftDown = true;
                return false;
            case VkLWin:
                _leftWinDown = true;
                return false;
            case VkRWin:
                _rightWinDown = true;
                return false;
        }

        _sequenceDirty = true;
        return false;
    }

    private static bool HandleKeyUp(int vkCode)
    {
        ModifierTapKind releasedKind;
        switch (vkCode)
        {
            case VkLControl:
                _leftCtrlDown = false;
                releasedKind = ModifierTapKind.Control;
                break;
            case VkRControl:
                _rightCtrlDown = false;
                releasedKind = ModifierTapKind.Control;
                break;
            case VkLMenu:
                _leftAltDown = false;
                releasedKind = ModifierTapKind.Alt;
                break;
            case VkRMenu:
                _rightAltDown = false;
                releasedKind = ModifierTapKind.Alt;
                break;
            case VkLShift:
                _leftShiftDown = false;
                return false;
            case VkRShift:
                _rightShiftDown = false;
                return false;
            case VkLWin:
                _leftWinDown = false;
                return false;
            case VkRWin:
                _rightWinDown = false;
                return false;
            default:
                _sequenceDirty = true;
                return false;
        }

        if (HasOtherModifiersPressed(releasedKind))
        {
            _sequenceDirty = true;
            _suppressCurrentAltTap = false;
            return false;
        }

        var now = Environment.TickCount64;
        if (!_sequenceDirty &&
            _lastTapKind == releasedKind &&
            now - _lastTapTimestamp <= 350)
        {
            var shouldSuppress = releasedKind == ModifierTapKind.Alt && _doubleAltEnabled && _suppressCurrentAltTap;
            _lastTapKind = ModifierTapKind.None;
            _lastTapTimestamp = 0;
            _suppressCurrentAltTap = false;
            HostAssets.AppendLog($"Keyboard double tap: triggered {releasedKind}.");
            if (shouldSuppress)
            {
                CancelForegroundAltMenuMode();
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() => _onDoubleTap?.Invoke(releasedKind.ToString()));
            return shouldSuppress;
        }

        _lastTapKind = releasedKind;
        _lastTapTimestamp = now;
        _sequenceDirty = false;
        if (releasedKind != ModifierTapKind.Alt)
        {
            _suppressCurrentAltTap = false;
        }

        return releasedKind == ModifierTapKind.Alt && _doubleAltEnabled && _suppressCurrentAltTap;
    }

    private static bool HasOtherModifiersPressed(ModifierTapKind releasedKind)
    {
        return releasedKind switch
        {
            ModifierTapKind.Control => _leftAltDown || _rightAltDown || _leftShiftDown || _rightShiftDown || _leftWinDown || _rightWinDown,
            ModifierTapKind.Alt => _leftCtrlDown || _rightCtrlDown || _leftShiftDown || _rightShiftDown || _leftWinDown || _rightWinDown,
            _ => true
        };
    }

    private static void ResetKeyState()
    {
        _leftCtrlDown = false;
        _rightCtrlDown = false;
        _leftAltDown = false;
        _rightAltDown = false;
        _leftShiftDown = false;
        _rightShiftDown = false;
        _leftWinDown = false;
        _rightWinDown = false;
        _suppressCurrentAltTap = false;
    }

    private static bool ShouldSuppressCurrentAltTap()
    {
        if (!_doubleAltEnabled || _sequenceDirty)
        {
            return false;
        }

        var now = Environment.TickCount64;
        return _lastTapKind == ModifierTapKind.Alt &&
               now - _lastTapTimestamp <= 350 &&
               !_leftCtrlDown &&
               !_rightCtrlDown &&
               !_leftShiftDown &&
               !_rightShiftDown &&
               !_leftWinDown &&
               !_rightWinDown;
    }

    private static void CancelForegroundAltMenuMode()
    {
        const uint keyEventKeyUp = 0x0002;
        keybd_event((byte)VkEscape, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VkEscape, 0, keyEventKeyUp, UIntPtr.Zero);
    }

    private enum ModifierTapKind
    {
        None,
        Control,
        Alt
    }

    private const int VkEscape = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
