using System.Diagnostics;
using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class Jis109RuntimeSurfaceAuditTests
{
    public static IEnumerable<object[]> NumberAndFunctionRowKeys()
    {
        for (var digit = 0; digit <= 9; digit++)
            yield return [new KeyId((KeyCode)((int)KeyCode.Digit0 + digit)).Code];

        for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
            yield return [new KeyId((KeyCode)((int)KeyCode.F1 + functionNumber - 1)).Code];
    }

    [Fact]
    public void Canonical_JIS109_names_Windows_bindings_and_output_keys_are_symmetric()
    {
        Assert.Equal(109, Jis109PhysicalKeyRegistry.Keys.Count);
        Assert.Equal(109, WindowsKeyMap.Jis109PhysicalBindings.Count);

        foreach (var physical in Jis109PhysicalKeyRegistry.Keys)
        {
            Assert.True(KeyId.TryParseCompact(physical.Name, out var parsed));
            Assert.Equal(physical.Code, parsed);

            var binding = BindingFor(physical.Code);
            Assert.NotEqual((ushort)0, binding.WindowsKey.ScanCode);
            Assert.Equal(physical.Code, WindowsKeyMap.TryResolveKeyId(binding.WindowsKey)?.Code);

            Assert.True(WindowsKeyMap.TryResolveOutputKey(new KeyId(physical.Code), out var output));
            Assert.Equal(physical.Code, WindowsKeyMap.TryResolveKeyId(output)?.Code);
        }
    }

    [Theory]
    [MemberData(nameof(NumberAndFunctionRowKeys))]
    public void Number_and_S_function_rows_survive_full_router_from_real_scan_to_timeout_output(KeyCode code)
    {
        using var fixture = CreateRuntime(InputMode.S, kanaInputActive: true);
        var binding = BindingFor(code);
        var now = Environment.TickCount64;

        Assert.NotEqual((ushort)0, binding.WindowsKey.ScanCode);
        Assert.Equal(
            KeyboardDisposition.Suppress,
            Dispatch(fixture, binding.WindowsKey, KeyEventKind.Down, now));
        Assert.Equal(
            KeyboardDisposition.Suppress,
            Dispatch(fixture, binding.WindowsKey, KeyEventKind.Up, now + 1));

        Assert.True(
            fixture.Output.WaitForKeyCount(2, TimeSpan.FromSeconds(2)),
            $"{code} did not emit after the real chord timeout path.");

        Assert.True(WindowsKeyMap.TryResolveOutputKey(new KeyId(code), out var expectedOutput));
        Assert.Equal(
            [
                new RecordedKeyboardEvent(expectedOutput, KeyEventKind.Down),
                new RecordedKeyboardEvent(expectedOutput, KeyEventKind.Up)
            ],
            fixture.Output.SnapshotKeys());
        Assert.Empty(fixture.Output.SnapshotText());
    }

    [Fact]
    public void K_function_row_stays_transparent_through_full_router_with_real_scan_identity()
    {
        using var fixture = CreateRuntime(InputMode.K, kanaInputActive: true);
        var timestamp = Environment.TickCount64;

        for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
        {
            var code = (KeyCode)((int)KeyCode.F1 + functionNumber - 1);
            var binding = BindingFor(code);

            Assert.Equal(
                KeyboardDisposition.PassThrough,
                Dispatch(fixture, binding.WindowsKey, KeyEventKind.Down, timestamp++));
            Assert.Equal(
                KeyboardDisposition.PassThrough,
                Dispatch(fixture, binding.WindowsKey, KeyEventKind.Up, timestamp++));
        }

        Assert.Empty(fixture.Output.SnapshotKeys());
        Assert.Empty(fixture.Output.SnapshotText());
    }

    [Fact]
    public void Known_unmapped_navigation_system_and_numpad_keys_are_not_accidentally_swallowed()
    {
        using var fixture = CreateRuntime(InputMode.S, kanaInputActive: true);
        var codes = new[]
        {
            KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
            KeyCode.Left, KeyCode.Up, KeyCode.Right, KeyCode.Down,
            KeyCode.LeftWin, KeyCode.RightWin, KeyCode.Apps, KeyCode.PrintScreen, KeyCode.ScrollLock, KeyCode.Pause,
            KeyCode.NumLock, KeyCode.Numpad0, KeyCode.Numpad1, KeyCode.Numpad2, KeyCode.Numpad3,
            KeyCode.Numpad4, KeyCode.Numpad5, KeyCode.Numpad6, KeyCode.Numpad7, KeyCode.Numpad8, KeyCode.Numpad9,
            KeyCode.NumpadDecimal, KeyCode.NumpadDivide, KeyCode.NumpadMultiply, KeyCode.NumpadSubtract,
            KeyCode.NumpadAdd, KeyCode.NumpadEnter
        };
        var timestamp = Environment.TickCount64;

        foreach (var code in codes)
        {
            var binding = BindingFor(code);
            Assert.Equal(
                KeyboardDisposition.PassThrough,
                Dispatch(fixture, binding.WindowsKey, KeyEventKind.Down, timestamp++));
            Assert.Equal(
                KeyboardDisposition.PassThrough,
                Dispatch(fixture, binding.WindowsKey, KeyEventKind.Up, timestamp++));
        }

        Assert.Empty(fixture.Output.SnapshotKeys());
        Assert.Empty(fixture.Output.SnapshotText());
    }

    private static WindowsPhysicalKeyBinding BindingFor(KeyCode code)
        => WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == code);

    private static RuntimeFixture CreateRuntime(InputMode startupMode, bool kanaInputActive)
    {
        var configuration = GeneratedProfile.Create() with { StartupMode = startupMode };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var desktop = new NullDesktopBackend();
        var inputMethod = new FixedInputMethod(kanaInputActive);
        var send = new LegacySendOutput(output, desktop);
        var runtime = new IKeydRuntimeHandler(
            configuration,
            inputMethod,
            keyboardState,
            send,
            desktop);
        var router = new BehaviorWindowsInputRouter(
            configuration.Profile,
            () => runtime.Mode.Route(inputMethod).Keymap?.ToString(),
            send,
            output,
            runtime);

        return new RuntimeFixture(runtime, router, keyboardState, output);
    }

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        KeyboardKey key,
        KeyEventKind kind,
        long timestampMs)
    {
        var keyboardEvent = new KeyboardEvent(key, kind, KeyEventOrigin.Physical, timestampMs);
        fixture.KeyboardState.Apply(keyboardEvent);
        return fixture.Router.OnKeyboardEvent(keyboardEvent);
    }

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        BehaviorWindowsInputRouter Router,
        KeyboardState KeyboardState,
        RecordingKeyboardOutput Output) : IDisposable
    {
        public void Dispose()
        {
            Router.Dispose();
            Runtime.Dispose();
        }
    }

    private sealed class FixedInputMethod(bool kanaInputActive) : IInputMethod
    {
        public bool IsKanaInputActive() => kanaInputActive;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly object _gate = new();
        private readonly List<RecordedKeyboardEvent> _keys = [];
        private readonly List<string> _text = [];

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

        public void SendText(string text)
        {
            lock (_gate)
                _text.Add(text);
        }

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

        public string[] SnapshotText()
        {
            lock (_gate)
                return _text.ToArray();
        }
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "Jis109RuntimeSurfaceAudit";
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

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);
}
