from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one exact match, found {count}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/iKeyd.Core/Input/KeyboardContracts.cs",
    "public readonly record struct KeyboardKey(ushort VirtualKey, ushort ScanCode, bool IsExtended = false);",
    "public readonly record struct KeyboardKey(ushort VirtualKey, ushort ScanCode, bool IsExtended = false, bool PreserveVirtualKeyWithScanCode = false);",
)

replace_once(
    "src/iKeyd.App/WindowsKeyMap.cs",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey));\n        return true;",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey), PreserveVirtualKeyWithScanCode: true);\n        return true;",
)

replace_once(
    "src/iKeyd.Windows/Input/WindowsKeyboardOutput.cs",
    '''        if (scanCode != 0)\n        {\n            virtualKey = 0;\n            flags |= KeyEventScanCode;\n        }''',
    '''        // Generic scan-code injection intentionally asks Windows to resolve the\n        // virtual key from the physical scan code. Explicit AHK vk+sc tokens are\n        // different: AHK preserves both wVk and wScan without KEYEVENTF_SCANCODE.\n        if (scanCode != 0 && !key.PreserveVirtualKeyWithScanCode)\n        {\n            virtualKey = 0;\n            flags |= KeyEventScanCode;\n        }''',
)

compat = "tests/iKeyd.Windows.Tests/LegacySendOutputCompatibilityTests.cs"
replace_once(compat, "new KeyboardKey(0x1C, 0x79)", "new KeyboardKey(0x1C, 0x79, PreserveVirtualKeyWithScanCode: true)")
replace_once(compat, "new KeyboardKey(0x1D, 0x7B)", "new KeyboardKey(0x1D, 0x7B, PreserveVirtualKeyWithScanCode: true)")
replace_once(compat, "new KeyboardKey(0xF3, 0x29)", "new KeyboardKey(0xF3, 0x29, PreserveVirtualKeyWithScanCode: true)")

keyboard_tests = "tests/iKeyd.Windows.Tests/WindowsKeyboardOutputTests.cs"
replace_once(
    keyboard_tests,
    '''    [Fact]\n    public void Extended_key_up_sets_extended_and_keyup_flags()''',
    '''    [Fact]\n    public void Explicit_vk_sc_output_preserves_both_identifiers_without_scan_code_mode()\n    {\n        var input = WindowsKeyboardOutput.BuildKeyInput(\n            new KeyboardKey(0xF3, 0x29, PreserveVirtualKeyWithScanCode: true),\n            KeyEventKind.Down);\n\n        Assert.Equal((ushort)0xF3, input.Data.Keyboard.VirtualKey);\n        Assert.Equal((ushort)0x29, input.Data.Keyboard.ScanCode);\n        Assert.Equal(0u, input.Data.Keyboard.Flags & 0x0008u);\n        Assert.Equal(WindowsKeyboardOutput.InjectionMarker, input.Data.Keyboard.ExtraInfo);\n    }\n\n    [Fact]\n    public void Extended_key_up_sets_extended_and_keyup_flags()''',
)

Path("tests/iKeyd.Windows.Tests/WindowsKeyboardOutputNativeIdentityTests.cs").write_text(
    r'''using System.ComponentModel;
using System.Runtime.InteropServices;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardOutputNativeIdentityTests
{
    [Fact]
    [Trait("Category", "WindowsE2E")]
    public void Explicit_vk_sc_identity_survives_real_SendInput_and_low_level_hook()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var capture = new OwnInjectionCapture();
        capture.Start();

        var output = new WindowsKeyboardOutput();
        output.SendKeyPress(new KeyboardKey(0xF3, 0x29, PreserveVirtualKeyWithScanCode: true));

        Assert.True(capture.WaitForCount(2, TimeSpan.FromSeconds(5)), "WH_KEYBOARD_LL did not observe the explicit vk/sc SendInput events.");
        var events = capture.Snapshot();
        Assert.Equal(2, events.Count);
        Assert.All(events, item => Assert.Equal((uint)0xF3, item.VirtualKey));
        Assert.All(events, item => Assert.Equal((uint)0x29, item.ScanCode));
        Assert.Equal("down", events[0].Kind);
        Assert.Equal("up", events[1].Kind);
    }

    private sealed class OwnInjectionCapture : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint WmQuit = 0x0012;
        private const uint WmKeyDown = 0x0100;
        private const uint WmKeyUp = 0x0101;
        private const uint WmSysKeyDown = 0x0104;
        private const uint WmSysKeyUp = 0x0105;
        private const uint PmNoRemove = 0;

        private readonly object _gate = new();
        private readonly HookProc _hookProc;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly ManualResetEventSlim _received = new(false);
        private readonly List<ObservedNativeKey> _events = [];
        private Thread? _thread;
        private uint _threadId;
        private nint _hook;
        private Exception? _startError;

        public OwnInjectionCapture() => _hookProc = HookCallback;

        public void Start()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "iKeyd.Tests.VKSC.NativeCapture"
            };
            _thread.Start();
            _started.Wait();
            if (_startError is not null)
                throw new InvalidOperationException("Could not start native vk/sc capture.", _startError);
        }

        public bool WaitForCount(int count, TimeSpan timeout)
        {
            lock (_gate)
            {
                if (_events.Count >= count)
                    return true;
            }
            return _received.Wait(timeout);
        }

        public IReadOnlyList<ObservedNativeKey> Snapshot()
        {
            lock (_gate)
                return _events.ToArray();
        }

        public void Dispose()
        {
            if (_thread is not null)
            {
                if (_threadId != 0)
                    NativeMethods.PostThreadMessageW(_threadId, WmQuit, 0, 0);
                if (!ReferenceEquals(Thread.CurrentThread, _thread))
                    _thread.Join(TimeSpan.FromSeconds(5));
            }
            _received.Dispose();
            _started.Dispose();
        }

        private void ThreadMain()
        {
            try
            {
                _threadId = NativeMethods.GetCurrentThreadId();
                NativeMethods.PeekMessageW(out _, 0, 0, 0, PmNoRemove);
                var module = NativeMethods.GetModuleHandleW(null);
                _hook = NativeMethods.SetWindowsHookExW(WhKeyboardLl, _hookProc, module, 0);
                if (_hook == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install native vk/sc hook.");
                _started.Set();

                while (true)
                {
                    var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                    if (result == 0)
                        break;
                    if (result == -1)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Native vk/sc capture message loop failed.");
                    NativeMethods.TranslateMessage(ref message);
                    NativeMethods.DispatchMessageW(ref message);
                }
            }
            catch (Exception error)
            {
                _startError = error;
                _started.Set();
            }
            finally
            {
                if (_hook != 0)
                    NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = 0;
                _threadId = 0;
            }
        }

        private nint HookCallback(int code, nuint wParam, nint lParam)
        {
            var down = wParam is WmKeyDown or WmSysKeyDown;
            var up = wParam is WmKeyUp or WmSysKeyUp;
            if (code >= 0 && (down || up))
            {
                var native = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if (native.ExtraInfo == WindowsKeyboardOutput.InjectionMarker)
                {
                    lock (_gate)
                    {
                        _events.Add(new ObservedNativeKey(
                            down ? "down" : "up",
                            native.VirtualKey,
                            native.ScanCode));
                        if (_events.Count >= 2)
                            _received.Set();
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    private readonly record struct ObservedNativeKey(string Kind, uint VirtualKey, uint ScanCode);
    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookExW(int hookId, HookProc callback, nint module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref NativeMessage message);
        [DllImport("user32.dll")]
        public static extern nint DispatchMessageW(ref NativeMessage message);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage, uint removeMessage);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandleW(string? moduleName);
    }
}
''',
    encoding="utf-8",
)
