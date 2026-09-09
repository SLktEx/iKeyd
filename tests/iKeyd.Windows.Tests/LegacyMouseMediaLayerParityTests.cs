using System.Text.Json;
using iKeyd.App;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyMouseMediaLayerParityTests
{
    private static string ProfilePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
    private static string RuntimeFixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");

    public static IEnumerable<object[]> PinnedMouseMediaCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        foreach (var item in document.RootElement.GetProperty("mouseMediaCases").EnumerateArray())
        {
            yield return new object[]
            {
                item.GetProperty("key").GetString() ?? string.Empty,
                item.GetProperty("action").GetString() ?? string.Empty
            };
        }
    }

    [Fact]
    public void Pinned_mouse_media_fixture_has_the_complete_17_case_dispatch_set()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        var actual = document.RootElement.GetProperty("mouseMediaCases")
            .EnumerateArray()
            .Select(item => $"{item.GetProperty("key").GetString()}:{item.GetProperty("action").GetString()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "j:MouseLeft",
            "k:MouseDown",
            "l:MouseRight",
            "i:MouseUp",
            "u:LeftClick",
            "o:RightClick",
            "p:WheelUp",
            ";:WheelDown",
            "@:Ctrl+WheelUp",
            "::Ctrl+WheelDown",
            ",:MiddleClick",
            "q:VolumeUp",
            "a:VolumeMute",
            "z:VolumeDown",
            "r:MediaNext",
            "f:MediaPlayPause",
            "v:MediaPrevious"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PinnedMouseMediaCases))]
    public void Every_pinned_SM_mouse_media_mapping_is_reachable_through_runtime(
        string physicalKey,
        string legacyAction)
    {
        using var fixture = CreateRuntime();
        EnterSm(fixture);

        Assert.True(WindowsKeyMap.TryResolveCharacter(physicalKey[0], out var key));
        var down = Dispatch(fixture, key, KeyEventKind.Down, 20);
        var up = Dispatch(fixture, key, KeyEventKind.Up, 21);

        Assert.Equal(KeyboardDisposition.Suppress, down);
        Assert.Equal(KeyboardDisposition.Suppress, up);
        Assert.Equal([ExpectedAction(legacyAction)], fixture.Desktop.Actions);
    }

    [Theory]
    [InlineData('D')]
    [InlineData('E')]
    [InlineData('C')]
    public void SM_mouse_speed_modifier_keys_are_consumed_without_leaking(char key)
    {
        using var fixture = CreateRuntime();
        EnterSm(fixture);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Down, 20));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Up, 21));
        Assert.Empty(fixture.Desktop.Actions);
    }

    [Theory]
    [InlineData('Y', "button:left:down", "button:left:up")]
    [InlineData('H', "button:right:down", "button:right:up")]
    public void SM_button_toggle_branches_are_reachable_and_toggle_both_directions(
        char key,
        string firstAction,
        string secondAction)
    {
        using var fixture = CreateRuntime();
        EnterSm(fixture);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Down, 20));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Up, 21));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Down, 30));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Up, 31));

        Assert.Equal([firstAction, secondAction], fixture.Desktop.Actions);
    }

    [Theory]
    [InlineData('N', "move:101,201")]
    [InlineData('M', "move:899,799")]
    public void SM_active_window_corner_branches_are_reachable(char key, string expectedAction)
    {
        using var fixture = CreateRuntime();
        EnterSm(fixture);

        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Down, 20));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(fixture, key, KeyEventKind.Up, 21));
        Assert.Equal([expectedAction], fixture.Desktop.Actions);
    }

    private static string ExpectedAction(string legacyAction)
        => legacyAction switch
        {
            "MouseLeft" => "move-by:-1,0",
            "MouseDown" => "move-by:0,1",
            "MouseRight" => "move-by:1,0",
            "MouseUp" => "move-by:0,-1",
            "LeftClick" => "click:left",
            "RightClick" => "click:right",
            "WheelUp" => "scroll:120:plain",
            "WheelDown" => "scroll:-120:plain",
            "Ctrl+WheelUp" => "scroll:120:ctrl",
            "Ctrl+WheelDown" => "scroll:-120:ctrl",
            "MiddleClick" => "click:middle",
            "VolumeUp" => "media:VolumeUp",
            "VolumeMute" => "media:VolumeMute",
            "VolumeDown" => "media:VolumeDown",
            "MediaNext" => "media:NextTrack",
            "MediaPlayPause" => "media:PlayPause",
            "MediaPrevious" => "media:PreviousTrack",
            _ => throw new InvalidDataException($"Unknown pinned mouse/media action '{legacyAction}'.")
        };

    private static RuntimeFixture CreateRuntime()
    {
        // Keep the periodic pointer timer far outside this deterministic unit test;
        // direction key-down still emits the configured one-pixel tap nudge.
        var mouse = new MouseMotionProfile(
            "virtual_stick",
            60_000,
            45,
            2,
            "smoothstep",
            1000,
            900,
            240,
            4400,
            "neutral",
            1,
            32);
        var configuration = IKeydConfiguration.Load(ProfilePath) with
        {
            StartupMode = InputMode.R,
            Mouse = mouse
        };
        var keyboardState = new KeyboardState();
        var desktop = new RecordingDesktopBackend();
        var runtime = new IKeydRuntimeHandler(
            configuration,
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(new NullKeyboardOutput()),
            desktop);
        return new RuntimeFixture(runtime, keyboardState, desktop);
    }

    private static void EnterSm(RuntimeFixture fixture)
    {
        Assert.Equal(
            KeyboardDisposition.Suppress,
            Dispatch(fixture, WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, 0));
        Assert.Equal(
            KeyboardDisposition.Suppress,
            Dispatch(fixture, WindowsKeyMap.Keyboard(WindowsKeyMap.NonConvert), KeyEventKind.Down, 10));
    }

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        char key,
        KeyEventKind kind,
        long timestamp)
    {
        Assert.True(WindowsKeyMap.TryResolveCharacter(key, out var physical));
        return Dispatch(fixture, physical, kind, timestamp);
    }

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        KeyboardKey key,
        KeyEventKind kind,
        long timestamp)
    {
        var keyboardEvent = new KeyboardEvent(key, kind, KeyEventOrigin.Physical, timestamp);
        fixture.KeyboardState.Apply(keyboardEvent);
        return fixture.Runtime.OnKeyboardEvent(keyboardEvent);
    }

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        KeyboardState KeyboardState,
        RecordingDesktopBackend Desktop) : IDisposable
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

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        private readonly HashSet<DesktopMouseButton> _buttons = [];

        public List<string> Actions { get; } = [];

        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(100, 200, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "MouseMediaParityTest";
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
        public DesktopPoint GetPointerPosition() => new(400, 300);
        public void MovePointer(DesktopPoint position) => Actions.Add($"move:{position.X},{position.Y}");
        public void MovePointerBy(int deltaX, int deltaY) => Actions.Add($"move-by:{deltaX},{deltaY}");
        public bool IsMouseButtonDown(DesktopMouseButton button) => _buttons.Contains(button);
        public void SetMouseButton(DesktopMouseButton button, bool down)
        {
            if (down)
                _buttons.Add(button);
            else
                _buttons.Remove(button);
            Actions.Add($"button:{button.ToString().ToLowerInvariant()}:{(down ? "down" : "up")}");
        }
        public void Click(DesktopMouseButton button)
            => Actions.Add($"click:{button.ToString().ToLowerInvariant()}");
        public void ScrollVertical(int wheelDelta, bool controlModifier = false)
            => Actions.Add($"scroll:{wheelDelta}:{(controlModifier ? "ctrl" : "plain")}");
        public void SendMediaCommand(DesktopMediaCommand command)
            => Actions.Add($"media:{command}");
    }
}
