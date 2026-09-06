using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.State;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class RuntimeStateTests
{
    [Fact]
    public void Store_initializes_sets_toggles_and_resets_typed_fields()
    {
        var profile = new RuntimeStateProfile([
            RuntimeStateFieldProfile.String("mode", "normal"),
            RuntimeStateFieldProfile.Bool("nav_locked", false)
        ]);
        var store = new RuntimeStateStore(profile);

        Assert.True(store.TryGetScalar("state.mode", out var initialMode));
        Assert.Equal("normal", initialMode);
        Assert.True(store.TryGetScalar("nav_locked", out var initialFlag));
        Assert.Equal("false", initialFlag);

        store.SetScalar("mode", "coding");
        store.Toggle("nav_locked");
        Assert.True(store.TryGetScalar("mode", out var changedMode));
        Assert.Equal("coding", changedMode);
        Assert.True(store.TryGetScalar("nav_locked", out var changedFlag));
        Assert.Equal("true", changedFlag);

        store.Reset();
        Assert.True(store.TryGetScalar("mode", out var resetMode));
        Assert.Equal("normal", resetMode);
        Assert.True(store.TryGetScalar("nav_locked", out var resetFlag));
        Assert.Equal("false", resetFlag);

        Assert.Throws<ArgumentException>(() => store.SetScalar("nav_locked", "not-a-bool"));
        Assert.Throws<InvalidOperationException>(() => store.Toggle("mode"));
    }

    [Fact]
    public void Canonical_dsl_compiles_state_set_toggle_and_state_when()
    {
        const string source = """
        state {
            mode: string = "normal"
            nav_locked: bool = false
        }

        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        keymap S {
            A = SET() {
                state = mode
                value = "coding"
            }
            B = WHEN() {
                state = mode
                operator = equals
                expected = "coding"
                then_kind = key
                then_value = Escape
                else_kind = key
                else_value = F1
            }
            C = TOGGLE() {
                state = nav_locked
            }
            D = WHEN() {
                state = nav_locked
                operator = equals
                expected = true
                then_kind = text
                then_value = "locked"
                else_kind = text
                else_value = "open"
            }
        }

        keymap K {
            A = "a"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "state.ikeyd");
        Assert.Equal(2, document.Profile.State.Count);
        Assert.Equal(RuntimeStateType.String, document.Profile.State.GetField("mode").Type);
        Assert.Equal(RuntimeStateType.Bool, document.Profile.State.GetField("nav_locked").Type);
        Assert.Empty(document.Profile.SystemQueries);

        var store = new RuntimeStateStore(document.Profile.State);
        var runtime = new BehaviorRuntime(document.Profile.GetKeymap("S").BuildBehaviorBindings(
            document.Profile.BehaviorDefinitions,
            EmptySystemQuerySnapshot.Instance,
            document.Profile.State,
            store));

        Assert.Equal(BehaviorActionKind.StateSet, Assert.Single(runtime.OnKeyDown("A", 0).Actions).Kind);
        store.SetScalar("mode", "coding");
        Assert.Equal(new KeyId("Escape"), Assert.Single(runtime.OnKeyDown("B", 10).Actions).Key);
        _ = runtime.OnKeyUp("A", 20);
        _ = runtime.OnKeyUp("B", 21);

        var toggle = Assert.Single(runtime.OnKeyDown("C", 30).Actions);
        Assert.Equal(BehaviorActionKind.StateToggle, toggle.Kind);
        store.Toggle(toggle.Name!);
        var text = Assert.Single(runtime.OnKeyDown("D", 40).Actions);
        Assert.Equal(BehaviorActionKind.SendText, text.Kind);
        Assert.Equal("locked", text.Text);

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("RuntimeStateFieldProfile.String(\"mode\", \"normal\")", generated, StringComparison.Ordinal);
        Assert.Contains("RuntimeStateFieldProfile.Bool(\"nav_locked\", false)", generated, StringComparison.Ordinal);
        Assert.Contains("\"StateSet\"", BehaviorActionKind.StateSet.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Custom_behavior_can_mutate_and_branch_on_shared_state()
    {
        const string source = """
        state {
            mode: string = "normal"
            nav_locked: bool = false
        }

        behavior STATEFUL() {
            on_press {
                state.set(mode, "coding")
                state.toggle(nav_locked)
                if state.mode == "coding" {
                    send Escape
                } else {
                    send F1
                }
                if state.nav_locked != false {
                    send Left
                }
            }
        }

        profile demo {
            chord_window = 40ms
            startup_mode = S
        }
        keymap S {
            A = STATEFUL()
        }
        keymap K {
            A = "a"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "custom-state.ikeyd");
        var definition = Assert.Single(document.Profile.BehaviorDefinitions.Values);
        var press = Assert.Single(definition.Handlers);
        Assert.Contains(press.Statements, statement => statement.Op == "state_set");
        Assert.Contains(press.Statements, statement => statement.Op == "state_toggle");
        Assert.Contains(press.Statements, statement => statement.Op == "if_state_equals");
        Assert.Contains(press.Statements, statement => statement.Op == "if_state_not_equals");

        var store = new RuntimeStateStore(document.Profile.State);
        var runtime = new BehaviorRuntime(document.Profile.GetKeymap("S").BuildBehaviorBindings(
            document.Profile.BehaviorDefinitions,
            EmptySystemQuerySnapshot.Instance,
            document.Profile.State,
            store));

        var down = runtime.OnKeyDown("A", 0);
        Assert.Equal(
            [new KeyId("Escape"), new KeyId("Left")],
            down.Actions.Where(action => action.Kind == BehaviorActionKind.SendKey).Select(action => action.Key).ToArray());
        Assert.True(store.TryGetScalar("mode", out var mode));
        Assert.Equal("coding", mode);
        Assert.True(store.TryGetScalar("nav_locked", out var flag));
        Assert.Equal("true", flag);
    }

    [Fact]
    public void State_and_cached_system_conditions_compose_in_one_when_tree()
    {
        var stateProfile = new RuntimeStateProfile([RuntimeStateFieldProfile.Bool("armed", true)]);
        var state = new RuntimeStateStore(stateProfile);
        var system = new SystemQuerySnapshotStore();
        system.Publish([new KeyValuePair<string, string>(SystemQueryKeys.ForegroundProcess, "Code.exe")]);
        var invocation = new BehaviorInvocationProfile(
            "WHEN",
            [],
            new Dictionary<string, string>
            {
                ["query"] = SystemQueryKeys.ForegroundProcess,
                ["operator"] = "equals",
                ["expected"] = "Code.exe",
                ["then_kind"] = "when",
                ["then_state"] = "armed",
                ["then_operator"] = "equals",
                ["then_expected"] = "true",
                ["then_then_kind"] = "key",
                ["then_then_value"] = "Escape",
                ["then_else_kind"] = "key",
                ["then_else_value"] = "F1",
                ["else_kind"] = "key",
                ["else_value"] = "F2"
            });
        var source = new KeyId("A");
        var runtime = new BehaviorRuntime(new Dictionary<KeyId, BehaviorDefinition>
        {
            [source] = BehaviorDefinitionFactory.Create(
                invocation,
                new Dictionary<string, UserBehaviorDefinitionProfile>(),
                system,
                stateProfile,
                state)
        });

        Assert.Equal(new KeyId("Escape"), Assert.Single(runtime.OnKeyDown(source, 0).Actions).Key);
    }

    [Theory]
    [InlineData("SET", "missing", "value", "x", "does not define field")]
    [InlineData("TOGGLE", "mode", null, null, "not bool")]
    [InlineData("SET", "flag", "value", "not-bool", "requires true or false")]
    public void Invalid_state_references_and_types_fail_during_compile_validation(
        string helper,
        string stateName,
        string? valueKey,
        string? value,
        string expected)
    {
        var stateProfile = new RuntimeStateProfile([
            RuntimeStateFieldProfile.String("mode", "normal"),
            RuntimeStateFieldProfile.Bool("flag", false)
        ]);
        var options = new Dictionary<string, string> { ["state"] = stateName };
        if (valueKey is not null)
            options[valueKey] = value!;
        var invocation = new BehaviorInvocationProfile(helper, [], options);

        var error = Assert.ThrowsAny<Exception>(() => BehaviorDefinitionFactory.Create(
            invocation,
            new Dictionary<string, UserBehaviorDefinitionProfile>(),
            EmptySystemQuerySnapshot.Instance,
            stateProfile,
            new RuntimeStateStore(stateProfile)));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
