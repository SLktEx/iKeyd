using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class RealInputRegressionTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public void Shingeta_romaji_chord_is_emitted_as_keyboard_keys_instead_of_Unicode_text()
    {
        using var fixture = CreateRuntime(InputMode.T);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'K', KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'Q', KeyEventKind.Down, 10));

        Assert.Empty(fixture.Output.Text);
        Assert.Equal(
        [
            Event('F', KeyEventKind.Down),
            Event('F', KeyEventKind.Up),
            Event('A', KeyEventKind.Down),
            Event('A', KeyEventKind.Up)
        ],
        fixture.Output.Events);
    }

    [Fact]
    public void NonConvert_F_keeps_the_legacy_vk_sc_output()
    {
        using var fixture = CreateRuntime(InputMode.R);

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture, 'F', KeyEventKind.Down, 10);
        Dispatch(fixture, 'F', KeyEventKind.Up, 11);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Up, 20);

        Assert.Equal(2, fixture.Output.Events.Count);
        Assert.Equal(new KeyboardKey(0xF3, 0x29), fixture.Output.Events[0].Key);
        Assert.Equal(KeyEventKind.Down, fixture.Output.Events[0].Kind);
        Assert.Equal(new KeyboardKey(0xF3, 0x29), fixture.Output.Events[1].Key);
        Assert.Equal(KeyEventKind.Up, fixture.Output.Events[1].Kind);
    }

    [Fact]
    public void Repeated_layer_down_does_not_reopen_a_consumed_SM_transition()
    {
        using var fixture = CreateRuntime(InputMode.R);

        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 10);
        Dispatch(fixture, 'D', KeyEventKind.Down, 20);

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 100);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Up, 110);
        Dispatch(fixture, 'D', KeyEventKind.Up, 111);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 120);

        Assert.Empty(fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Fact]
    public void Alt_layer_release_matches_the_keydown_variant_even_if_Alt_is_released_first()
    {
        using var fixture = CreateRuntime(InputMode.R);
        const ushort leftAlt = 0xA4;

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, leftAlt, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Down, 10));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, leftAlt, KeyEventKind.Up, 20));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Up, 30));

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'Q', KeyEventKind.Down, 40));
    }

    [Fact]
    public void Panic_reset_discards_held_layer_state_and_accepts_the_late_keyup()
    {
        using var fixture = CreateRuntime(InputMode.R);

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        fixture.Runtime.ResetInputState();
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Up, 10));

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'Q', KeyEventKind.Down, 20));
    }

    [Fact]
    public void Complete_number_row_is_consumed_and_preserved_when_S_and_K_routes_are_active()
    {
        foreach (var mode in new[] { InputMode.S, InputMode.K })
        {
            using var fixture = CreateRuntime(mode, kanaInputActive: true);
            for (var digit = 0; digit <= 9; digit++)
            {
                var virtualKey = (ushort)('0' + digit);
                Assert.Equal(
                    KeyboardDisposition.Suppress,
                    Dispatch(fixture, virtualKey, KeyEventKind.Down, digit * 10L));
                Assert.Equal(
                    KeyboardDisposition.Suppress,
                    Dispatch(fixture, virtualKey, KeyEventKind.Up, digit * 10L + 1));
            }

            Assert.Equal(Enumerable.Range(0, 10).Select(value => value.ToString()), fixture.Output.Text);
        }
    }

    [Fact]
    public void S_function_row_is_emitted_and_K_function_row_is_transparent_on_active_IME_routes()
    {
        using (var sFixture = CreateRuntime(InputMode.S, kanaInputActive: true))
        {
            for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
            {
                var virtualKey = (ushort)(WindowsKeyMap.F1 + functionNumber - 1);
                Assert.Equal(
                    KeyboardDisposition.Suppress,
                    Dispatch(sFixture, virtualKey, KeyEventKind.Down, functionNumber * 10L));
                Assert.Equal(
                    KeyboardDisposition.Suppress,
                    Dispatch(sFixture, virtualKey, KeyEventKind.Up, functionNumber * 10L + 1));
            }

            Assert.Equal(24, sFixture.Output.Events.Count);
            for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
            {
                var virtualKey = (ushort)(WindowsKeyMap.F1 + functionNumber - 1);
                var eventOffset = (functionNumber - 1) * 2;
                Assert.Equal(new RecordedKeyboardEvent(WindowsKeyMap.Keyboard(virtualKey), KeyEventKind.Down), sFixture.Output.Events[eventOffset]);
                Assert.Equal(new RecordedKeyboardEvent(WindowsKeyMap.Keyboard(virtualKey), KeyEventKind.Up), sFixture.Output.Events[eventOffset + 1]);
            }
        }

        using (var kFixture = CreateRuntime(InputMode.K, kanaInputActive: true))
        {
            for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
            {
                var virtualKey = (ushort)(WindowsKeyMap.F1 + functionNumber - 1);
                Assert.Equal(
                    KeyboardDisposition.PassThrough,
                    Dispatch(kFixture, virtualKey, KeyEventKind.Down, functionNumber * 10L));
                Assert.Equal(
                    KeyboardDisposition.PassThrough,
                    Dispatch(kFixture, virtualKey, KeyEventKind.Up, functionNumber * 10L + 1));
            }

            Assert.Empty(kFixture.Output.Events);
            Assert.Empty(kFixture.Output.Text);
        }
    }

    [Theory]
    [InlineData(InputMode.S)]
    [InlineData(InputMode.K)]
    public void Number_and_function_rows_stay_transparent_when_IME_route_is_inactive(InputMode mode)
    {
        using var fixture = CreateRuntime(mode, kanaInputActive: false);
        var keys = new ushort[] { '0', '9', WindowsKeyMap.F1, WindowsKeyMap.F12 };

        for (var index = 0; index < keys.Length; index++)
        {
            var virtualKey = keys[index];
            Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, virtualKey, KeyEventKind.Down, index * 10L));
            Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, virtualKey, KeyEventKind.Up, index * 10L + 1));
        }

        Assert.Empty(fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Fact]
    public void SM_wheel_uses_the_real_JIS_semicolon_and_colon_physical_positions()
    {
        using var fixture = CreateRuntime(InputMode.R);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 10));

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, (ushort)0xBB, KeyEventKind.Down, 20));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, (ushort)0xBB, KeyEventKind.Up, 21));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, (ushort)0xBA, KeyEventKind.Down, 30));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, (ushort)0xBA, KeyEventKind.Up, 31));

        Assert.Equal(
        [
            new RecordedScroll(-120, false),
            new RecordedScroll(-120, true)
        ],
        fixture.Desktop.Scrolls);
    }

    [Theory]
    [InlineData(false, false, false, 1000.0)]
    [InlineData(true, false, false, 800.0)]
    [InlineData(false, true, false, 240.0)]
    [InlineData(false, false, true, 4400.0)]
    [InlineData(true, true, true, 800.0)]
    public void Keyboard_mouse_uses_explicit_velocity_bands_instead_of_hold_time_acceleration(
        bool precision,
        bool fine,
        bool fast,
        double expected)
    {
        Assert.Equal(expected, KeyboardMouseMotion.SpeedForModifiers(precision, fine, fast));
    }

    private static RuntimeFixture CreateRuntime(InputMode startupMode, bool kanaInputActive = false)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = startupMode };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var desktop = new RecordingDesktopBackend();
        var runtime = new IKeydRuntimeHandler(
            configuration,
            new FixedInputMethod(kanaInputActive),
            keyboardState,
            new LegacySendOutput(output),
            desktop);
        return new RuntimeFixture(runtime, keyboardState, output, desktop);
    }

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        ushort virtualKey,
        KeyEventKind kind,
        long timestampMs)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            kind,
            KeyEventOrigin.Physical,
            timestampMs);
        fixture.KeyboardState.Apply(keyboardEvent);
        return fixture.Runtime.OnKeyboardEvent(keyboardEvent);
    }

    private static RecordedKeyboardEvent Event(char virtualKey, KeyEventKind kind)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind);

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        KeyboardState KeyboardState,
        RecordingKeyboardOutput Output,
        RecordingDesktopBackend Desktop) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class FixedInputMethod(bool kanaInputActive) : IInputMethod
    {
        public bool IsKanaInputActive() => kanaInputActive;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<RecordedKeyboardEvent> Events { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new RecordedKeyboardEvent(key, kind));

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public List<RecordedScroll> Scrolls { get; } = [];

        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "RealInputRegressionTest";
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
        public void ScrollVertical(int wheelDelta, bool controlModifier = false)
            => Scrolls.Add(new RecordedScroll(wheelDelta, controlModifier));
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);
    private readonly record struct RecordedScroll(int Delta, bool ControlModifier);
}
