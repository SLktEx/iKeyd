using System.Text;
using System.Text.Json;
using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyModifiedDispatchMatrixTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
    private static string RuntimeFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");

    private static readonly DispatchCase[] KeyboardCases =
    [
        new("H", "Q", "^q"),
        new("S", "Q", "+q"),
        new("HS", "Q", "^+q"),
        new("K", "Q", "#q"),
        new("A", "Q", "!q"),
        new("KH", "Q", "#^q"),
        new("KS", "Q", "#+q"),
        new("AH", "Q", "!^q"),
        new("AS", "Q", "!+q"),
        new("SH", "A", "1"),
        new("KSH", "A", "^1"),
        new("ASH", "A", "!1")
    ];

    public static IEnumerable<object[]> ExplicitKeyboardCases()
        => KeyboardCases.Select(item => new object[] { item.State, item.KeyName, item.ExpectedSend });

    [Fact]
    public void Every_explicit_keyboard_modified_dispatch_state_has_an_executable_runtime_probe()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        var fixtureStates = document.RootElement
            .GetProperty("modifiedKeyDispatch")
            .EnumerateArray()
            .Where(item => item.GetProperty("state").GetString() is not ("SM" or "other"))
            .Select(item => item.GetProperty("state").GetString() ?? string.Empty)
            .OrderBy(state => state, StringComparer.Ordinal)
            .ToArray();

        var probedStates = KeyboardCases
            .Select(item => item.State)
            .OrderBy(state => state, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(fixtureStates, probedStates);
    }

    [Theory]
    [MemberData(nameof(ExplicitKeyboardCases))]
    public void Explicit_keyboard_modified_dispatch_state_matches_legacy_action(
        string state,
        string keyName,
        string expectedSend)
    {
        var expected = RenderLegacySend(expectedSend);
        var actual = DispatchThroughRuntime(state, keyName);

        Assert.Equal(expected.Text, actual.Output.Text);
        Assert.Equal(expected.Events, actual.Output.Events);
        Assert.Empty(actual.MediaCommands);
    }

    [Fact]
    public void SM_modified_dispatch_reaches_mouse_media_mode()
    {
        var actual = DispatchThroughRuntime("SM", "Q");

        Assert.Equal(new[] { DesktopMediaCommand.VolumeUp }, actual.MediaCommands);
        Assert.Equal(string.Empty, actual.Output.Text);
        Assert.Empty(actual.Output.Events);
    }

    [Fact]
    public void Other_modified_dispatch_reaches_function_dispatch()
    {
        var expected = RenderLegacySend("{LEFT}");
        var actual = DispatchThroughRuntime("M", "J");

        Assert.Equal(expected.Text, actual.Output.Text);
        Assert.Equal(expected.Events, actual.Output.Events);
        Assert.Empty(actual.MediaCommands);
    }

    private static ExecutionResult DispatchThroughRuntime(string state, string keyName)
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

        // Layer setup is precondition construction, not part of the dispatch
        // observation. This matters for A because the physical Alt+Kana setup
        // intentionally reproduces AutoHotkey's Ctrl menu-mask tap.
        output.Clear();
        desktop.MediaCommands.Clear();

        Assert.True(TryResolvePhysicalKey(keyName, out var key), $"No physical key mapping for '{keyName}'.");
        Dispatch(runtime, keyboardState, key, KeyEventKind.Down, timestamp += 10);
        Dispatch(runtime, keyboardState, key, KeyEventKind.Up, timestamp += 10);

        return new ExecutionResult(output.Snapshot(), desktop.MediaCommands.ToArray());
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
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.NonConvert), KeyEventKind.Down, timestamp += 10);
                break;
            case 'H':
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Convert), KeyEventKind.Down, timestamp += 10);
                break;
            case 'S':
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, timestamp += 10);
                break;
            case 'K':
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Kana), KeyEventKind.Down, timestamp += 10);
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Kana), KeyEventKind.Up, timestamp += 10);
                break;
            case 'A':
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Alt), KeyEventKind.Down, timestamp += 10);
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Kana), KeyEventKind.Down, timestamp += 10);
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Kana), KeyEventKind.Up, timestamp += 10);
                Dispatch(runtime, keyboardState, WindowsKeyMap.Keyboard(WindowsKeyMap.Alt), KeyEventKind.Up, timestamp += 10);
                break;
            default:
                throw new InvalidDataException($"Unsupported legacy layer '{layer}'.");
        }
    }

    private static void Dispatch(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        KeyboardKey key,
        KeyEventKind kind,
        long timestamp)
    {
        var keyboardEvent = new KeyboardEvent(key, kind, KeyEventOrigin.Physical, timestamp);
        keyboardState.Apply(keyboardEvent);
        runtime.OnKeyboardEvent(keyboardEvent);
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

    private sealed record DispatchCase(string State, string KeyName, string ExpectedSend);
    private sealed record ExecutionResult(ObservedOutput Output, IReadOnlyList<DesktopMediaCommand> MediaCommands);
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

        public void Clear()
        {
            _text.Clear();
            _events.Clear();
        }

        public ObservedOutput Snapshot() => new(_text.ToString(), _events.ToArray());
    }

    private sealed class ProbeDesktopBackend : IDesktopBackend
    {
        public List<DesktopMediaCommand> MediaCommands { get; } = [];

        public WindowHandle GetActiveWindow() => new(1);
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 100, 100);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "ModifiedDispatchProbe";
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
