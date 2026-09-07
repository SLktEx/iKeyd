using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed partial class LegacyShLayerParityTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
    private static string RuntimeFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");

    [Theory]
    [InlineData("SH", "1")]
    [InlineData("KSH", "^1")]
    [InlineData("ASH", "!1")]
    public void Space_then_Convert_A_matches_legacy_SHKey_A(string state, string expectedSend)
    {
        var expected = RenderLegacySend(expectedSend);
        var actual = DispatchThroughRuntime(state, "A");

        AssertObservedEqual(state, "A", expectedSend, expected, actual);
    }

    [Theory]
    [InlineData("1", "{F1}")]
    [InlineData("2", "{F2}")]
    [InlineData("3", "{F3}")]
    [InlineData("4", "{F4}")]
    public void SH_physical_mode_digits_are_resolved_before_process1_to_4_mode_switches(
        string keyName,
        string shSend)
    {
        foreach (var (state, prefix) in LoadShDispatchStates())
        {
            var expectedSend = prefix + shSend;
            var expected = RenderLegacySend(expectedSend);
            var actual = DispatchThroughRuntime(state, keyName);

            AssertObservedEqual(state, keyName, expectedSend, expected, actual);
        }
    }

    [Fact]
    public void Runtime_fixture_keeps_all_SHKey_dispatch_states_in_the_parity_matrix()
    {
        var states = LoadShDispatchStates();

        Assert.Equal(
            new[] { (State: "SH", Prefix: ""), (State: "KSH", Prefix: "^"), (State: "ASH", Prefix: "!") },
            states);
    }

    [Fact]
    [Trait("Category", "HostedAhkSourceDifferentialE2E")]
    public void Every_pinned_SHKey_assignment_is_reachable_through_SH_KSH_and_ASH()
    {
        var sourcePath = Environment.GetEnvironmentVariable("IKEYD_LEGACY_AHK");
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return;

        var assignments = LoadShKeyAssignments(sourcePath);
        Assert.NotEmpty(assignments);

        var states = LoadShDispatchStates();
        Assert.NotEmpty(states);

        var failures = new List<string>();
        foreach (var assignment in assignments.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolvePhysicalKey(assignment.Key, out _))
            {
                failures.Add($"SHKey_{assignment.Key}: no Windows physical-key mapping exists for the pinned source assignment.");
                continue;
            }

            foreach (var (state, prefix) in states)
            {
                var expectedSend = prefix + assignment.Value;
                try
                {
                    var expected = RenderLegacySend(expectedSend);
                    var actual = DispatchThroughRuntime(state, assignment.Key);
                    var difference = DescribeDifference(expected, actual);
                    if (difference is not null)
                        failures.Add($"{state}+{assignment.Key} ({Escape(expectedSend)}): {difference}");
                }
                catch (Exception error)
                {
                    failures.Add(
                        $"{state}+{assignment.Key} ({Escape(expectedSend)}): " +
                        $"{error.GetType().Name}: {error.Message}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} pinned SH-layer routes were unreachable or mismatched:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static ObservedOutput DispatchThroughRuntime(string state, string keyName)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = InputMode.S };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(output);
        using var runtime = new IKeydRuntimeHandler(
            configuration,
            new FixedInputMethod(active: false),
            keyboardState,
            send,
            new ProbeDesktopBackend());

        long timestamp = 0;
        foreach (var layer in state)
            ApplyLayer(runtime, keyboardState, layer, ref timestamp);

        Assert.True(TryResolvePhysicalKey(keyName, out var key), $"No physical key mapping for SHKey_{keyName}.");
        Dispatch(runtime, keyboardState, key, KeyEventKind.Down, timestamp += 10);
        Dispatch(runtime, keyboardState, key, KeyEventKind.Up, timestamp += 10);

        return output.Snapshot();
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

    private static IReadOnlyList<(string State, string Prefix)> LoadShDispatchStates()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        return document.RootElement
            .GetProperty("modifiedKeyDispatch")
            .EnumerateArray()
            .Where(item => (item.GetProperty("action").GetString() ?? string.Empty).Contains("SHKey", StringComparison.Ordinal))
            .Select(item =>
            {
                var state = item.GetProperty("state").GetString() ?? string.Empty;
                var action = item.GetProperty("action").GetString() ?? string.Empty;
                var prefix = action switch
                {
                    "SHKey" => string.Empty,
                    "Ctrl+SHKey" => "^",
                    "Alt+SHKey" => "!",
                    _ => throw new InvalidDataException($"Unsupported SHKey dispatch action '{action}' for state '{state}'.")
                };
                return (State: state, Prefix: prefix);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> LoadShKeyAssignments(string sourcePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(sourcePath, Encoding.UTF8))
        {
            var match = ShKeyAssignmentRegex().Match(rawLine.Trim());
            if (!match.Success)
                continue;

            var key = match.Groups[1].Value;
            var value = StripAhkInlineComment(match.Groups[2].Value).Trim();
            if (value.Length != 0)
                result[key] = value;
        }
        return result;
    }

    private static string StripAhkInlineComment(string expression)
    {
        for (var index = 0; index < expression.Length; index++)
        {
            if (expression[index] == ';' && (index == 0 || char.IsWhiteSpace(expression[index - 1])))
                return expression[..index].TrimEnd();
        }
        return expression;
    }

    private static void AssertObservedEqual(
        string state,
        string key,
        string expectedSend,
        ObservedOutput expected,
        ObservedOutput actual)
    {
        var difference = DescribeDifference(expected, actual);
        Assert.True(
            difference is null,
            difference is null
                ? string.Empty
                : $"{state}+{key} should execute legacy Send '{Escape(expectedSend)}': {difference}");
    }

    private static string? DescribeDifference(ObservedOutput expected, ObservedOutput actual)
    {
        if (!string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
            return $"text expected '{Escape(expected.Text)}', actual '{Escape(actual.Text)}'";
        if (!expected.Events.SequenceEqual(actual.Events, StringComparer.Ordinal))
            return $"events expected [{string.Join(", ", expected.Events)}], actual [{string.Join(", ", actual.Events)}]";
        return null;
    }

    private static string Escape(string value)
        => value.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    [GeneratedRegex(@"^SHKey_([A-Za-z0-9]+)\s*(?::=|=)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShKeyAssignmentRegex();

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

    private sealed record ObservedOutput(string Text, IReadOnlyList<string> Events);

    private sealed class ProbeDesktopBackend : IDesktopBackend
    {
        public WindowHandle GetActiveWindow() => new(1);
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 100, 100);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "ShLayerParityProbe";
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
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
