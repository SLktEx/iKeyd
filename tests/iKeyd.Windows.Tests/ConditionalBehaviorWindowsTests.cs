using iKeyd.App;
using iKeyd.Core.Automation;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Automation;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ConditionalBehaviorWindowsTests
{
    [Fact]
    public void Conditional_layer_action_selects_true_and_false_branches_from_snapshot()
    {
        var snapshot = new SystemQuerySnapshotStore();
        var output = new RecordingKeyboardOutput();
        var host = new RecordingHostActionSink();
        var handler = CreateLayerHandler(
            KeyBehaviorAction.When(
                new SystemQueryCondition(SystemQueryKeys.ForegroundProcess, SystemQueryConditionOperator.Equals, "Code.exe"),
                KeyBehaviorAction.Key("Left"),
                KeyBehaviorAction.Key("Right")),
            snapshot,
            output,
            host);

        snapshot.Publish([new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe")]);
        PressLayerAction(handler, 0);
        Assert.Equal([WindowsKeyMap.Left, WindowsKeyMap.Left], output.Events.Select(item => item.VirtualKey).ToArray());

        output.Events.Clear();
        snapshot.Publish([new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "explorer.exe")]);
        PressLayerAction(handler, 100);
        Assert.Equal([WindowsKeyMap.Right, WindowsKeyMap.Right], output.Events.Select(item => item.VirtualKey).ToArray());
        Assert.Empty(host.Actions);
    }

    [Fact]
    public void Missing_snapshot_value_with_no_else_is_a_no_op()
    {
        var output = new RecordingKeyboardOutput();
        var host = new RecordingHostActionSink();
        var handler = CreateLayerHandler(
            KeyBehaviorAction.When(
                new SystemQueryCondition(SystemQueryKeys.ForegroundTitle, SystemQueryConditionOperator.Equals, "target"),
                KeyBehaviorAction.Key("Escape")),
            EmptySystemQuerySnapshot.Instance,
            output,
            host);

        PressLayerAction(handler, 0);

        Assert.Empty(output.Events);
        Assert.Empty(output.Text);
        Assert.Empty(host.Actions);
    }

    [Fact]
    public void Conditional_host_action_uses_existing_host_boundary()
    {
        var snapshot = new SystemQuerySnapshotStore();
        snapshot.Publish([new KeyValuePair<string, string>(SystemQueryKeys.KeyboardCapsLock, "true")]);
        var output = new RecordingKeyboardOutput();
        var host = new RecordingHostActionSink();
        var action = KeyBehaviorAction.When(
            new SystemQueryCondition(SystemQueryKeys.KeyboardCapsLock, SystemQueryConditionOperator.Equals, "true"),
            KeyBehaviorAction.Exec("tool.exe", ["--caps"]),
            KeyBehaviorAction.Key("Escape"));
        var handler = CreateLayerHandler(action, snapshot, output, host);

        PressLayerAction(handler, 0);

        Assert.Single(host.Actions);
        Assert.Equal(KeyBehaviorActionKind.Exec, host.Actions[0].Kind);
        Assert.Equal("tool.exe", host.Actions[0].Value);
        Assert.Equal(["--caps"], host.Actions[0].GetArguments());
        Assert.Empty(output.Events);
    }

    [Fact]
    public void Configured_modifier_applies_to_key_selected_by_condition()
    {
        var snapshot = new SystemQuerySnapshotStore();
        snapshot.Publish([new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe")]);
        var output = new RecordingKeyboardOutput();
        var host = new RecordingHostActionSink();
        var fallback = new RecordingHandler();
        var conditional = KeyBehaviorAction.When(
            new SystemQueryCondition(SystemQueryKeys.ForegroundProcess, SystemQueryConditionOperator.Equals, "Code.exe"),
            KeyBehaviorAction.Key("Left"),
            KeyBehaviorAction.Key("Right"));
        var profile = new KeyBehaviorProfile(
            [
                new KeyBehaviorBinding(KeyCode.A, KeyBehaviorAction.Key("A"), KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control)),
                new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("APP")),
            ],
            [new KeyBehaviorLayer("APP", [new KeyBehaviorLayerBinding(KeyCode.H, conditional)])]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, output, new RecordingDesktopBackend(), snapshot, host, fallback);

        handler.OnKeyboardEvent(Event('A', KeyEventKind.Down, 0));
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, KeyEventOrigin.Physical, 10));
        handler.OnKeyboardEvent(Event('H', KeyEventKind.Down, 20));
        handler.OnKeyboardEvent(Event('H', KeyEventKind.Up, 21));
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Up, KeyEventOrigin.Physical, 30));
        handler.OnKeyboardEvent(Event('A', KeyEventKind.Up, 40));

        Assert.Equal(
            [
                new Observed(WindowsKeyMap.LeftControl, KeyEventKind.Down),
                new Observed(WindowsKeyMap.Left, KeyEventKind.Down),
                new Observed(WindowsKeyMap.Left, KeyEventKind.Up),
                new Observed(WindowsKeyMap.LeftControl, KeyEventKind.Up),
            ],
            output.Events);
        Assert.Empty(host.Actions);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Keyboard_condition_evaluation_does_not_query_provider()
    {
        var provider = new CountingQueryProvider { Value = "Code.exe" };
        using var cache = new WindowsSystemQueryCache(provider, [SystemQueryKeys.ForegroundProcess], TimeSpan.FromDays(1));
        Assert.Equal(1, provider.Calls);
        var output = new RecordingKeyboardOutput();
        var host = new RecordingHostActionSink();
        var handler = CreateLayerHandler(
            KeyBehaviorAction.When(
                new SystemQueryCondition(SystemQueryKeys.ForegroundProcess, SystemQueryConditionOperator.Equals, "Code.exe"),
                KeyBehaviorAction.Key("Escape")),
            cache,
            output,
            host);

        PressLayerAction(handler, 0);

        Assert.Equal(1, provider.Calls);
        Assert.Equal([WindowsKeyMap.Escape, WindowsKeyMap.Escape], output.Events.Select(item => item.VirtualKey).ToArray());
    }

    [Fact]
    public void Query_cache_refreshes_and_retains_previous_value_on_failure()
    {
        var provider = new CountingQueryProvider { Value = "Code.exe" };
        using var cache = new WindowsSystemQueryCache(provider, [SystemQueryKeys.ForegroundProcess], TimeSpan.FromDays(1));
        Assert.True(cache.TryGetValue(SystemQueryKeys.ForegroundProcess, out var first));
        Assert.Equal("Code.exe", first);

        provider.Value = "explorer.exe";
        cache.Refresh();
        Assert.True(cache.TryGetValue(SystemQueryKeys.ForegroundProcess, out var second));
        Assert.Equal("explorer.exe", second);

        provider.Throw = true;
        cache.Refresh();
        Assert.True(cache.TryGetValue(SystemQueryKeys.ForegroundProcess, out var retained));
        Assert.Equal("explorer.exe", retained);
    }

    private static ConfiguredBehaviorKeyboardHandler CreateLayerHandler(
        KeyBehaviorAction action,
        ISystemQuerySnapshot snapshot,
        RecordingKeyboardOutput output,
        RecordingHostActionSink host)
    {
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("APP"))],
            [new KeyBehaviorLayer("APP", [new KeyBehaviorLayerBinding(KeyCode.H, action)])]);
        return new ConfiguredBehaviorKeyboardHandler(
            profile,
            output,
            new RecordingDesktopBackend(),
            snapshot,
            host,
            new RecordingHandler());
    }

    private static void PressLayerAction(ConfiguredBehaviorKeyboardHandler handler, long timestamp)
    {
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, KeyEventOrigin.Physical, timestamp));
        handler.OnKeyboardEvent(Event('H', KeyEventKind.Down, timestamp + 20));
        handler.OnKeyboardEvent(Event('H', KeyEventKind.Up, timestamp + 21));
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Up, KeyEventOrigin.Physical, timestamp + 30));
    }

    private static KeyboardEvent Event(char key, KeyEventKind kind, long timestamp)
        => new(WindowsKeyMap.Keyboard(key), kind, KeyEventOrigin.Physical, timestamp);

    private sealed class CountingQueryProvider : ISystemQueryProvider
    {
        public int Calls { get; private set; }
        public string Value { get; set; } = string.Empty;
        public bool Throw { get; set; }

        public string GetValue(string key)
        {
            Calls++;
            if (Throw)
                throw new InvalidOperationException("query failed");
            return Value;
        }
    }

    private sealed class RecordingHostActionSink : IConfiguredHostActionSink
    {
        public List<KeyBehaviorAction> Actions { get; } = [];
        public void Post(KeyBehaviorAction action) => Actions.Add(action);
    }

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        public List<KeyboardEvent> Events { get; } = [];
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            Events.Add(keyboardEvent);
            return KeyboardDisposition.PassThrough;
        }
    }

    private readonly record struct Observed(ushort VirtualKey, KeyEventKind Kind);

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<Observed> Events { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new Observed(key.VirtualKey, kind));

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private static readonly WindowHandle Active = new(1);
        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 100, 100);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 100, 100);
        public string? GetWindowClass(WindowHandle window) => "Test";
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
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
