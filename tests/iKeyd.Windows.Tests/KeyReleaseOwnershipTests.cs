using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Remapped_repeat_preserves_release_after_a_failed_down_passes_through(bool layerFirst)
    {
        using var fixture = new RuntimeFixture();
        fixture.Output.Events = [];
        var failure = new InvalidOperationException("Simulated output failure.");

        fixture.Down(KeyCode.NonConvert);
        fixture.Output.NextKeyPressFailure = failure;
        // WindowsKeyboardHook resets the handler and passes this physical down
        // through after an exception. Model that contract without a native hook.
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.DownWithHookRecovery(KeyCode.J));
        Assert.Same(failure, fixture.HookFailure);
        Assert.Contains((ushort)'J', fixture.Output.DownKeys);
        fixture.Up(KeyCode.NonConvert);

        fixture.Down(KeyCode.NonConvert);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.J));
        if (layerFirst)
            fixture.Up(KeyCode.NonConvert);
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.J));
        if (!layerFirst)
            fixture.Up(KeyCode.NonConvert);
        Assert.Empty(fixture.Output.DownKeys);

        // The recovery ownership ends at key-up and must not leak to a new hold.
        fixture.Down(KeyCode.NonConvert);
        fixture.Down(KeyCode.J);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.J));
        fixture.Up(KeyCode.NonConvert);
        Assert.Empty(fixture.Output.DownKeys);
        Assert.Single(fixture.Output.Events, e => e.Key.VirtualKey == 'J' && e.Kind == KeyEventKind.Up);
    }

    [Fact]
    public void Failed_keyup_clears_recovery_ownership_before_the_next_hold()
    {
        // Keep pending chord output deterministic; no timer should flush it first.
        using var fixture = new RuntimeFixture(chordWindowMs: 60_000);
        fixture.Output.Events = [];
        fixture.SetMode(InputMode.S);
        fixture.SetMode(InputMode.T);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.J));
        var downFailure = new InvalidOperationException("Pending output failed.");
        fixture.Output.NextKeyPressFailure = downFailure;
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.DownWithHookRecovery(KeyCode.Space));
        Assert.Same(downFailure, fixture.HookFailure);

        fixture.SetMode(InputMode.R);
        fixture.Down(KeyCode.Space);
        var upFailure = new InvalidOperationException("Layer tap output failed.");
        fixture.Output.NextKeyPressFailure = upFailure;
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.UpWithHookRecovery(KeyCode.Space));
        Assert.Same(upFailure, fixture.HookFailure);

        fixture.Down(KeyCode.Space);
        fixture.Down(KeyCode.A);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.Space));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.A));
        fixture.Up(KeyCode.J);
        Assert.Single(fixture.Output.Events, e => e.Key.VirtualKey == WindowsKeyMap.Space && e.Kind == KeyEventKind.Up);
        Assert.Empty(fixture.Output.DownKeys);
    }

    [Theory]
    [InlineData(KeyEventOrigin.Injected)]
    [InlineData(KeyEventOrigin.OwnInjected)]
    public void Injected_down_does_not_create_physical_release_ownership(KeyEventOrigin origin)
    {
        using var fixture = new RuntimeFixture();
        fixture.Output.Events = [];
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Injected(KeyCode.J, KeyEventKind.Down, origin));
        fixture.Down(KeyCode.NonConvert);
        fixture.Down(KeyCode.J);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Up(KeyCode.J));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Injected(KeyCode.J, KeyEventKind.Up, origin));
        fixture.Up(KeyCode.NonConvert);
        Assert.DoesNotContain(fixture.Output.Events, e => e.Key.VirtualKey == 'J');
        Assert.Empty(fixture.Output.DownKeys);
    }

    [Theory]
    [InlineData(KeyEventOrigin.Injected, false)]
    [InlineData(KeyEventOrigin.OwnInjected, false)]
    [InlineData(KeyEventOrigin.Injected, true)]
    [InlineData(KeyEventOrigin.OwnInjected, true)]
    public void Injected_up_does_not_clear_physical_release_ownership(KeyEventOrigin origin, bool recover)
    {
        using var fixture = new RuntimeFixture();
        fixture.Output.Events = [];
        if (recover)
        {
            fixture.Down(KeyCode.NonConvert);
            fixture.Output.NextKeyPressFailure = new InvalidOperationException("Simulated output failure.");
            Assert.Equal(KeyboardDisposition.PassThrough, fixture.DownWithHookRecovery(KeyCode.J));
            fixture.Up(KeyCode.NonConvert);
        }
        else
        {
            Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(KeyCode.J));
        }
        fixture.Down(KeyCode.NonConvert);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(KeyCode.J));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Injected(KeyCode.J, KeyEventKind.Up, origin));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.J));
        fixture.Up(KeyCode.NonConvert);
        Assert.Single(fixture.Output.Events, e => e.Key.VirtualKey == 'J' && e.Kind == KeyEventKind.Up);
        Assert.Empty(fixture.Output.DownKeys);
    }

    [Fact]
    public void Ordinary_passthrough_repeats_deliver_exactly_one_keyup()
    {
        using var fixture = new RuntimeFixture();
        fixture.Output.Events = [];
        for (var repeat = 0; repeat < 3; repeat++)
            Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(KeyCode.J));
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Up(KeyCode.J));
        Assert.Single(fixture.Output.Events, e => e.Key.VirtualKey == 'J' && e.Kind == KeyEventKind.Up);
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

        public RuntimeFixture(int? chordWindowMs = null)
        {
            var configuration = GeneratedProfile.Create() with { StartupMode = InputMode.R };
            if (chordWindowMs is { } window)
                configuration = configuration with { Profile = new AutomationProfile(window, configuration.Profile.Keymaps.Values) };
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
        public Exception? HookFailure { get; private set; }
        public KeyboardDisposition Down(KeyCode code) => Dispatch(code, KeyEventKind.Down);
        public KeyboardDisposition DownWithHookRecovery(KeyCode code) => Dispatch(code, KeyEventKind.Down, recover: true);
        public KeyboardDisposition UpWithHookRecovery(KeyCode code) => Dispatch(code, KeyEventKind.Up, recover: true);
        public KeyboardDisposition Up(KeyCode code) => Dispatch(code, KeyEventKind.Up);
        public void SetMode(InputMode mode) => _runtime.SetMode(mode);
        public void Reset() => _router.ResetInputState();

        public KeyboardDisposition Injected(KeyCode code, KeyEventKind kind, KeyEventOrigin origin)
        {
            var key = WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == code).WindowsKey;
            var keyboardEvent = new KeyboardEvent(key, kind, origin, _timestamp++);
            if (origin != KeyEventOrigin.OwnInjected)
                _state.Apply(keyboardEvent);
            // Deliver only to the routing code: injected events already exist
            // downstream and must not be re-emitted by the handler under test.
            return _router.OnKeyboardEvent(keyboardEvent);
        }

        private KeyboardDisposition Dispatch(KeyCode code, KeyEventKind kind, bool recover = false)
        {
            var key = WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == code).WindowsKey;
            return Dispatch(key, kind, recover);
        }

        public KeyboardDisposition Dispatch(KeyboardKey key, KeyEventKind kind, bool recover = false)
        {
            var keyboardEvent = new KeyboardEvent(key, kind, KeyEventOrigin.Physical, _timestamp++);
            _state.Apply(keyboardEvent);
            KeyboardDisposition disposition;
            try
            {
                disposition = _router.OnKeyboardEvent(keyboardEvent);
            }
            catch (Exception error) when (recover)
            {
                HookFailure = error;
                _router.ResetInputState();
                disposition = KeyboardDisposition.PassThrough;
            }
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
        public List<(KeyboardKey Key, KeyEventKind Kind)>? Events { get; set; }
        public Exception? NextKeyPressFailure { get; set; }
        public void SendKey(KeyboardKey key, KeyEventKind kind)
        {
            Events?.Add((key, kind));
            if (kind == KeyEventKind.Down)
                DownKeys.Add(key.VirtualKey);
            else
                DownKeys.Remove(key.VirtualKey);
        }
        public void SendKeyPress(KeyboardKey key)
        {
            if (NextKeyPressFailure is { } failure)
            {
                NextKeyPressFailure = null;
                throw failure;
            }
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
