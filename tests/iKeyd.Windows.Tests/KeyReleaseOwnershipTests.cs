using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class KeyReleaseOwnershipTests
{
    [Fact]
    public void Layer_release_before_typematic_repeat_keeps_the_downstream_keyup()
    {
        using var fixture = new RuntimeFixture();

        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.Space));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.A));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.Space));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(KeyCode.A));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.A));

        Assert.Empty(fixture.Output.DownKeys);
    }

    [Fact]
    public void Remapped_repeat_does_not_steal_release_from_an_earlier_passthrough_down()
    {
        using var fixture = new RuntimeFixture();

        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(KeyCode.J));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.NonConvert));
        // M+J emits Left, so no injected J-up can release the earlier physical J.
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.J));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.J));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.NonConvert));

        Assert.Empty(fixture.Output.DownKeys);
    }

    [Fact]
    public void Reentering_a_layer_after_passthrough_repeat_still_keeps_the_downstream_keyup()
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(KeyCode.NonConvert);
        fixture.Down(KeyCode.J);
        fixture.Up(KeyCode.NonConvert);
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(KeyCode.J));
        fixture.Down(KeyCode.NonConvert);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.J));
        fixture.Up(KeyCode.NonConvert);
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.J));

        Assert.Empty(fixture.Output.DownKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entirely_remapped_hold_still_suppresses_release_in_either_release_order(bool layerFirst)
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(KeyCode.Space);
        fixture.Down(KeyCode.A);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.A));
        if (layerFirst)
            Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.Space));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.A));
        if (!layerFirst)
            Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.Space));

        Assert.Empty(fixture.Output.DownKeys);
    }

    [Fact]
    public void New_hold_does_not_inherit_previous_passthrough_release_ownership()
    {
        using var fixture = new RuntimeFixture();

        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(KeyCode.A));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.A));
        fixture.Down(KeyCode.Space);
        fixture.Down(KeyCode.A);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.A));
        fixture.Up(KeyCode.Space);

        Assert.Empty(fixture.Output.DownKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_or_mode_change_while_held_preserves_downstream_release_ownership(bool changeMode)
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(KeyCode.J);
        if (changeMode)
        {
            fixture.SetMode(InputMode.S);
            fixture.SetMode(InputMode.T);
        }
        else
        {
            fixture.Reset();
            fixture.Down(KeyCode.NonConvert);
        }
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.J));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.J));
        if (changeMode)
            fixture.SetMode(InputMode.R);
        else
            fixture.Up(KeyCode.NonConvert);

        Assert.Empty(fixture.Output.DownKeys);
    }

    [Fact]
    public void Passthrough_release_tracking_is_allocation_free_after_warmup()
    {
        const int MeasurementIterations = 10_000;
        const int MaxWarmupWindows = 6;
        const int RequiredStableWindows = 2;
        using var fixture = new RuntimeFixture();
        var key = WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == KeyCode.A).WindowsKey;
        for (var index = 0; index < 100; index++)
        {
            fixture.Dispatch(key, KeyEventKind.Down);
            fixture.Dispatch(key, KeyEventKind.Up);
        }

        // Match the existing allocation tests' bounded steady-state windows so
        // tiered JIT/PGO startup is not mistaken for per-event allocation.
        var stableWindows = 0;
        long lastAllocated = long.MaxValue;
        for (var attempt = 0; attempt < MaxWarmupWindows; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < MeasurementIterations; index++)
            {
                fixture.Dispatch(key, KeyEventKind.Down);
                fixture.Dispatch(key, KeyEventKind.Up);
            }
            lastAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
            stableWindows = lastAllocated == 0 ? stableWindows + 1 : 0;
            if (stableWindows >= RequiredStableWindows)
                break;
        }

        Assert.True(
            stableWindows >= RequiredStableWindows,
            $"Release tracking did not reach steady-state zero allocation; last window allocated {lastAllocated} bytes.");
        Assert.Empty(fixture.Output.DownKeys);
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly KeyboardState _state = new();
        private readonly IKeydRuntimeHandler _runtime;
        private readonly BehaviorWindowsInputRouter _router;
        private long _timestamp;

        public RuntimeFixture()
        {
            var configuration = GeneratedProfile.Create() with { StartupMode = InputMode.R };
            var inputMethod = new FixedInputMethod();
            var send = new LegacySendOutput(Output);
            _runtime = new IKeydRuntimeHandler(configuration, inputMethod, _state, send, new NullDesktopBackend());
            _router = new BehaviorWindowsInputRouter(
                configuration.Profile,
                () => _runtime.Mode.Route(inputMethod).Keymap?.ToString(),
                send,
                Output,
                _runtime);
        }

        public RecordingKeyboardOutput Output { get; } = new();
        public KeyboardDisposition Down(KeyCode code) => Dispatch(code, KeyEventKind.Down);
        public KeyboardDisposition Up(KeyCode code) => Dispatch(code, KeyEventKind.Up);
        public void SetMode(InputMode mode) => _runtime.SetMode(mode);
        public void Reset() => _router.ResetInputState();

        private KeyboardDisposition Dispatch(KeyCode code, KeyEventKind kind)
        {
            var key = WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == code).WindowsKey;
            return Dispatch(key, kind);
        }

        public KeyboardDisposition Dispatch(KeyboardKey key, KeyEventKind kind)
        {
            var keyboardEvent = new KeyboardEvent(key, kind, KeyEventOrigin.Physical, _timestamp++);
            _state.Apply(keyboardEvent);
            var disposition = _router.OnKeyboardEvent(keyboardEvent);
            if (disposition == KeyboardDisposition.PassThrough)
                Output.SendKey(key, kind);
            return disposition;
        }

        public void Dispose()
        {
            _router.Dispose();
            _runtime.Dispose();
        }
    }

    private sealed class FixedInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    // Models the downstream held VK state from both injected output and physical
    // events allowed through the router. It does not simulate a native Windows hook.
    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public HashSet<ushort> DownKeys { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind)
        {
            if (kind == KeyEventKind.Down)
                DownKeys.Add(key.VirtualKey);
            else
                DownKeys.Remove(key.VirtualKey);
        }
        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        public WindowHandle GetActiveWindow() => default;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => default;
        public DesktopRect GetPrimaryWorkArea() => default;
        public string? GetWindowClass(WindowHandle window) => null;
        public bool IsWindow(WindowHandle window) => false;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [];
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
}
