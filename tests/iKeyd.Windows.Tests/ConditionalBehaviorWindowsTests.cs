using iKeyd.App;
using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Windows.Automation;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ConditionalBehaviorWindowsTests
{
    [Fact]
    public void Query_cache_refreshes_and_retains_previous_value_on_failure()
    {
        var provider = new CountingQueryProvider { Value = "Code.exe" };
        using var cache = new WindowsSystemQueryCache(
            provider,
            [SystemQueryKeys.ForegroundProcess],
            TimeSpan.FromDays(1));

        Assert.Equal(1, provider.Calls);
        Assert.True(cache.TryGetValue(SystemQueryKeys.ForegroundProcess, out var first));
        Assert.Equal("Code.exe", first);

        provider.Value = "explorer.exe";
        cache.Refresh();
        Assert.Equal(2, provider.Calls);
        Assert.True(cache.TryGetValue(SystemQueryKeys.ForegroundProcess, out var second));
        Assert.Equal("explorer.exe", second);

        provider.Throw = true;
        cache.Refresh();
        Assert.Equal(3, provider.Calls);
        Assert.True(cache.TryGetValue(SystemQueryKeys.ForegroundProcess, out var retained));
        Assert.Equal("explorer.exe", retained);
    }

    [Fact]
    public void Query_cache_only_refreshes_keys_required_by_the_profile()
    {
        var profile = ProfileWithWhen(
            query: SystemQueryKeys.ForegroundProcess,
            thenKind: "query",
            thenValue: SystemQueryKeys.ForegroundTitle,
            elseKind: "key",
            elseValue: "Escape");
        var provider = new MultiQueryProvider();

        using var cache = new WindowsSystemQueryCache(
            provider,
            profile.SystemQueries,
            TimeSpan.FromDays(1));

        Assert.Equal(
            [SystemQueryKeys.ForegroundProcess, SystemQueryKeys.ForegroundTitle],
            cache.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(1, provider.Calls[SystemQueryKeys.ForegroundProcess]);
        Assert.Equal(1, provider.Calls[SystemQueryKeys.ForegroundTitle]);
        Assert.False(provider.Calls.ContainsKey(SystemQueryKeys.Hostname));
    }

    [Fact]
    public void Behavior_router_selects_true_and_false_branches_from_snapshot_only()
    {
        var snapshot = new SystemQuerySnapshotStore();
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = ProfileWithWhen(
            query: SystemQueryKeys.ForegroundProcess,
            thenKind: "key",
            thenValue: "Escape",
            elseKind: "key",
            elseValue: "F1");

        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            systemQueries: snapshot);

        snapshot.Publish([
            new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe")
        ]);
        PressA(router, 0);
        Assert.Equal([WindowsKeyMap.Escape], keyboard.Presses.Select(key => key.VirtualKey).ToArray());

        keyboard.Presses.Clear();
        snapshot.Publish([
            new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "explorer.exe")
        ]);
        PressA(router, 100);
        Assert.Equal([WindowsKeyMap.F1], keyboard.Presses.Select(key => key.VirtualKey).ToArray());
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Keyboard_condition_evaluation_does_not_call_query_provider()
    {
        var provider = new CountingQueryProvider { Value = "Code.exe" };
        using var cache = new WindowsSystemQueryCache(
            provider,
            [SystemQueryKeys.ForegroundProcess],
            TimeSpan.FromDays(1));
        Assert.Equal(1, provider.Calls);

        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = ProfileWithWhen(
            query: SystemQueryKeys.ForegroundProcess,
            thenKind: "key",
            thenValue: "Escape");
        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            systemQueries: cache);

        PressA(router, 0);
        PressA(router, 100);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(
            [WindowsKeyMap.Escape, WindowsKeyMap.Escape],
            keyboard.Presses.Select(key => key.VirtualKey).ToArray());
    }

    [Fact]
    public void Conditional_host_branch_posts_through_existing_host_action_boundary()
    {
        var snapshot = new SystemQuerySnapshotStore();
        snapshot.Publish([
            new KeyValuePair<string, string>(SystemQueryKeys.KeyboardCapsLock, "true")
        ]);
        var posted = new List<BehaviorAction>();
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = ProfileWithWhen(
            query: SystemQueryKeys.KeyboardCapsLock,
            expected: "true",
            thenKind: "exec",
            thenValue: "tool.exe");
        var mapping = profile.GetKeymap("S").BehaviorMappings.Single();
        var options = mapping.Invocation.Options.ToDictionary(pair => pair.Key, pair => pair.Value);
        options["then_arg0"] = "--caps";
        profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("WHEN", [], options))]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            posted.Add,
            snapshot);

        PressA(router, 0);

        var action = Assert.Single(posted);
        Assert.Equal(BehaviorActionKind.Exec, action.Kind);
        Assert.Equal("tool.exe", action.Name);
        Assert.Equal(["--caps"], action.Arguments);
        Assert.Empty(keyboard.Presses);
    }

    private static AutomationProfile ProfileWithWhen(
        string query,
        string thenKind,
        string thenValue,
        string expected = "Code.exe",
        string? elseKind = null,
        string? elseValue = null)
    {
        var options = new Dictionary<string, string>
        {
            ["query"] = query,
            ["operator"] = "equals",
            ["expected"] = expected,
            ["then_kind"] = thenKind,
            ["then_value"] = thenValue
        };
        if (elseKind is not null)
            options["else_kind"] = elseKind;
        if (elseValue is not null)
            options["else_value"] = elseValue;

        return new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("WHEN", [], options))]),
                new AutomationKeymapProfile("K", [], [])
            ]);
    }

    private static void PressA(BehaviorWindowsInputRouter router, long timestamp)
    {
        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, timestamp)));
        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, timestamp + 1)));
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

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

    private sealed class MultiQueryProvider : ISystemQueryProvider
    {
        public Dictionary<string, int> Calls { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string GetValue(string key)
        {
            Calls[key] = Calls.TryGetValue(key, out var count) ? count + 1 : 1;
            return key;
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<KeyboardKey> Presses { get; } = [];
        public List<string> Text { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) => Presses.Add(key);
        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
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
}
