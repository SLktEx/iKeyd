using iKeyd.App;
using iKeyd.Compatibility.Tests;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Runs compatibility scenarios through the complete hotkeySKG Windows runtime
/// (layers, mode routing and legacy Send handling), rather than only the shared
/// chord engine used by <see cref="WindowsScenarioRunner"/>.
///
/// The legacy hosted oracle is switched to T mode to avoid requiring Japanese
/// IME installation on GitHub-hosted runners. This runner mirrors that setup by
/// reporting kana input active while retaining the scenario's requested S/K
/// keymap. Physical input is fed directly into the runtime so the comparison is
/// deterministic; Windows hook/SendInput coverage remains in WindowsScenarioRunner.
/// </summary>
public sealed class HotkeySkgRuntimeScenarioRunner : ICompatibilityScenarioRunner
{
    public string Name => "iKeyd.hotkeySKG-runtime";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The hotkeySKG runtime scenario runner requires Windows.");
        if (scenario.InitialState.Modifiers.Count != 0)
            throw new NotSupportedException("Runtime scenarios must express modifiers as explicit input events.");

        var configPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
        var configuration = IKeydConfiguration.Load(configPath);
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var desktop = new NoOpDesktopBackend();
        var send = new LegacySendOutput(output, desktop);

        using var runtime = new IKeydRuntimeHandler(
            configuration,
            new HostedKanaInputMethod(),
            keyboardState,
            send,
            desktop);

        if (!Enum.TryParse<InputMode>(scenario.InitialState.Mode, ignoreCase: true, out var mode))
            throw new NotSupportedException($"Unsupported runtime scenario mode '{scenario.InitialState.Mode}'.");
        runtime.SetMode(mode);

        foreach (var input in scenario.Input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var virtualKey = ScenarioKeyboard.ResolveVirtualKey(input.Key!);
            var kind = string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase)
                ? KeyEventKind.Up
                : KeyEventKind.Down;
            var keyboardEvent = new KeyboardEvent(
                new KeyboardKey(virtualKey, 0, ScenarioKeyboard.IsExtended(virtualKey)),
                kind,
                KeyEventOrigin.Physical,
                input.AtMs);

            // WindowsKeyboardHook updates KeyboardState before dispatching to the
            // runtime; reproduce that ordering here.
            keyboardState.Apply(keyboardEvent);
            _ = runtime.OnKeyboardEvent(keyboardEvent);
        }

        return Task.FromResult(new ScenarioRunResult
        {
            Runner = Name,
            ScenarioId = scenario.Id,
            Text = output.Text.ToString(),
            Events = output.Events.ToList(),
            Metadata = new Dictionary<string, string>
            {
                ["mode"] = scenario.InitialState.Mode,
                ["ime"] = "bypassed-via-hosted-kana-route",
                ["scope"] = "full-hotkeyskg-runtime-direct-input"
            }
        });
    }

    private sealed class HostedKanaInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => true;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public System.Text.StringBuilder Text { get; } = new();
        public List<ObservedKeyEvent> Events { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new ObservedKeyEvent
            {
                Kind = kind == KeyEventKind.Down ? "keyDown" : "keyUp",
                Key = ScenarioKeyboard.ResolveName(key.VirtualKey)
            });

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) => Text.Append(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class NoOpDesktopBackend : IDesktopBackend
    {
        private static readonly WindowHandle Active = new(1);
        private readonly HashSet<DesktopMouseButton> _buttons = [];

        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 1920, 1080);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "RuntimeScenarioWindow";
        public bool IsWindow(WindowHandle window) => !window.IsEmpty;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => _buttons.Contains(button);
        public void SetMouseButton(DesktopMouseButton button, bool down)
        {
            if (down)
                _buttons.Add(button);
            else
                _buttons.Remove(button);
        }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
