using System.Text;
using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyFunctionDispatchExhaustiveTests
{
    private static string ProfilePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    private static readonly string[] FunctionStates =
    [
        "M", "MH", "HM", "MS",
        "KM", "KMH", "KHM", "KMS",
        "AM", "AMH", "AHM", "AMS"
    ];

    private static readonly DirectKeyCase[] DirectKeys =
    [
        new(KeyCode.Q, "Q"),
        new(KeyCode.W, "W"),
        new(KeyCode.U, "U"),
        new(KeyCode.I, "I"),
        new(KeyCode.O, "O"),
        new(KeyCode.P, "P"),
        new(KeyCode.At, "@"),
        new(KeyCode.A, "A"),
        new(KeyCode.S, "S"),
        new(KeyCode.D, "D"),
        new(KeyCode.J, "J"),
        new(KeyCode.K, "K"),
        new(KeyCode.L, "L"),
        new(KeyCode.SColon, ";"),
        new(KeyCode.Colon, ":"),
        new(KeyCode.Z, "Z"),
        new(KeyCode.X, "X"),
        new(KeyCode.C, "C"),
        new(KeyCode.N, "N"),
        new(KeyCode.M, "M"),
        new(KeyCode.Comma, ","),
        new(KeyCode.Dot, "."),
        new(KeyCode.Slash, "/"),
        new(KeyCode.Digit5, "5"),
        new(KeyCode.Digit6, "6"),
        new(KeyCode.Digit7, "7"),
        new(KeyCode.Digit8, "8"),
        new(KeyCode.Digit9, "9"),
        new(KeyCode.Digit0, "0")
    ];

    public static IEnumerable<object[]> EveryDirectFunctionRoute()
    {
        foreach (var key in DirectKeys)
        foreach (var state in FunctionStates)
            yield return [key.Code, key.PhysicalName, state];
    }

    [Fact]
    public void Probe_key_set_exactly_matches_the_legacy_direct_table()
    {
        var mapped = Enum.GetValues<KeyCode>()
            .Where(code => LegacyFunctionSendMap.TryGetValues(code, out _))
            .OrderBy(code => code)
            .ToArray();
        var probed = DirectKeys
            .Select(item => item.Code)
            .OrderBy(code => code)
            .ToArray();

        Assert.Equal(mapped, probed);
    }

    [Theory]
    [MemberData(nameof(EveryDirectFunctionRoute))]
    public void Every_direct_function_route_is_reachable_through_runtime(
        KeyCode keyCode,
        string physicalName,
        string state)
    {
        var expectedSend = ExpectedSend(keyCode, state);
        var expected = RenderLegacySend(expectedSend);
        var actual = DispatchThroughRuntime(state, physicalName);

        // An empty legacy withFuncKey argument is still a defined mapping. It must
        // suppress the physical key rather than accidentally leaking it through.
        Assert.Equal(KeyboardDisposition.Suppress, actual.DownDisposition);
        Assert.Equal(KeyboardDisposition.Suppress, actual.UpDisposition);
        Assert.Equal(expected.Text, actual.Output.Text);
        Assert.Equal(expected.Events, actual.Output.Events);
        Assert.Empty(actual.MediaCommands);
    }

    private static string ExpectedSend(KeyCode keyCode, string state)
    {
        Assert.True(LegacyFunctionSendMap.TryGetValues(keyCode, out var values));

        var (prefix, slot) = state switch
        {
            "M" => ('\0', 0),
            "MH" => ('\0', 1),
            "HM" => ('\0', 2),
            "MS" => ('\0', 3),
            "KM" => ('^', 0),
            "KMH" => ('^', 1),
            "KHM" => ('^', 2),
            "KMS" => ('^', 3),
            "AM" => ('!', 0),
            "AMH" => ('!', 1),
            "AHM" => ('!', 2),
            "AMS" => ('!', 3),
            _ => throw new InvalidDataException($"Unknown function state '{state}'.")
        };

        var value = slot switch
        {
            0 => values.M,
            1 => values.MH,
            2 => values.HM,
            3 => values.MS,
            _ => throw new InvalidDataException($"Unknown legacy function slot {slot}.")
        };

        return prefix == '\0' || value.Length == 0
            ? value
            : string.Concat(prefix, value);
    }

    private static ExecutionResult DispatchThroughRuntime(string state, string physicalName)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = InputMode.S };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var desktop = new ProbeDesktopBackend();
        using var runtime = new IKeydRuntimeHandler(
            configuration,
            new FixedInputMethod(active: false),
            keyboardState,
            new LegacySendOutput(output),
            desktop);

        long timestamp = 0;
        foreach (var layer in state)
            ApplyLayer(runtime, keyboardState, layer, ref timestamp);

        Assert.True(TryResolvePhysicalKey(physicalName, out var key),
            $"No physical key mapping for '{physicalName}'.");

        var down = Dispatch(runtime, keyboardState, key, KeyEventKind.Down, timestamp += 10);
        var up = Dispatch(runtime, keyboardState, key, KeyEventKind.Up, timestamp += 10);

        return new ExecutionResult(down, up, output.Snapshot(), desktop.MediaCommands.ToArray());
    }

    private static void ApplyLayer(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        char layer,
        ref long timestamp)
    {
        switch (layer)
        {
            case 'M':
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.NonConvert),
                    KeyEventKind.Down, timestamp += 10);
                break;
            case 'H':
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Convert),
                    KeyEventKind.Down, timestamp += 10);
                break;
            case 'S':
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Space),
                    KeyEventKind.Down, timestamp += 10);
                break;
            case 'K':
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Kana),
                    KeyEventKind.Down, timestamp += 10);
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Kana),
                    KeyEventKind.Up, timestamp += 10);
                break;
            case 'A':
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Alt),
                    KeyEventKind.Down, timestamp += 10);
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Kana),
                    KeyEventKind.Down, timestamp += 10);
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Kana),
                    KeyEventKind.Up, timestamp += 10);
                Dispatch(runtime, keyboardState,
                    WindowsKeyMap.Keyboard(WindowsKeyMap.Alt),
                    KeyEventKind.Up, timestamp += 10);
                break;
            default:
                throw new InvalidDataException($"Unsupported legacy layer '{layer}'.");
        }
    }

    private static KeyboardDisposition Dispatch(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        KeyboardKey key,
        KeyEventKind kind,
        long timestamp)
    {
        var keyboardEvent = new KeyboardEvent(key, kind, KeyEventOrigin.Physical, timestamp);
        keyboardState.Apply(keyboardEvent);
        return runtime.OnKeyboardEvent(keyboardEvent);
    }

    private static bool TryResolvePhysicalKey(string legacyName, out KeyboardKey key)
    {
        if (legacyName.Length == 1 && WindowsKeyMap.TryResolveCharacter(legacyName[0], out key))
            return true;

        return WindowsKeyMap.TryResolveNamedKey(legacyName, out key);
    }

    private static ObservedOutput RenderLegacySend(string sendText)
    {
        var output = new RecordingKeyboardOutput();
        new LegacySendOutput(output).Send(sendText);
        return output.Snapshot();
    }

    private sealed record DirectKeyCase(KeyCode Code, string PhysicalName);
    private sealed record ExecutionResult(
        KeyboardDisposition DownDisposition,
        KeyboardDisposition UpDisposition,
        ObservedOutput Output,
        IReadOnlyList<DesktopMediaCommand> MediaCommands);
    private sealed record ObservedOutput(string Text, IReadOnlyList<string> Events);

    private sealed class FixedInputMethod(bool active) : IInputMethod
    {
        public bool IsKanaInputActive() => active;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly StringBuilder _text = new();
        private readonly List<string> _events = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => _events.Add($"{(kind == KeyEventKind.Down ? "down" : "up")}:{key.VirtualKey:X2}:{key.ScanCode:X2}:{key.IsExtended}");

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) => _text.Append(text);
        public bool IsToggleOn(ushort virtualKey) => false;
        public ObservedOutput Snapshot() => new(_text.ToString(), _events.ToArray());
    }

    private sealed class ProbeDesktopBackend : IDesktopBackend
    {
        public List<DesktopMediaCommand> MediaCommands { get; } = [];

        public WindowHandle GetActiveWindow() => new(1);
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 100, 100);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "FunctionDispatchProbe";
        public bool IsWindow(WindowHandle window) => true;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [new WindowHandle(1)];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => new(0, 0);
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) => MediaCommands.Add(command);
    }
}
