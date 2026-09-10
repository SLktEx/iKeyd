using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class SpaceImeRolloverTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public void Ime_active_composition_Space_alphabet_rollover_commits_Space_once_and_passes_letters_through()
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: true);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Down, 10));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Up, 11));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'B', KeyEventKind.Down, 20));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'B', KeyEventKind.Up, 21));

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 30));

        Assert.Equal(
        [
            Event(WindowsKeyMap.Space, KeyEventKind.Down),
            Event(WindowsKeyMap.Space, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Theory]
    [InlineData(39)]
    [InlineData(40)]
    public void Ime_active_composition_fast_Space_alphabet_rollover_is_inclusive_at_chord_boundary(long pressGapMs)
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: true);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Down, pressGapMs));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Up, pressGapMs + 1));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, pressGapMs + 2));

        Assert.Equal(
        [
            Event(WindowsKeyMap.Space, KeyEventKind.Down),
            Event(WindowsKeyMap.Space, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Fact]
    public void Ime_active_without_composition_fast_Space_alphabet_preserves_legacy_thumb_shift()
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: false);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Down, 10));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Up, 11));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 20));

        Assert.Equal(
        [
            Event(0xA0, KeyEventKind.Down),
            Event('A', KeyEventKind.Down),
            Event('A', KeyEventKind.Up),
            Event(0xA0, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Fact]
    public void Ime_active_without_composition_does_not_inject_literal_Space_byte()
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: false);

        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        Dispatch(fixture, 'A', KeyEventKind.Down, 10);
        Dispatch(fixture, 'A', KeyEventKind.Up, 11);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 20);

        Assert.DoesNotContain(
            fixture.Output.Events,
            item => item.Key.VirtualKey == WindowsKeyMap.Space);
    }

    [Theory]
    [InlineData(41)]
    [InlineData(100)]
    [InlineData(500)]
    public void Ime_active_long_held_Space_alphabet_preserves_legacy_thumb_shift(long pressGapMs)
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: true);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Down, pressGapMs));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Up, pressGapMs + 1));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, pressGapMs + 2));

        Assert.Equal(
        [
            Event(0xA0, KeyEventKind.Down),
            Event('A', KeyEventKind.Down),
            Event('A', KeyEventKind.Up),
            Event(0xA0, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Fact]
    public void Ime_inactive_Space_alphabet_preserves_legacy_thumb_shift()
    {
        using var fixture = CreateRuntime(kanaInputActive: false, compositionActive: false);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Down, 10));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Up, 11));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 20));

        Assert.Equal(
        [
            Event(0xA0, KeyEventKind.Down),
            Event('A', KeyEventKind.Down),
            Event('A', KeyEventKind.Up),
            Event(0xA0, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    [Fact]
    public void Ime_active_Space_non_alphabet_keeps_the_legacy_shift_layer()
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: true);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, '5', KeyEventKind.Down, 10));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, '5', KeyEventKind.Up, 11));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 20));

        Assert.Equal(
        [
            Event(0xA0, KeyEventKind.Down),
            Event('5', KeyEventKind.Down),
            Event('5', KeyEventKind.Up),
            Event(0xA0, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Empty(fixture.Output.Text);
    }

    private static RuntimeFixture CreateRuntime(bool kanaInputActive, bool compositionActive)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = InputMode.R };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var runtime = new IKeydRuntimeHandler(
            configuration,
            new FixedInputMethod(kanaInputActive, compositionActive),
            keyboardState,
            new LegacySendOutput(output),
            new NullDesktopBackend());
        return new RuntimeFixture(runtime, keyboardState, output);
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

    private static RecordedKeyboardEvent Event(ushort virtualKey, KeyEventKind kind)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind);

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        KeyboardState KeyboardState,
        RecordingKeyboardOutput Output) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class FixedInputMethod(bool active, bool compositionActive) : IInputMethod, IInputCompositionState
    {
        public bool IsKanaInputActive() => active;
        public bool IsCompositionActive() => compositionActive;
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

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);

        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "SpaceImeRolloverTest";
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
