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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Roman_only_Space_A_uses_one_Shift_chord_without_literal_Space_even_with_composition(
        bool repeatSpace, bool releaseSpaceFirst)
    {
        var kanaInputActive = WindowsInputMethod.IsRomaKanaConversionMode(0x10);
        using var fixture = CreateRuntime(kanaInputActive, compositionActive: true);

        Assert.False(kanaInputActive);
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        if (repeatSpace)
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 5));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Down, 10));
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.NotKana, KanaActive: false), LastEvent(fixture).Rollover);
        Assert.Equal(new[] { "kana" }, fixture.InputMethod.Queries);
        if (releaseSpaceFirst)
        {
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 11));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Up, 12));
        }
        else
        {
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Up, 11));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 12));
        }

        Assert.Equal(
        [
            Event(0xA0, KeyEventKind.Down),
            Event('A', KeyEventKind.Down),
            Event('A', KeyEventKind.Up),
            Event(0xA0, KeyEventKind.Up)
        ],
        fixture.Output.Events);
        Assert.Equal(1, fixture.Output.ChordCount);
        Assert.DoesNotContain(fixture.Output.Events, item => item.Key.VirtualKey == WindowsKeyMap.Space);
        Assert.Empty(fixture.Output.Text);
    }

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
    [InlineData(10)]
    [InlineData(39)]
    [InlineData(40)]
    public void Ime_active_composition_fast_Space_alphabet_rollover_is_inclusive_at_chord_boundary(long pressGapMs)
    {
        using var fixture = CreateRuntime(kanaInputActive: true, compositionActive: true);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Down, pressGapMs));
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.Committed, pressGapMs, 40, true, true, SpaceImeSendStatus.Returned), LastEvent(fixture).Rollover);
        Assert.Equal(new[] { "kana", "composition" }, fixture.InputMethod.Queries);
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
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.NoComposition, KanaActive: true, CompositionActive: false), LastEvent(fixture).Rollover);
        Assert.Equal(new[] { "kana", "composition" }, fixture.InputMethod.Queries);
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
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.LongHold, pressGapMs, 40, true, true), LastEvent(fixture).Rollover);
        Assert.Equal(new[] { "kana", "composition" }, fixture.InputMethod.Queries);
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
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.NotAlphabet, KanaActive: true, CompositionActive: true), LastEvent(fixture).Rollover);
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

    [Theory]
    [InlineData(40, 40)]
    [InlineData(41, 40)]
    [InlineData(75, 75)]
    [InlineData(76, 75)]
    public void Space_repeat_preserves_original_timestamp_and_configured_boundary(int gap, int threshold)
    {
        using var fixture = CreateRuntime(true, true, threshold);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 1000);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 1000 + gap - 1);
        Assert.Equal(default, LastEvent(fixture).Rollover);
        Dispatch(fixture, 'A', KeyEventKind.Down, 1000 + gap);
        var diagnostic = LastEvent(fixture).Rollover;
        Assert.Equal(gap, diagnostic.ElapsedMs);
        Assert.Equal(threshold, diagnostic.ThresholdMs);
        Assert.Equal(gap <= threshold ? SpaceImeRolloverDecision.Committed : SpaceImeRolloverDecision.LongHold, diagnostic.Decision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Committed_hold_continues_without_queries_and_both_release_orders_clear_hold(bool releaseSpaceFirst)
    {
        using var fixture = CreateRuntime(true, true);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        Dispatch(fixture, 'A', KeyEventKind.Down, 10);
        fixture.InputMethod.ThrowOnQuery = true;
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 499);
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Down, 500));
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.Continuation), LastEvent(fixture).Rollover);
        Assert.Equal(new[] { "kana", "composition" }, fixture.InputMethod.Queries);
        Assert.Equal(2, fixture.Output.Events.Count);
        var first = releaseSpaceFirst ? WindowsKeyMap.Space : (ushort)'A';
        var second = releaseSpaceFirst ? (ushort)'A' : WindowsKeyMap.Space;
        Assert.Equal(releaseSpaceFirst ? KeyboardDisposition.Suppress : KeyboardDisposition.PassThrough, Dispatch(fixture, first, KeyEventKind.Up, 501));
        Assert.Equal(default, LastEvent(fixture).Rollover);
        Assert.Equal(releaseSpaceFirst ? KeyboardDisposition.PassThrough : KeyboardDisposition.Suppress, Dispatch(fixture, second, KeyEventKind.Up, 502));
        Assert.Equal(default, LastEvent(fixture).Rollover);
        Assert.Equal(0, LastEvent(fixture).After.HeldLayerCount);
        Assert.Equal(0, LastEvent(fixture).After.LayerCount);
        fixture.InputMethod.ThrowOnQuery = false;
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 600);
        Dispatch(fixture, 'B', KeyEventKind.Down, 641);
        Assert.Equal(SpaceImeRolloverDecision.LongHold, LastEvent(fixture).Rollover.Decision);
    }

    [Fact]
    public void Consumed_hold_short_circuits_without_reusing_previous_observations()
    {
        using var fixture = CreateRuntime(true, false);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        Dispatch(fixture, 'A', KeyEventKind.Down, 10);
        fixture.InputMethod.ThrowOnQuery = true;
        Dispatch(fixture, 'A', KeyEventKind.Down, 20);
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.Consumed), LastEvent(fixture).Rollover);
        Assert.Equal(new[] { "kana", "composition" }, fixture.InputMethod.Queries);
    }

    [Fact]
    public void Release_ownership_can_pass_A_up_after_a_suppressed_repeat()
    {
        using var fixture = CreateRuntime(true, true);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        Dispatch(fixture, 'A', KeyEventKind.Down, 10);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 11);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 20);
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, 'A', KeyEventKind.Down, 100));
        Assert.Equal(SpaceImeRolloverDecision.LongHold, LastEvent(fixture).Rollover.Decision);
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Up, 101));
        Assert.Equal(default, LastEvent(fixture).Rollover);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Query_exception_keeps_only_obtained_results_and_rethrows(bool compositionQuery)
    {
        using var fixture = CreateRuntime(true, true);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        fixture.InputMethod.ThrowOnQuery = !compositionQuery;
        fixture.InputMethod.ThrowOnComposition = compositionQuery;
        Assert.Throws<InvalidOperationException>(() => Dispatch(fixture, 'A', KeyEventKind.Down, 10));
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.Interrupted, KanaActive: compositionQuery ? true : null), LastEvent(fixture).Rollover);
        Assert.Equal(InputDiagnosticKind.Exception, fixture.Runtime.GetInputDiagnosticSnapshot()[^1].DiagnosticKind);
    }

    [Fact]
    public void Space_output_exception_records_attempt_and_reset_does_not_reuse_decision()
    {
        using var fixture = CreateRuntime(true, true);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        fixture.Output.ThrowOnSpace = true;
        Assert.Throws<InvalidOperationException>(() => Dispatch(fixture, 'A', KeyEventKind.Down, 10));
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.Interrupted, 10, 40, true, true, SpaceImeSendStatus.Attempted), LastEvent(fixture).Rollover);
        var exception = fixture.Runtime.GetInputDiagnosticSnapshot()[^1];
        Assert.Equal(InputDiagnosticKind.Exception, exception.DiagnosticKind);
        Assert.Equal(0, exception.After.LayerCount);
        Assert.Equal(0, exception.After.HeldLayerCount);
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(fixture, 'A', KeyEventKind.Up, 11));
        Assert.Equal(default, LastEvent(fixture).Rollover);
        fixture.Output.ThrowOnSpace = false;
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 12);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 100);
        Dispatch(fixture, 'A', KeyEventKind.Down, 110);
        Assert.Equal(SpaceImeRolloverDecision.Committed, LastEvent(fixture).Rollover.Decision);
        fixture.Runtime.ResetInputState();
        Assert.Equal(default, fixture.Runtime.GetInputDiagnosticSnapshot()[^1].Rollover);
        Dispatch(fixture, 'A', KeyEventKind.Up, 111);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Up, 112);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 200);
        Dispatch(fixture, 'A', KeyEventKind.Down, 241);
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.LongHold, 41, 40, true, true), LastEvent(fixture).Rollover);
    }

    private static InputDiagnosticEntry LastEvent(RuntimeFixture fixture)
        => fixture.Runtime.GetInputDiagnosticSnapshot().Last(entry => entry.DiagnosticKind == InputDiagnosticKind.Event);

    [Fact]
    public void Other_layer_is_not_applicable_and_does_not_query_IME()
    {
        using var fixture = CreateRuntime(true, true);
        fixture.InputMethod.ThrowOnQuery = true;
        Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Down, 0);
        Dispatch(fixture, 'A', KeyEventKind.Down, 10);
        Assert.Equal(default, LastEvent(fixture).Rollover);
        Assert.Empty(fixture.InputMethod.Queries);
    }

    [Fact]
    public void Missing_composition_interface_is_distinct_from_a_negative_query()
    {
        using var fixture = CreateRuntime(true, true, inputOverride: new KanaOnlyInputMethod());
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 0);
        Dispatch(fixture, 'A', KeyEventKind.Down, 10);
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.CompositionUnavailable, KanaActive: true), LastEvent(fixture).Rollover);
    }

    [Fact]
    public void Reversed_timestamp_rejects_before_reading_threshold()
    {
        using var fixture = CreateRuntime(true, true);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 100);
        Dispatch(fixture, 'A', KeyEventKind.Down, 99);
        Assert.Equal(new SpaceImeRolloverDiagnostic(SpaceImeRolloverDecision.TimestampReversed, KanaActive: true, CompositionActive: true), LastEvent(fixture).Rollover);
        Assert.DoesNotContain(fixture.Output.Events, item => item.Key.VirtualKey == WindowsKeyMap.Space);
    }

    private sealed class KanaOnlyInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => true;
    }

    private static RuntimeFixture CreateRuntime(bool kanaInputActive, bool compositionActive, int threshold = 40, IInputMethod? inputOverride = null)
    {
        var profile = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ProfilePath))!;
        profile["source"]!["chordWindowMs"] = threshold;
        var configuration = IKeydConfiguration.Parse(profile.ToJsonString()) with { StartupMode = InputMode.R };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var inputMethod = new FixedInputMethod(kanaInputActive, compositionActive);
        var runtime = new IKeydRuntimeHandler(
            configuration,
            inputOverride ?? inputMethod,
            keyboardState,
            new LegacySendOutput(output),
            new NullDesktopBackend());
        return new RuntimeFixture(runtime, keyboardState, output, inputMethod);
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
        RecordingKeyboardOutput Output,
        FixedInputMethod InputMethod) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class FixedInputMethod(bool active, bool compositionActive) : IInputMethod, IInputCompositionState
    {
        public List<string> Queries { get; } = [];
        public bool ThrowOnQuery { get; set; }
        public bool ThrowOnComposition { get; set; }
        public bool IsKanaInputActive()
        {
            Queries.Add("kana");
            if (ThrowOnQuery) throw new InvalidOperationException();
            return active;
        }
        public bool IsCompositionActive()
        {
            Queries.Add("composition");
            if (ThrowOnQuery || ThrowOnComposition) throw new InvalidOperationException();
            return compositionActive;
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput, IKeyboardChordOutput
    {
        public int ChordCount { get; private set; }
        public bool ThrowOnSpace { get; set; }

        public void SendChord(ReadOnlySpan<KeyboardKey> modifiers, KeyboardKey key)
        {
            ChordCount++;
            foreach (var modifier in modifiers)
                SendKey(modifier, KeyEventKind.Down);
            SendKeyPress(key);
            foreach (var modifier in modifiers)
                SendKey(modifier, KeyEventKind.Up);
        }

        public List<RecordedKeyboardEvent> Events { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new RecordedKeyboardEvent(key, kind));

        public void SendKeyPress(KeyboardKey key)
        {
            if (ThrowOnSpace && key.VirtualKey == WindowsKeyMap.Space)
                throw new InvalidOperationException();
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
