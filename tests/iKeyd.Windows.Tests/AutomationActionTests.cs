using iKeyd.App;
using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Windows.Automation;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class AutomationActionTests
{
    [Fact]
    public void Exec_start_info_preserves_literal_argv_without_shell_joining()
    {
        var request = CommandRequest.Exec("tool.exe", ["--name", "hello world", "a&b"]);

        var startInfo = WindowsCommandActionQueue.CreateStartInfo(request);

        Assert.Equal("tool.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["--name", "hello world", "a&b"], startInfo.ArgumentList);
    }

    [Fact]
    public void Shell_start_info_is_an_explicit_command_interpreter_boundary()
    {
        var startInfo = WindowsCommandActionQueue.CreateStartInfo(CommandRequest.Shell("echo hello | more"));

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("/d", startInfo.ArgumentList[0]);
        Assert.Equal("/s", startInfo.ArgumentList[1]);
        Assert.Equal("/c", startInfo.ArgumentList[2]);
        Assert.Equal("echo hello | more", startInfo.ArgumentList[3]);
    }

    [Fact]
    public void Bounded_queue_returns_false_instead_of_blocking_when_full()
    {
        using var started = new ManualResetEventSlim();
        using var queue = new WindowsCommandActionQueue(
            capacity: 1,
            execute: async (request, cancellationToken) =>
            {
                started.Set();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new CommandResult(request, 0, string.Empty, string.Empty, null);
            });

        Assert.True(queue.TryEnqueue(CommandRequest.Exec("first.exe")));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(queue.TryEnqueue(CommandRequest.Exec("second.exe")));
        Assert.False(queue.TryEnqueue(CommandRequest.Exec("third.exe")));
    }

    [Fact]
    public void Query_provider_handles_platform_neutral_environment_and_ime_keys()
    {
        var provider = new WindowsSystemQueryProvider(new FakeInputMethod(true));

        Assert.Equal(Environment.MachineName, provider.GetValue(SystemQueryKeys.Hostname));
        Assert.Equal(Environment.UserName, provider.GetValue(SystemQueryKeys.Username));
        Assert.Equal("true", provider.GetValue(SystemQueryKeys.ImeKanaActive));
    }

    [Fact]
    public void Behavior_router_posts_exec_once_and_does_not_repeat_it()
    {
        var posted = new List<BehaviorAction>();
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile(
                        "A",
                        new BehaviorInvocationProfile(
                            "EXEC",
                            [],
                            new Dictionary<string, string>
                            {
                                ["executable"] = "tool.exe",
                                ["arg0"] = "hello world"
                            }))]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            posted.Add);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30)));

        var action = Assert.Single(posted);
        Assert.Equal(BehaviorActionKind.Exec, action.Kind);
        Assert.Equal("tool.exe", action.Name);
        Assert.Equal(["hello world"], action.Arguments);
        Assert.Empty(keyboard.Text);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Behavior_router_posts_query_without_running_a_provider_inline()
    {
        var posted = new List<BehaviorAction>();
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile(
                        "A",
                        new BehaviorInvocationProfile(
                            "QUERY",
                            [],
                            new Dictionary<string, string> { ["key"] = SystemQueryKeys.ForegroundProcess }))]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            posted.Add);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0));

        var action = Assert.Single(posted);
        Assert.Equal(BehaviorActionKind.Query, action.Kind);
        Assert.Equal(SystemQueryKeys.ForegroundProcess, action.Name);
        Assert.Empty(keyboard.Text);
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

    private sealed class FakeInputMethod(bool active) : IInputMethod
    {
        public bool IsKanaInputActive() => active;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<string> Text { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
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
