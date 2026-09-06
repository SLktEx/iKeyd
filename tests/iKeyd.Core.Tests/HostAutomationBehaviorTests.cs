using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class HostAutomationBehaviorTests
{
    [Fact]
    public void Exec_action_preserves_argv_and_does_not_repeat()
    {
        var source = new KeyId("A");
        var definition = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile("EXEC", ["tool.exe", "--name", "hello world"]));
        var runtime = new BehaviorRuntime(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 20);
        var up = runtime.OnKeyUp(source, 30);

        var action = Assert.Single(down.Actions);
        Assert.Equal(BehaviorActionKind.Exec, action.Kind);
        Assert.Equal("tool.exe", action.Name);
        Assert.Equal(["--name", "hello world"], action.Arguments);
        Assert.Equal(BehaviorRepeatPolicy.Never, action.RepeatPolicy);
        Assert.Empty(repeat.Actions);
        Assert.True(repeat.Suppress);
        Assert.True(up.Suppress);
    }

    [Fact]
    public void System_query_registry_is_case_insensitive_and_rejects_unknown_keys()
    {
        Assert.Equal(SystemQueryKeys.ForegroundProcess, SystemQueryKeys.Normalize("FOREGROUND.PROCESS"));
        Assert.Contains(SystemQueryKeys.ImeKanaActive, SystemQueryKeys.All);
        Assert.Throws<ArgumentException>(() => SystemQueryKeys.Normalize("system.not-real"));
    }

    [Fact]
    public void Canonical_dsl_preserves_exec_shell_and_query_without_json_build_hop()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        keymap S {
            A = EXEC() {
                executable = "tool.exe"
                arg0 = "--name"
                arg1 = "hello world"
            }
            B = SHELL() {
                command = "echo hello | more"
            }
            C = QUERY() {
                key = foreground.process
            }
        }

        keymap K {
            A = "a"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "host-actions.ikeyd");
        var mappings = document.Profile.GetKeymap("S").BehaviorMappings;
        Assert.Equal(3, mappings.Count);

        var exec = mappings.Single(mapping => mapping.Key.Value == "A").Invocation;
        Assert.True(string.Equals("EXEC", exec.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("tool.exe", exec.Options["executable"]);
        Assert.Equal("--name", exec.Options["arg0"]);
        Assert.Equal("hello world", exec.Options["arg1"]);

        var shell = mappings.Single(mapping => mapping.Key.Value == "B").Invocation;
        Assert.Equal("echo hello | more", shell.Options["command"]);

        var query = mappings.Single(mapping => mapping.Key.Value == "C").Invocation;
        Assert.Equal(SystemQueryKeys.ForegroundProcess, query.Options["key"]);

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("\"EXEC\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"tool.exe\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"hello world\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"foreground.process\"", generated, StringComparison.Ordinal);

        var roundTrip = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(document.Profile));
        var roundTripExec = roundTrip.GetKeymap("S").BehaviorMappings
            .Single(mapping => mapping.Key.Value == "A").Invocation;
        Assert.Equal("tool.exe", roundTripExec.Options["executable"]);
        Assert.Equal("hello world", roundTripExec.Options["arg1"]);
    }

    [Fact]
    public void Canonical_dsl_rejects_unknown_query_at_check_time()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }
        keymap S {
            A = QUERY() {
                key = foreground.not_real
            }
        }
        keymap K {
            A = "a"
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(source, "bad-query.ikeyd"));

        Assert.Contains("Unsupported system query", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exec_option_arguments_must_be_contiguous()
    {
        var invocation = new BehaviorInvocationProfile(
            "EXEC",
            [],
            new Dictionary<string, string>
            {
                ["executable"] = "tool.exe",
                ["arg1"] = "orphan"
            });

        var error = Assert.Throws<InvalidDataException>(() => BehaviorDefinitionFactory.Create(invocation));
        Assert.Contains("missing arg0", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
