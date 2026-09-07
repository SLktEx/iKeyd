using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class Jis109WindowsHookRuntimeE2ETests
{
    private const uint KeyEventKeyUp = 0x0002;
    private static readonly nuint ForeignMarker = (nuint)0x174710U;

    [Fact]
    [Trait("Category", "WindowsE2E")]
    public void Native_hook_timestamp_drives_isolated_number_and_function_timeout_output()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var configuration = GeneratedProfile.Create() with { StartupMode = InputMode.S };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var desktop = new NullDesktopBackend();
        var inputMethod = new ActiveKanaInputMethod();
        var send = new LegacySendOutput(output, desktop);
        using var runtime = new IKeydRuntimeHandler(
            configuration,
            inputMethod,
            keyboardState,
            send,
            desktop);
        using var router = new BehaviorWindowsInputRouter(
            configuration.Profile,
            () => runtime.Mode.Route(inputMethod).Keymap?.ToString(),
            send,
            output,
            runtime);
        using var hook = new WindowsKeyboardHook(keyboardState);
        var observer = new PhysicalSemanticsRouterObserver(router);
        hook.Start(observer);

        try
        {
            var digit = BindingFor(KeyCode.Digit1);
            Inject(digit, KeyEventKind.Down);
            Inject(digit, KeyEventKind.Up);

            Assert.True(observer.WaitForCount(2, TimeSpan.FromSeconds(5)), "Digit1 did not reach the native hook/runtime path.");
            Assert.All(observer.Snapshot().Take(2), item => Assert.Equal(KeyboardDisposition.Suppress, item.Disposition));
            Assert.True(output.WaitForKeyCount(2, TimeSpan.FromSeconds(2)), "Digit1 did not emit after the native hook timestamp timeout.");

            var f1 = BindingFor(KeyCode.F1);
            Inject(f1, KeyEventKind.Down);
            Inject(f1, KeyEventKind.Up);

            Assert.True(observer.WaitForCount(4, TimeSpan.FromSeconds(5)), "F1 did not reach the native hook/runtime path.");
            Assert.All(observer.Snapshot().Skip(2).Take(2), item => Assert.Equal(KeyboardDisposition.Suppress, item.Disposition));
            Assert.True(output.WaitForKeyCount(4, TimeSpan.FromSeconds(2)), "F1 did not emit after the native hook timestamp timeout.");

            Assert.True(WindowsKeyMap.TryResolveOutputKey(new KeyId(KeyCode.Digit1), out var digitOutput));
            Assert.True(WindowsKeyMap.TryResolveOutputKey(new KeyId(KeyCode.F1), out var f1Output));
            Assert.Equal(
                [
                    new RecordedKeyboardEvent(digitOutput, KeyEventKind.Down),
                    new RecordedKeyboardEvent(digitOutput, KeyEventKind.Up),
                    new RecordedKeyboardEvent(f1Output, KeyEventKind.Down),
                    new RecordedKeyboardEvent(f1Output, KeyEventKind.Up)
                ],
                output.SnapshotKeys());

            var observed = observer.Snapshot();
            Assert.Equal(digit.WindowsKey.ScanCode, observed[0].HookEvent.Key.ScanCode);
            Assert.Equal(f1.WindowsKey.ScanCode, observed[2].HookEvent.Key.ScanCode);
            Assert.All(observed, item => Assert.Equal(KeyEventOrigin.Injected, item.HookEvent.Origin));

            // The production hook expands KBDLLHOOKSTRUCT.time into TickCount64.
            // Pin that these tests actually exercise that native-timestamp path
            // rather than supplying the runtime timestamp directly.
            var now = Environment.TickCount64;
            Assert.All(observed, item => Assert.InRange(Math.Abs(now - item.HookEvent.TimestampMs), 0, 10_000));
        }
        finally
        {
            hook.Stop();
        }
    }

    private static WindowsPhysicalKeyBinding BindingFor(KeyCode code)
        => WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == code);

    private static void Inject(WindowsPhysicalKeyBinding binding, KeyEventKind kind)
    {
        var flags = kind == KeyEventKind.Up ? KeyEventKeyUp : 0U;
        NativeMethods.keybd_event(
            checked((byte)binding.WindowsKey.VirtualKey),
            checked((byte)binding.WindowsKey.ScanCode),
            flags,
            ForeignMarker);
    }

    private sealed class PhysicalSemanticsRouterObserver : IKeyboardEventHandler, IInputStateResettable
    {
        private readonly BehaviorWindowsInputRouter _router;
        private readonly object _gate = new();
        private readonly List<ObservedRuntimeEvent> _events = [];

        public PhysicalSemanticsRouterObserver(BehaviorWindowsInputRouter router)
            => _router = router;

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            var physical = keyboardEvent with { Origin = KeyEventOrigin.Physical };
            var disposition = _router.OnKeyboardEvent(physical);
            lock (_gate)
                _events.Add(new ObservedRuntimeEvent(keyboardEvent, disposition));

            // Verification input is injected only to exercise the real user32 hook.
            // Never let it escape into the runner's foreground application.
            return KeyboardDisposition.Suppress;
        }

        public void ResetInputState() => _router.ResetInputState();

        public bool WaitForCount(int expected, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                lock (_gate)
                {
                    if (_events.Count >= expected)
                        return true;
                }
                Thread.Sleep(5);
            }

            lock (_gate)
                return _events.Count >= expected;
        }

        public ObservedRuntimeEvent[] Snapshot()
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly object _gate = new();
        private readonly List<RecordedKeyboardEvent> _keys = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
        {
            lock (_gate)
                _keys.Add(new RecordedKeyboardEvent(key, kind));
        }

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;

        public bool WaitForKeyCount(int expected, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                lock (_gate)
                {
                    if (_keys.Count >= expected)
                        return true;
                }
                Thread.Sleep(5);
            }

            lock (_gate)
                return _keys.Count >= expected;
        }

        public RecordedKeyboardEvent[] SnapshotKeys()
        {
            lock (_gate)
                return _keys.ToArray();
        }
    }

    private sealed class ActiveKanaInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => true;
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "Jis109WindowsHookRuntimeE2E";
        public bool IsWindow(WindowHandle window) => window == _window;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [_window];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }

    private readonly record struct ObservedRuntimeEvent(
        KeyboardEvent HookEvent,
        KeyboardDisposition Disposition);

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
