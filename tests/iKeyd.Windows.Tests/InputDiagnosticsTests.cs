using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Layers;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class InputDiagnosticsTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public void Ring_buffer_keeps_only_the_latest_entries_in_sequence_order()
    {
        var diagnostics = new InputDiagnosticsBuffer();
        var state = EmptyState();

        for (var index = 0; index < InputDiagnosticsBuffer.Capacity + 7; index++)
        {
            diagnostics.RecordEvent(
                new KeyboardEvent(
                    new KeyboardKey((ushort)('A' + (index % 26)), 0),
                    KeyEventKind.Down,
                    KeyEventOrigin.Physical,
                    index),
                state,
                state,
                KeyboardDisposition.PassThrough);
        }

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(InputDiagnosticsBuffer.Capacity, snapshot.Length);
        Assert.Equal(8, snapshot[0].Sequence);
        Assert.Equal(InputDiagnosticsBuffer.Capacity + 7, snapshot[^1].Sequence);
    }

    [Fact]
    public void Export_never_contains_literal_logical_output_text()
    {
        var diagnostics = new InputDiagnosticsBuffer();
        var state = EmptyState();
        const string secret = "literal-output-must-not-appear";

        diagnostics.RecordOutput(10, InputDiagnosticKind.KeymapOutputKeys, state, secret);

        var text = diagnostics.ExportText();
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.Contains($"len={secret.Length}", text, StringComparison.Ordinal);
        Assert.Contains(InputDiagnosticsBuffer.Fingerprint(secret).ToString("X16"), text, StringComparison.Ordinal);
    }

    [Fact]
    public void NonConvert_F_trace_records_legacy_vk_sc_output_and_clean_release_state()
    {
        using var fixture = CreateRuntime(InputMode.R);

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture, 'F', KeyEventKind.Down, 10);
        Dispatch(fixture, 'F', KeyEventKind.Up, 11);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Up, 20);

        var snapshot = fixture.Runtime.GetInputDiagnosticSnapshot();
        Assert.Contains(snapshot, entry => entry.DiagnosticKind == InputDiagnosticKind.LegacyVirtualScan);

        var release = Assert.Single(snapshot.Where(entry =>
            entry.DiagnosticKind == InputDiagnosticKind.Event &&
            entry.VirtualKey == WindowsKeyMap.NonConvert &&
            entry.EventKind == KeyEventKind.Up));
        Assert.Equal(0, release.After.HeldLayerCount);
        Assert.Equal(0, release.After.HeldPhysicalCount);
        Assert.Equal(KeyboardModifierMask.None, release.After.PhysicalModifiers);
        Assert.Equal(0, release.After.LayerCount);
        Assert.Equal(LayerModifiers.None, release.After.LayerModifiers);
    }

    [Fact]
    public void Scan_only_NonConvert_is_the_same_physical_layer_trigger()
    {
        using var fixture = CreateRuntime(InputMode.R);
        var nonConvert = new KeyboardKey(0x00, 0x7B, false);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, nonConvert, KeyEventKind.Down, 0));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Keyboard('F'), KeyEventKind.Down, 10));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, WindowsKeyMap.Keyboard('F'), KeyEventKind.Up, 11));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, nonConvert, KeyEventKind.Up, 20));

        var snapshot = fixture.Runtime.GetInputDiagnosticSnapshot();
        Assert.Contains(snapshot, entry => entry.DiagnosticKind == InputDiagnosticKind.LegacyVirtualScan);
        var release = snapshot.Last(entry =>
            entry.DiagnosticKind == InputDiagnosticKind.Event &&
            entry.ScanCode == 0x7B &&
            entry.EventKind == KeyEventKind.Up);
        Assert.Equal(0, release.After.HeldLayerCount);
        Assert.Equal(0, release.After.LayerCount);
    }

    [Fact]
    public void Physical_modifier_summary_is_recorded_without_snapshot_allocation()
    {
        using var fixture = CreateRuntime(InputMode.R);
        const ushort leftControl = 0xA2;

        Dispatch(fixture, leftControl, KeyEventKind.Down, 0);
        Dispatch(fixture, 'Q', KeyEventKind.Down, 10);

        var q = fixture.Runtime.GetInputDiagnosticSnapshot().Last(entry =>
            entry.DiagnosticKind == InputDiagnosticKind.Event && entry.VirtualKey == 'Q');
        Assert.Equal(2, q.After.HeldPhysicalCount);
        Assert.Equal(KeyboardModifierMask.Control, q.After.PhysicalModifiers);
    }

    [Fact]
    public void Shingeta_trace_records_keyboard_output_strategy_without_plain_romaji()
    {
        using var fixture = CreateRuntime(InputMode.T);

        Dispatch(fixture, 'K', KeyEventKind.Down, 0);
        Dispatch(fixture, 'Q', KeyEventKind.Down, 10);

        var snapshot = fixture.Runtime.GetInputDiagnosticSnapshot();
        var output = Assert.Single(snapshot.Where(entry => entry.DiagnosticKind == InputDiagnosticKind.KeymapOutputKeys));
        Assert.Equal(2, output.PayloadLength);
        Assert.Equal(InputDiagnosticsBuffer.Fingerprint("fa"), output.PayloadFingerprint);
        Assert.DoesNotContain("fa", fixture.Runtime.ExportInputDiagnostics(), StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_NonConvert_F_release_order_stress_returns_to_clean_state()
    {
        using var fixture = CreateRuntime(InputMode.R);
        long timestamp = 0;

        for (var iteration = 0; iteration < 500; iteration++)
        {
            Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, timestamp++);
            if ((iteration % 3) == 0)
                Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, timestamp++);
            Dispatch(fixture, 'F', KeyEventKind.Down, timestamp++);

            if ((iteration & 1) == 0)
            {
                Dispatch(fixture, 'F', KeyEventKind.Up, timestamp++);
                Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Up, timestamp++);
            }
            else
            {
                Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Up, timestamp++);
                Dispatch(fixture, 'F', KeyEventKind.Up, timestamp++);
            }
        }

        var disposition = Dispatch(fixture, 'Q', KeyEventKind.Down, timestamp++);
        Assert.Equal(KeyboardDisposition.PassThrough, disposition);

        var snapshot = fixture.Runtime.GetInputDiagnosticSnapshot();
        Assert.DoesNotContain(snapshot, entry => entry.DiagnosticKind == InputDiagnosticKind.InvariantViolation);
        var last = snapshot.Last(entry => entry.DiagnosticKind == InputDiagnosticKind.Event);
        Assert.Equal(1, last.After.HeldPhysicalCount);
        Assert.Equal(0, last.After.HeldLayerCount);
        Assert.Equal(0, last.After.LayerCount);
    }

    [Fact]
    public void Panic_reset_is_recorded_and_leaves_logical_state_empty_without_falsifying_physical_state()
    {
        using var fixture = CreateRuntime(InputMode.R);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        fixture.Runtime.ResetInputState();

        var reset = fixture.Runtime.GetInputDiagnosticSnapshot().Last(entry => entry.DiagnosticKind == InputDiagnosticKind.Reset);
        Assert.Equal(0, reset.After.HeldLayerCount);
        Assert.Equal(0, reset.After.SuppressedKeyCount);
        Assert.Equal(0, reset.After.LayerCount);
        Assert.Equal(1, reset.After.HeldPhysicalCount);
        Assert.Null(reset.After.TimerMode);
        Assert.Equal(ChordEngineState.Idle, reset.After.SChordState);
        Assert.Equal(ChordEngineState.Idle, reset.After.KChordState);
    }

    private static InputDiagnosticState EmptyState()
        => new(
            LayerModifiers.None,
            0,
            false,
            0,
            0,
            KeyboardModifierMask.None,
            0,
            ChordEngineState.Idle,
            ChordEngineState.Idle,
            null,
            0);

    private static RuntimeFixture CreateRuntime(InputMode startupMode)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = startupMode };
        var keyboardState = new KeyboardState();
        var output = new NullKeyboardOutput();
        var runtime = new IKeydRuntimeHandler(
            configuration,
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(output),
            new NullDesktopBackend());
        return new RuntimeFixture(runtime, keyboardState);
    }

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        ushort virtualKey,
        KeyEventKind kind,
        long timestampMs)
        => Dispatch(fixture, WindowsKeyMap.Keyboard(virtualKey), kind, timestampMs);

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        KeyboardKey key,
        KeyEventKind kind,
        long timestampMs)
    {
        var keyboardEvent = new KeyboardEvent(
            key,
            kind,
            KeyEventOrigin.Physical,
            timestampMs);
        fixture.KeyboardState.Apply(keyboardEvent);
        return fixture.Runtime.OnKeyboardEvent(keyboardEvent);
    }

    private sealed record RuntimeFixture(IKeydRuntimeHandler Runtime, KeyboardState KeyboardState) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
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
        public string? GetWindowClass(WindowHandle window) => "InputDiagnosticsTest";
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
}
