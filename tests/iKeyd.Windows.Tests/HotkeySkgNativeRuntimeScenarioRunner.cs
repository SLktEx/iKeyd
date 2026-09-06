using System.ComponentModel;
using System.Runtime.InteropServices;
using iKeyd.App;
using iKeyd.Compatibility.Tests;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Runs the full hotkeySKG runtime with the real WindowsKeyboardOutput and
/// observes iKeyd's own SendInput events through WH_KEYBOARD_LL.
/// </summary>
public sealed class HotkeySkgNativeRuntimeScenarioRunner : ICompatibilityScenarioRunner
{
    public string Name => "iKeyd.hotkeySKG-native-runtime";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The native hotkeySKG runner requires Windows.");
        if (scenario.InitialState.Modifiers.Count != 0)
            throw new NotSupportedException("Native runtime scenarios must express modifiers as input events.");

        var configPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
        var configuration = IKeydConfiguration.Load(configPath);
        var keyboardState = new KeyboardState();
        var output = new WindowsKeyboardOutput();
        var desktop = new NoOpDesktopBackend();
        var send = new LegacySendOutput(output, desktop);

        using var capture = new OwnNativeEventCapture();
        capture.Start();
        using var runtime = new IKeydRuntimeHandler(
            configuration,
            new HostedKanaInputMethod(),
            keyboardState,
            send,
            desktop);

        if (!Enum.TryParse<InputMode>(scenario.InitialState.Mode, ignoreCase: true, out var mode))
            throw new NotSupportedException($"Unsupported native runtime scenario mode '{scenario.InitialState.Mode}'.");
        runtime.SetMode(mode);

        foreach (var input in scenario.Input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var virtualKey = ScenarioKeyboard.ResolveVirtualKey(input.Key!);
            var kind = string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase)
                ? KeyEventKind.Up
                : KeyEventKind.Down;
            var keyboardEvent = new KeyboardEvent(
                new KeyboardKey(virtualKey, 0, ScenarioKeyboard.IsExtended(virtualKey)),
                kind,
                KeyEventOrigin.Physical,
                input.AtMs);
            keyboardState.Apply(keyboardEvent);
            _ = runtime.OnKeyboardEvent(keyboardEvent);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(75), cancellationToken);
        return new ScenarioRunResult
        {
            Runner = Name,
            ScenarioId = scenario.Id,
            Text = string.Empty,
            Events = capture.Events.ToList(),
            Metadata = new Dictionary<string, string>
            {
                ["mode"] = scenario.InitialState.Mode,
                ["scope"] = "full-runtime-real-sendinput-native-vk-scan"
            }
        };
    }

    private sealed class HostedKanaInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => true;
    }

    private sealed class NoOpDesktopBackend : IDesktopBackend
    {
        private static readonly WindowHandle Active = new(1);
        private readonly HashSet<DesktopMouseButton> _buttons = [];
        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 1920, 1080);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "NativeRuntimeScenarioWindow";
        public bool IsWindow(WindowHandle window) => !window.IsEmpty;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => _buttons.Contains(button);
        public void SetMouseButton(DesktopMouseButton button, bool down)
        {
            if (down) _buttons.Add(button); else _buttons.Remove(button);
        }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }

    private sealed class OwnNativeEventCapture : IDisposable
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
        private readonly List<ObservedKeyEvent> _events = [];
        private Thread? _thread;
        private uint _threadId;
        private nint _hook;
        private Exception? _startError;

        public OwnNativeEventCapture() => _hookProc = HookCallback;

        public IReadOnlyList<ObservedKeyEvent> Events
        {
            get { lock (_gate) return _events.ToArray(); }
        }

        public void Start()
        {
            _thread = new Thread(ThreadMain) { IsBackground = true, Name = "iKeyd.NativeOutputCapture" };
            _thread.Start();
            _started.Wait();
            if (_startError is not null)
                throw new InvalidOperationException("Could not start iKeyd native output capture.", _startError);
        }

        public void Dispose()
        {
            var thread = _thread;
            if (thread is not null)
            {
                if (_threadId != 0)
                    NativeMethods.PostThreadMessageW(_threadId, WmQuit, 0, 0);
                if (!ReferenceEquals(Thread.CurrentThread, thread))
                    thread.Join(TimeSpan.FromSeconds(5));
            }
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install iKeyd native output hook.");
                _started.Set();
                while (true)
                {
                    var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                    if (result == 0) break;
                    if (result == -1)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "iKeyd native output message loop failed.");
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
                if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = 0;
                _threadId = 0;
            }
        }

        private nint HookCallback(int code, nuint wParam, nint lParam)
        {
            var isDown = wParam == (nuint)WmKeyDown || wParam == (nuint)WmSysKeyDown;
            var isUp = wParam == (nuint)WmKeyUp || wParam == (nuint)WmSysKeyUp;
            if (code >= 0 && (isDown || isUp))
            {
                var native = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if (native.ExtraInfo == WindowsKeyboardOutput.InjectionMarker)
                {
                    lock (_gate)
                    {
                        _events.Add(new ObservedKeyEvent
                        {
                            Kind = isDown ? "keyDown" : "keyUp",
                            Key = $"VK{native.VirtualKey:X2}SC{native.ScanCode:X3}"
                        });
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        private delegate nint HookProc(int code, nuint wParam, nint lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public NativePoint Point; public uint Private; }
        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookExW(int hookId, HookProc callback, nint module, uint threadId);
            [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(nint hook);
            [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
            [DllImport("user32.dll", SetLastError = true)] public static extern int GetMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool TranslateMessage(ref NativeMessage message);
            [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref NativeMessage message);
            [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PeekMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage, uint removeMessage);
            [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? moduleName);
        }
    }
}
