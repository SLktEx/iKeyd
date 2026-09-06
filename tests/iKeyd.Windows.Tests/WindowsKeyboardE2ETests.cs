using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardE2ETests
{
    private const byte VkNonConvert = 0x1D;
    private const byte NonConvertScanCode = 0x7B;
    private const byte VkQ = 0x51;
    private const byte VkF24 = 0x87;
    private const uint KeyEventKeyUp = 0x0002;
    private static readonly nuint ForeignMarker = (nuint)0x13572468U;
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    [Trait("Category", "WindowsE2E")]
    public void Low_level_hook_receives_external_injected_input_and_ignores_own_SendInput()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var hook = new WindowsKeyboardHook();
        var handler = new RecordingHandler(VkF24, expectedEvents: 2);
        hook.Start(handler);

        try
        {
            // Exercise the real user32 -> WH_KEYBOARD_LL path with injected input that
            // does not carry iKeyd's marker. The hook should observe and suppress it.
            NativeMethods.keybd_event(VkF24, 0, 0, ForeignMarker);
            NativeMethods.keybd_event(VkF24, 0, KeyEventKeyUp, ForeignMarker);

            Assert.True(handler.Wait(TimeSpan.FromSeconds(5)), "WH_KEYBOARD_LL did not receive the injected F24 events.");

            var events = handler.Snapshot();
            Assert.Equal(2, events.Count);
            Assert.Equal(KeyEventKind.Down, events[0].Kind);
            Assert.Equal(KeyEventKind.Up, events[1].Kind);
            Assert.All(events, e => Assert.Equal(KeyEventOrigin.Injected, e.Origin));
            Assert.All(events, e => Assert.Equal(VkF24, e.Key.VirtualKey));

            // Now use iKeyd's actual SendInput implementation. These events travel
            // through Windows too, but carry InjectionMarker and must not be sent
            // back to the application handler.
            var beforeOwnInjection = handler.Count;
            var output = new WindowsKeyboardOutput();
            output.SendKeyPress(new KeyboardKey(VkF24, 0));

            Thread.Sleep(300);
            Assert.Equal(beforeOwnInjection, handler.Count);
        }
        finally
        {
            hook.Stop();
        }
    }

    [Fact]
    [Trait("Category", "WindowsE2E")]
    public void Panic_reset_clears_real_hook_layer_state_before_late_keyup()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var keyboardState = new KeyboardState();
        using var hook = new WindowsKeyboardHook(keyboardState);
        using var runtime = new IKeydRuntimeHandler(
            IKeydConfiguration.Load(ProfilePath) with { StartupMode = InputMode.R },
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(new NullKeyboardOutput()),
            new NullDesktopBackend());
        var handler = new SuppressingRuntimeObserver(runtime);
        hook.Start(handler);

        try
        {
            // Materialize a real M-layer down event through WH_KEYBOARD_LL.
            NativeMethods.keybd_event(VkNonConvert, NonConvertScanCode, 0, ForeignMarker);
            Assert.True(handler.WaitForCount(1, TimeSpan.FromSeconds(5)), "NonConvert down did not reach the real hook/runtime path.");
            Assert.Equal(KeyboardDisposition.Suppress, handler.Snapshot()[0].Disposition);

            // Simulate the tray/panic recovery while the physical key is still down.
            runtime.ResetInputState();

            // A late physical release must be absorbed without reopening/sticking M.
            NativeMethods.keybd_event(VkNonConvert, NonConvertScanCode, KeyEventKeyUp, ForeignMarker);
            Assert.True(handler.WaitForCount(2, TimeSpan.FromSeconds(5)), "Late NonConvert keyup did not reach the runtime.");
            Assert.Equal(KeyboardDisposition.Suppress, handler.Snapshot()[1].Disposition);

            // Q immediately after recovery must be ordinary R-mode pass-through. The
            // observer suppresses the test event at the outer hook boundary so no Q
            // is typed into the user's foreground application.
            NativeMethods.keybd_event(VkQ, 0, 0, ForeignMarker);
            NativeMethods.keybd_event(VkQ, 0, KeyEventKeyUp, ForeignMarker);
            Assert.True(handler.WaitForCount(4, TimeSpan.FromSeconds(5)), "Post-reset Q events did not reach the runtime.");

            var observed = handler.Snapshot();
            Assert.Equal(VkQ, observed[2].Event.Key.VirtualKey);
            Assert.Equal(KeyEventKind.Down, observed[2].Event.Kind);
            Assert.Equal(KeyboardDisposition.PassThrough, observed[2].Disposition);
            Assert.Equal(VkQ, observed[3].Event.Key.VirtualKey);
            Assert.Equal(KeyEventKind.Up, observed[3].Event.Kind);
            Assert.Equal(KeyboardDisposition.PassThrough, observed[3].Disposition);
        }
        finally
        {
            hook.Stop();
        }
    }

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        private readonly ushort _virtualKey;
        private readonly int _expectedEvents;
        private readonly ManualResetEventSlim _received = new(false);
        private readonly object _gate = new();
        private readonly List<KeyboardEvent> _events = [];

        public RecordingHandler(ushort virtualKey, int expectedEvents)
        {
            _virtualKey = virtualKey;
            _expectedEvents = expectedEvents;
        }

        public int Count
        {
            get
            {
                lock (_gate)
                    return _events.Count;
            }
        }

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            if (keyboardEvent.Key.VirtualKey != _virtualKey)
                return KeyboardDisposition.PassThrough;

            lock (_gate)
            {
                _events.Add(keyboardEvent);
                if (_events.Count >= _expectedEvents)
                    _received.Set();
            }

            return KeyboardDisposition.Suppress;
        }

        public bool Wait(TimeSpan timeout) => _received.Wait(timeout);

        public IReadOnlyList<KeyboardEvent> Snapshot()
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    private sealed class SuppressingRuntimeObserver : IKeyboardEventHandler, IInputStateResettable
    {
        private readonly IKeydRuntimeHandler _runtime;
        private readonly object _gate = new();
        private readonly List<ObservedRuntimeEvent> _events = [];

        public SuppressingRuntimeObserver(IKeydRuntimeHandler runtime)
            => _runtime = runtime;

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            var disposition = _runtime.OnKeyboardEvent(keyboardEvent);
            lock (_gate)
                _events.Add(new ObservedRuntimeEvent(keyboardEvent, disposition));

            // Never allow the injected verification keys to reach the foreground app.
            return KeyboardDisposition.Suppress;
        }

        public void ResetInputState() => _runtime.ResetInputState();

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
                Thread.Sleep(10);
            }

            lock (_gate)
                return _events.Count >= expected;
        }

        public IReadOnlyList<ObservedRuntimeEvent> Snapshot()
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    private sealed class InactiveInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    private sealed class NullKeyboardOutput : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);

        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "WindowsKeyboardE2E";
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
        KeyboardEvent Event,
        KeyboardDisposition Disposition);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
