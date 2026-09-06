using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ConditionalBehaviorTests
{
    [Fact]
    public void Conditions_support_equals_not_equals_and_missing_values()
    {
        var snapshot = new SystemQuerySnapshotStore();
        snapshot.Publish([
            new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe"),
            new KeyValuePair<string, string>(SystemQueryKeys.KeyboardCapsLock, "true")
        ]);

        Assert.True(new SystemQueryCondition(
            SystemQueryKeys.ForegroundProcess,
            SystemQueryConditionOperator.Equals,
            "code.EXE").Evaluate(snapshot));
        Assert.True(new SystemQueryCondition(
            SystemQueryKeys.ForegroundProcess,
            SystemQueryConditionOperator.NotEquals,
            "explorer.exe").Evaluate(snapshot));
        Assert.True(new SystemQueryCondition(
            SystemQueryKeys.KeyboardCapsLock,
            SystemQueryConditionOperator.Equals,
            "TRUE").Evaluate(snapshot));

        // Missing is always false, including !=, so stale/unavailable host state
        // cannot accidentally select a negative branch.
        Assert.False(new SystemQueryCondition(
            SystemQueryKeys.ForegroundTitle,
            SystemQueryConditionOperator.NotEquals,
            "anything").Evaluate(snapshot));
    }

    [Fact]
    public void When_selects_snapshot_branch_once_and_does_not_repeat()
    {
        var snapshot = new SystemQuerySnapshotStore();
        snapshot.Publish([
            new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe")
        ]);
        var invocation = WhenInvocation(
            query: SystemQueryKeys.ForegroundProcess,
            thenKind: "key",
            thenValue: "Escape",
            elseKind: "key",
            elseValue: "F1");
        var source = new KeyId("A");
        var runtime = new BehaviorRuntime(new Dictionary<KeyId, BehaviorDefinition>
        {
            [source] = BehaviorDefinitionFactory.Create(
                invocation,
                new Dictionary<string, UserBehaviorDefinitionProfile>(),
                snapshot)
        });

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 10);
        var up = runtime.OnKeyUp(source, 20);

        Assert.Equal([BehaviorAction.SendKey(new KeyId("Escape"))], down.Actions);
        Assert.Empty(repeat.Actions);
        Assert.True(repeat.Suppress);
        Assert.True(up.Suppress);
    }

    [Fact]
    public void When_supports_nested_conditions_and_optional_else()
    {
        var snapshot = new SystemQuerySnapshotStore();
        snapshot.Publish([
            new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe"),
            new KeyValuePair<string, string>(SystemQueryKeys.KeyboardCapsLock, "true")
        ]);
        var invocation = new BehaviorInvocationProfile(
            "WHEN",
            [],
            new Dictionary<string, string>
            {
                ["query"] = SystemQueryKeys.ForegroundProcess,
                ["operator"] = "equals",
                ["expected"] = "Code.exe",
                ["then_kind"] = "when",
                ["then_query"] = SystemQueryKeys.KeyboardCapsLock,
                ["then_operator"] = "equals",
                ["then_expected"] = "true",
                ["then_then_kind"] = "text",
                ["then_then_value"] = "nested true",
                ["then_else_kind"] = "text",
                ["then_else_value"] = "nested false"
            });
        var source = new KeyId("A");
        var runtime = new BehaviorRuntime(new Dictionary<KeyId, BehaviorDefinition>
        {
            [source] = BehaviorDefinitionFactory.Create(
                invocation,
                new Dictionary<string, UserBehaviorDefinitionProfile>(),
                snapshot)
        });

        var down = runtime.OnKeyDown(source, 0);

        Assert.Equal([BehaviorAction.SendText("nested true")], down.Actions);
    }

    [Fact]
    public void Profile_collects_only_queries_required_by_query_and_nested_when_actions()
    {
        var when = new BehaviorInvocationProfile(
            "WHEN",
            [],
            new Dictionary<string, string>
            {
                ["query"] = SystemQueryKeys.ForegroundProcess,
                ["operator"] = "equals",
                ["expected"] = "Code.exe",
                ["then_kind"] = "query",
                ["then_value"] = SystemQueryKeys.ForegroundTitle,
                ["else_kind"] = "when",
                ["else_query"] = SystemQueryKeys.KeyboardNumLock,
                ["else_operator"] = "equals",
                ["else_expected"] = "true",
                ["else_then_kind"] = "key",
                ["else_then_value"] = "F3"
            });
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [
                        new BehaviorMappingProfile("A", when),
                        new BehaviorMappingProfile(
                            "B",
                            new BehaviorInvocationProfile(
                                "QUERY",
                                [],
                                new Dictionary<string, string> { ["key"] = SystemQueryKeys.Hostname }))
                    ]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        Assert.Equal(
            [
                SystemQueryKeys.ForegroundProcess,
                SystemQueryKeys.ForegroundTitle,
                SystemQueryKeys.KeyboardNumLock,
                SystemQueryKeys.Hostname
            ],
            profile.SystemQueries);
    }

    [Fact]
    public void Canonical_dsl_preserves_when_semantics_in_static_and_json_representations()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        keymap S {
            A = WHEN() {
                query = foreground.process
                operator = equals
                expected = "Code.exe"
                then_kind = exec
                then_value = "tool.exe"
                then_arg0 = "--from-when"
                else_kind = text
                else_value = "fallback"
            }
        }

        keymap K {
            A = "a"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "conditional.ikeyd");
        var invocation = Assert.Single(document.Profile.GetKeymap("S").BehaviorMappings).Invocation;
        Assert.Equal("foreground.process", invocation.Options["query"]);
        Assert.Equal("tool.exe", invocation.Options["then_value"]);
        Assert.Equal("--from-when", invocation.Options["then_arg0"]);
        Assert.Equal([SystemQueryKeys.ForegroundProcess], document.Profile.SystemQueries);

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("\"WHEN\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"then_arg0\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"--from-when\"", generated, StringComparison.Ordinal);

        var roundTrip = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(document.Profile));
        var roundTripInvocation = Assert.Single(roundTrip.GetKeymap("S").BehaviorMappings).Invocation;
        Assert.Equal(invocation.Options, roundTripInvocation.Options);
    }

    [Theory]
    [InlineData("bogus", "Unknown condition operator")]
    [InlineData("equals", "Unsupported system query")]
    public void Canonical_dsl_rejects_invalid_when_payloads_with_source_context(
        string conditionOperator,
        string expectedMessage)
    {
        var query = expectedMessage.Contains("system query", StringComparison.OrdinalIgnoreCase)
            ? "foreground.not_real"
            : SystemQueryKeys.ForegroundProcess;
        var source = $$"""
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }
        keymap S {
            A = WHEN() {
                query = {{query}}
                operator = {{conditionOperator}}
                expected = "Code.exe"
                then_kind = key
                then_value = Escape
            }
        }
        keymap K {
            A = "a"
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(source, "bad-when.ikeyd"));

        Assert.Contains("bad-when.ikeyd", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static BehaviorInvocationProfile WhenInvocation(
        string query,
        string thenKind,
        string thenValue,
        string? elseKind = null,
        string? elseValue = null)
    {
        var options = new Dictionary<string, string>
        {
            ["query"] = query,
            ["operator"] = "equals",
            ["expected"] = "Code.exe",
            ["then_kind"] = thenKind,
            ["then_value"] = thenValue
        };
        if (elseKind is not null)
            options["else_kind"] = elseKind;
        if (elseValue is not null)
            options["else_value"] = elseValue;
        return new BehaviorInvocationProfile("WHEN", [], options);
    }
}
