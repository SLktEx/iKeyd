using System.Text;
using iKeyd.App;
using iKeyd.Compatibility.Tests;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Runs compatibility scenarios through the real Windows-v1 runtime handler while
/// replacing OS side effects with recording backends. This exercises layer/mode/
/// function dispatch without minimizing the CI runner or moving its real pointer.
/// </summary>
public sealed class IKeydRuntimeScenarioRunner : ICompatibilityScenarioRunner
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
    private readonly DesktopMouseButton[] _initialMouseButtons;

    public IKeydRuntimeScenarioRunner(params DesktopMouseButton[] initialMouseButtons)
        => _initialMouseButtons = initialMouseButtons ?? [];

    public string Name => "iKeyd.Runtime";
    public bool IsAvailable => true;

    public Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        cancellationToken.ThrowIfCancellationRequested();
        if (scenario.InitialState.Modifiers.Count != 0)
            throw new NotSupportedException("The deterministic runtime runner does not apply initial OS modifiers yet.");

        if (!Enum.TryParse<InputMode>(scenario.InitialState.Mode, ignoreCase: true, out var startupMode))
            throw new InvalidDataException($"Unsupported scenario mode '{scenario.InitialState.Mode}'.");

        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = startupMode };
        var keyboardState = new KeyboardState();
        var keyboard = new RecordingKeyboardOutput();
        var desktop = new RecordingDesktopBackend();
        desktop.SetInitialMouseButtons(_initialMouseButtons);
        var inputMethod = new FixedInputMethod(
            string.Equals(scenario.InitialState.Ime, "on", StringComparison.OrdinalIgnoreCase));
        var send = new LegacySendOutput(keyboard);

        using var runtime = new IKeydRuntimeHandler(
            configuration,
            inputMethod,
            keyboardState,
            send,
            desktop);

        ApplyInitialLayers(runtime, keyboardState, scenario.InitialState.Layers);
        foreach (var input in scenario.Input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dispatch(runtime, keyboardState, input);
        }

        return Task.FromResult(new ScenarioRunResult
        {
            Runner = Name,
            ScenarioId = scenario.Id,
            Text = keyboard.Text.Length == 0 ? null : keyboard.Text,
            Events = keyboard.Events.ToList(),
            Actions = desktop.Actions.ToList(),
            Metadata = new Dictionary<string, string>
            {
                ["mode"] = scenario.InitialState.Mode,
                ["ime"] = scenario.InitialState.Ime,
                ["layers"] = string.Join(string.Empty, scenario.InitialState.Layers),
                ["scope"] = "runtime-handler-recording-backends"
            }
        });
    }

    private static void ApplyInitialLayers(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        IReadOnlyList<string> layers)
    {
        foreach (var rawLayer in layers)
        {
            var layer = rawLayer.Trim().ToUpperInvariant();
            switch (layer)
            {
                case "M":
                    Dispatch(runtime, keyboardState, "keyDown", WindowsKeyMap.NonConvert, 0);
                    break;
                case "H":
                    Dispatch(runtime, keyboardState, "keyDown", WindowsKeyMap.Convert, 0);
                    break;
                case "S":
                    Dispatch(runtime, keyboardState, "keyDown", WindowsKeyMap.Space, 0);
                    break;
                case "K":
                    Dispatch(runtime, keyboardState, "keyDown", WindowsKeyMap.Kana, 0);
                    Dispatch(runtime, keyboardState, "keyUp", WindowsKeyMap.Kana, 0);
                    break;
                case "A":
                    Dispatch(runtime, keyboardState, "keyDown", WindowsKeyMap.Alt, 0);
                    Dispatch(runtime, keyboardState, "keyDown", WindowsKeyMap.Kana, 0);
                    Dispatch(runtime, keyboardState, "keyUp", WindowsKeyMap.Kana, 0);
                    Dispatch(runtime, keyboardState, "keyUp", WindowsKeyMap.Alt, 0);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported initial layer '{rawLayer}'.");
            }
        }
    }

    private static void Dispatch(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        ScenarioInputEvent input)
        => Dispatch(runtime, keyboardState, input.Kind, ResolveVirtualKey(input.Key!), input.AtMs);

    private static void Dispatch(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        string kind,
        ushort virtualKey,
        long timestampMs)
    {
        var keyEventKind = string.Equals(kind, "keyUp", StringComparison.OrdinalIgnoreCase)
            ? KeyEventKind.Up
            : KeyEventKind.Down;
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            keyEventKind,
            KeyEventOrigin.Physical,
            timestampMs);
        keyboardState.Apply(keyboardEvent);
        runtime.OnKeyboardEvent(keyboardEvent);
    }

    private static ushort ResolveVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var upper = char.ToUpperInvariant(key[0]);
            if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return upper;
        }

        return key.Trim().ToUpperInvariant() switch
        {
            "SCOLON" => WindowsKeyMap.OemSemicolon,
            "COLON" => WindowsKeyMap.OemPlus,
            "COMMA" => WindowsKeyMap.OemComma,
            "DOT" => WindowsKeyMap.OemPeriod,
            "SLASH" => WindowsKeyMap.OemSlash,
            "AT" => WindowsKeyMap.OemAt,
            "SPACE" => WindowsKeyMap.Space,
            "NONCONVERT" or "MUHENKAN" => WindowsKeyMap.NonConvert,
            "CONVERT" or "HENKAN" => WindowsKeyMap.Convert,
            "KANA" => WindowsKeyMap.Kana,
            "ALT" => WindowsKeyMap.Alt,
            "CTRL" or "CONTROL" => WindowsKeyMap.Control,
            "SHIFT" => WindowsKeyMap.Shift,
            _ => throw new NotSupportedException($"No runtime virtual-key mapping for scenario key '{key}'.")
        };
    }

    private sealed class FixedInputMethod(bool active) : IInputMethod
    {
        public bool IsKanaInputActive() => active;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly StringBuilder _text = new();
        private readonly List<ObservedKeyEvent> _events = [];

        public string Text => _text.ToString();
        public IReadOnlyList<ObservedKeyEvent> Events => _events;

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => _events.Add(new ObservedKeyEvent
            {
                Kind = kind == KeyEventKind.Down ? "keyDown" : "keyUp",
                Key = KeyName(key.VirtualKey)
            });

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) => _text.Append(text);
        public bool IsToggleOn(ushort virtualKey) => false;

        private static string KeyName(ushort virtualKey)
        {
            if (virtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return ((char)virtualKey).ToString();
            if (virtualKey is >= WindowsKeyMap.F1 and <= WindowsKeyMap.F12)
                return $"F{virtualKey - WindowsKeyMap.F1 + 1}";

            return virtualKey switch
            {
                WindowsKeyMap.Shift => "Shift",
                WindowsKeyMap.Control => "Control",
                WindowsKeyMap.Alt => "Alt",
                WindowsKeyMap.LeftWin => "LWin",
                WindowsKeyMap.Tab => "Tab",
                WindowsKeyMap.Enter => "Enter",
                WindowsKeyMap.Space => "Space",
                WindowsKeyMap.Left => "Left",
                WindowsKeyMap.Right => "Right",
                WindowsKeyMap.Up => "Up",
                WindowsKeyMap.Down => "Down",
                WindowsKeyMap.Home => "Home",
                WindowsKeyMap.End => "End",
                WindowsKeyMap.PageUp => "PageUp",
                WindowsKeyMap.PageDown => "PageDown",
                WindowsKeyMap.Delete => "Delete",
                WindowsKeyMap.Insert => "Insert",
                WindowsKeyMap.Escape => "Escape",
                WindowsKeyMap.NonConvert => "NonConvert",
                WindowsKeyMap.Convert => "Convert",
                _ => $"VK_{virtualKey:X2}"
            };
        }
    }

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        private readonly WindowHandle _secondaryWindow = new(2);
        private readonly HashSet<DesktopMouseButton> _buttons = [];
        private DesktopWindowState _windowState = DesktopWindowState.Normal;
        private DesktopRect _bounds = new(100, 100, 800, 600);
        private readonly DesktopRect _workArea = new(0, 0, 1920, 1080);
        private bool _topMost;
        private byte? _opacity;
        private bool _caption = true;
        private DesktopPoint _pointer = new(400, 300);

        public List<ObservedAction> Actions { get; } = [];

        public void SetInitialMouseButtons(IEnumerable<DesktopMouseButton> buttons)
        {
            _buttons.Clear();
            foreach (var button in buttons)
                _buttons.Add(button);
        }

        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => _windowState;
        public DesktopRect GetWindowBounds(WindowHandle window) => _bounds;
        public DesktopRect GetPrimaryWorkArea() => _workArea;
        public string? GetWindowClass(WindowHandle window) => "iKeydScenarioWindow";
        public bool IsWindow(WindowHandle window) => window == _window || window == _secondaryWindow;

        public void Minimize(WindowHandle window)
        {
            _windowState = DesktopWindowState.Minimized;
            Add("window", "minimize");
        }

        public void Maximize(WindowHandle window)
        {
            _windowState = DesktopWindowState.Maximized;
            Add("window", "maximize");
        }

        public void Restore(WindowHandle window)
        {
            _windowState = DesktopWindowState.Normal;
            Add("window", "restore");
        }

        public void MoveResize(WindowHandle window, DesktopRect bounds)
        {
            _bounds = bounds;
            Add("window", $"move-resize:{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
        }

        public void Activate(WindowHandle window) => Add("window", "activate");
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [_window, _secondaryWindow];

        public bool IsTopMost(WindowHandle window) => _topMost;
        public void SetTopMost(WindowHandle window, bool enabled)
        {
            _topMost = enabled;
            Add("window", $"topmost:{enabled.ToString().ToLowerInvariant()}");
        }

        public byte? GetOpacity(WindowHandle window) => _opacity;
        public void SetOpacity(WindowHandle window, byte? opacity)
        {
            _opacity = opacity;
            Add("window", opacity is null ? "opacity:off" : $"opacity:{opacity.Value}");
        }

        public bool HasCaption(WindowHandle window) => _caption;
        public void SetCaption(WindowHandle window, bool enabled)
        {
            _caption = enabled;
            Add("window", $"caption:{enabled.ToString().ToLowerInvariant()}");
        }

        public DesktopPoint GetPointerPosition() => _pointer;
        public void MovePointer(DesktopPoint position)
        {
            _pointer = position;
            Add("mouse", $"move:{position.X},{position.Y}");
        }

        public void MovePointerBy(int deltaX, int deltaY)
        {
            _pointer = new DesktopPoint(_pointer.X + deltaX, _pointer.Y + deltaY);
            Add("mouse", $"move-by:{deltaX},{deltaY}");
        }

        public bool IsMouseButtonDown(DesktopMouseButton button) => _buttons.Contains(button);
        public void SetMouseButton(DesktopMouseButton button, bool down)
        {
            if (down) _buttons.Add(button); else _buttons.Remove(button);
            Add("mouse", $"button:{button.ToString().ToLowerInvariant()}:{(down ? "down" : "up")}");
        }

        public void Click(DesktopMouseButton button)
            => Add("mouse", $"click:{button.ToString().ToLowerInvariant()}");

        public void ScrollVertical(int wheelDelta, bool controlModifier = false)
            => Add("mouse", $"scroll:{wheelDelta}:{(controlModifier ? "ctrl" : "plain")}");

        public void SendMediaCommand(DesktopMediaCommand command)
            => Add("media", command.ToString());

        private void Add(string kind, string value)
            => Actions.Add(new ObservedAction { Kind = kind, Value = value });
    }
}
