using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Platform;
using iKeyd.Wayland.Clipboard;
using iKeyd.Wayland.Desktop;
using iKeyd.Wayland.Input;
using Xunit;

namespace iKeyd.Wayland.Tests;

public sealed class WaylandBackendTests
{
    [Fact]
    public void Evdev_map_normalizes_common_keyboard_keys()
    {
        var map = new LinuxEvdevKeyMap();

        Assert.True(map.TryFromEvdev(30, out var a));
        Assert.Equal((ushort)'A', a.VirtualKey);
        Assert.Equal((ushort)30, a.ScanCode);

        Assert.True(map.TryFromEvdev(94, out var muhenkan));
        Assert.Equal((ushort)0x1D, muhenkan.VirtualKey);

        Assert.True(map.TryToEvdev(new KeyboardKey(0x26, 0), out var up));
        Assert.Equal((ushort)103, up);
    }

    [Theory]
    [InlineData('a', 30, false)]
    [InlineData('A', 30, true)]
    [InlineData('1', 2, false)]
    [InlineData('!', 2, true)]
    [InlineData('/', 53, false)]
    public void Ascii_output_is_reduced_to_evdev_strokes(char character, ushort expectedCode, bool expectedShift)
    {
        var map = new LinuxEvdevKeyMap();

        Assert.True(map.TryGetAsciiStroke(character, out var code, out var shift));
        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedShift, shift);
    }

    [Fact]
    public void Capability_model_reports_unsupported_operations_explicitly()
    {
        var capabilities = new BackendCapabilities([BackendCapability.KeyboardInput]);

        Assert.True(capabilities.Supports(BackendCapability.KeyboardInput));
        Assert.False(capabilities.Supports(BackendCapability.WindowMoveResize));
        var error = Assert.Throws<BackendCapabilityException>(
            () => capabilities.Require(BackendCapability.WindowMoveResize, "compositor adapter required"));
        Assert.Equal(BackendCapability.WindowMoveResize, error.Capability);
        Assert.Contains("compositor adapter required", error.Message);
    }

    [Fact]
    public void Clipboard_adapter_uses_commands_only_when_capability_is_available()
    {
        var runner = new FakeCommandRunner { CurrentText = "before" };
        var options = new WaylandBackendOptions([], WlCopyCommand: "copy", WlPasteCommand: "paste");
        using var clipboard = new WaylandClipboardService(
            options,
            runner,
            TimeSpan.FromHours(1),
            isWaylandSession: true);

        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardRead));
        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardWrite));
        Assert.Equal("before", clipboard.ReadText());

        clipboard.WriteText("after");
        Assert.Equal("after", runner.CurrentText);
        Assert.Equal("after", clipboard.ReadText());
    }

    [Fact]
    public void Clipboard_watch_raises_changed_when_selection_changes()
    {
        var runner = new FakeCommandRunner { CurrentText = "one" };
        var options = new WaylandBackendOptions([], WlCopyCommand: "copy", WlPasteCommand: "paste");
        using var clipboard = new WaylandClipboardService(
            options,
            runner,
            TimeSpan.FromMilliseconds(10),
            isWaylandSession: true);
        using var changed = new ManualResetEventSlim();
        clipboard.Changed += (_, _) => changed.Set();

        runner.CurrentText = "two";

        Assert.True(changed.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal("two", clipboard.ReadText());
    }

    [Fact]
    public void Clipboard_without_wayland_session_exposes_no_false_capabilities()
    {
        var runner = new FakeCommandRunner();
        var options = new WaylandBackendOptions([], WlCopyCommand: "copy", WlPasteCommand: "paste");
        using var clipboard = new WaylandClipboardService(
            options,
            runner,
            TimeSpan.FromHours(1),
            isWaylandSession: false);

        Assert.False(clipboard.Capabilities.Supports(BackendCapability.ClipboardRead));
        var error = Assert.Throws<BackendCapabilityException>(() => clipboard.ReadText());
        Assert.Equal(BackendCapability.ClipboardRead, error.Capability);
    }

    [Fact]
    public void Desktop_backend_routes_generic_pointer_operations_and_rejects_window_operations()
    {
        var input = new FakeVirtualInput();
        var desktop = new WaylandDesktopBackend(input);

        desktop.MovePointerBy(12, -8);
        desktop.Click(DesktopMouseButton.Left);
        desktop.ScrollVertical(240, controlModifier: true);
        desktop.SendMediaCommand(DesktopMediaCommand.PlayPause);

        Assert.Contains((12, -8), input.Moves);
        Assert.Single(input.Clicks);
        Assert.Contains(2, input.Scrolls);
        Assert.Equal(2, input.KeyEvents.Count); // Ctrl down/up around scroll.
        Assert.Single(input.MediaKeys);

        var error = Assert.Throws<BackendCapabilityException>(() => desktop.Minimize(default));
        Assert.Equal(BackendCapability.WindowState, error.Capability);
        Assert.False(desktop.Capabilities.Supports(BackendCapability.WindowQuery));
    }

    private sealed class FakeCommandRunner : IWaylandCommandRunner
    {
        private readonly object _gate = new();
        public string? CurrentText { get; set; }

        public bool Exists(string command) => command is "copy" or "paste";

        public WaylandCommandResult Run(
            string command,
            IReadOnlyList<string> arguments,
            string? standardInput = null,
            TimeSpan? timeout = null)
        {
            lock (_gate)
            {
                if (command == "copy")
                {
                    CurrentText = standardInput ?? string.Empty;
                    return new WaylandCommandResult(0, string.Empty, string.Empty);
                }
                if (command == "paste")
                    return new WaylandCommandResult(0, CurrentText ?? string.Empty, string.Empty);
                return new WaylandCommandResult(127, string.Empty, "not found");
            }
        }
    }

    private sealed class FakeVirtualInput : IWaylandVirtualInput
    {
        public List<(int X, int Y)> Moves { get; } = [];
        public List<ushort> Clicks { get; } = [];
        public List<int> Scrolls { get; } = [];
        public List<ushort> MediaKeys { get; } = [];
        public List<(KeyboardKey Key, KeyEventKind Kind)> KeyEvents { get; } = [];

        public BackendCapabilities Capabilities { get; } = new([
            BackendCapability.KeyboardOutput,
            BackendCapability.TextOutputAscii,
            BackendCapability.PointerRelative,
            BackendCapability.PointerButtons,
            BackendCapability.PointerScroll,
            BackendCapability.MediaKeys
        ]);

        public void EmitKeyCode(ushort evdevCode, int value) { }
        public void MovePointerBy(int deltaX, int deltaY) => Moves.Add((deltaX, deltaY));
        public void SetMouseButton(ushort buttonCode, bool down) { }
        public void ClickMouseButton(ushort buttonCode) => Clicks.Add(buttonCode);
        public void ScrollVertical(int wheelClicks) => Scrolls.Add(wheelClicks);
        public void SendMediaKey(ushort keyCode) => MediaKeys.Add(keyCode);
        public void SendKey(KeyboardKey key, KeyEventKind kind) => KeyEvents.Add((key, kind));
        public void SendKeyPress(KeyboardKey key) { SendKey(key, KeyEventKind.Down); SendKey(key, KeyEventKind.Up); }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }
}
