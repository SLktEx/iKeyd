using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class UserBehaviorTapHoldTests
{
    [Fact]
    public void Tap_handler_runs_when_released_before_tapping_term()
    {
        var runtime = Runtime(
            Definition(),
            Invocation(("tapping_term", "100ms")));

        runtime.OnKeyDown("A", 0);
        var release = runtime.OnKeyUp("A", 99);

        Assert.Equal([BehaviorAction.SendKey("X")], release.Actions);
    }

    [Fact]
    public void Hold_handler_runs_once_at_timeout_and_owned_layer_is_released()
    {
        var runtime = Runtime(
            Definition(),
            Invocation(("tapping_term", "100ms")));

        runtime.OnKeyDown("A", 0);
        var before = runtime.AdvanceTo(99);
        var hold = runtime.AdvanceTo(100);
        var repeatedAdvance = runtime.AdvanceTo(150);
        var release = runtime.OnKeyUp("A", 160);

        Assert.Empty(before);
        Assert.Equal([BehaviorAction.LayerOn("NUM")], hold);
        Assert.Empty(repeatedAdvance);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], release.Actions);
    }

    [Fact]
    public void Interrupt_resolves_hold_before_interrupt_handler_by_default()
    {
        var runtime = Runtime(
            Definition(includeInterrupt: true),
            Invocation(("tapping_term", "200ms")));

        runtime.OnKeyDown("A", 0);
        var interrupt = runtime.OnKeyDown("B", 20);
        var release = runtime.OnKeyUp("A", 30);

        Assert.Equal(
            [BehaviorAction.LayerOn("NUM"), BehaviorAction.SendKey("B")],
            interrupt.Actions);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], release.Actions);
    }

    [Fact]
    public void Hold_on_other_key_press_false_keeps_pending_tap_before_timeout()
    {
        var runtime = Runtime(
            Definition(includeInterrupt: true),
            Invocation(
                ("tapping_term", "200ms"),
                ("hold_on_other_key_press", "false")));

        runtime.OnKeyDown("A", 0);
        var interrupt = runtime.OnKeyDown("B", 20);
        var release = runtime.OnKeyUp("A", 30);

        Assert.Equal([BehaviorAction.SendKey("B")], interrupt.Actions);
        Assert.Equal([BehaviorAction.SendKey("X")], release.Actions);
    }

    [Fact]
    public void Cancel_before_resolution_runs_neither_tap_nor_hold()
    {
        var runtime = Runtime(Definition(), Invocation());

        runtime.OnKeyDown("A", 0);
        var cancelled = runtime.CancelAll();

        Assert.Empty(cancelled);
    }

    [Theory]
    [InlineData("-1ms")]
    [InlineData("100")]
    [InlineData("abcms")]
    public void Invalid_tapping_term_is_rejected(string tappingTerm)
    {
        var definition = Definition();
        var definitions = new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [definition.Name] = definition
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(
                Invocation(("tapping_term", tappingTerm)),
                definitions));

        Assert.Contains("tapping_term", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invocation_options_remain_rejected_for_user_behaviors_without_tap_hold_events()
    {
        var definition = new UserBehaviorDefinitionProfile(
            "PLAIN",
            [],
            handlers:
            [
                new UserBehaviorHandlerProfile(
                    "press",
                    [],
                    [new UserBehaviorStatementProfile("send", value: "X")])
            ]);
        var definitions = new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [definition.Name] = definition
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(
                new BehaviorInvocationProfile(
                    "PLAIN",
                    [],
                    new Dictionary<string, string> { ["tapping_term"] = "100ms" }),
                definitions));

        Assert.Contains("does not support invocation options", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Canonical_dsl_parses_tap_hold_handlers_options_and_static_generation()
    {
        var document = IKeydDslDocumentParser.Parse(
            """
            behavior SMART_TH(tap_key, layer_name) {
                on_hold {
                    layer.on(layer_name)
                }

                on_tap {
                    send tap_key
                }
            }

            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = SMART_TH(X, NUM) {
                    tapping_term = 170ms
                    hold_on_other_key_press = false
                }
            }

            keymap K {
            }

            keymap NUM {
            }
            """,
            "tap-hold.ikeyd");

        var definition = Assert.Single(document.Profile.BehaviorDefinitions.Values);
        Assert.NotNull(definition.FindHandler("hold"));
        Assert.NotNull(definition.FindHandler("tap"));

        var invocation = Assert.Single(document.Profile.GetKeymap("S").BehaviorMappings).Invocation;
        Assert.Equal("170ms", invocation.Options["tapping_term"]);
        Assert.Equal("false", invocation.Options["hold_on_other_key_press"]);

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("SMART_TH", generated, StringComparison.Ordinal);
        Assert.Contains("\"hold\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"tap\"", generated, StringComparison.Ordinal);
        Assert.Contains("tapping_term", generated, StringComparison.Ordinal);

        var runtime = new BehaviorRuntime(
            document.Profile.GetKeymap("S").BuildBehaviorBindings(document.Profile.BehaviorDefinitions));
        runtime.OnKeyDown("A", 0);
        Assert.Equal([BehaviorAction.SendKey("X")], runtime.OnKeyUp("A", 100).Actions);
    }

    private static BehaviorRuntime Runtime(
        UserBehaviorDefinitionProfile definition,
        BehaviorInvocationProfile invocation)
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S", [], [],
                    [new BehaviorMappingProfile("A", invocation)]),
                new AutomationKeymapProfile("NUM", [], [])
            ],
            behaviorDefinitions: [definition]);

        return new BehaviorRuntime(
            profile.GetKeymap("S").BuildBehaviorBindings(profile.BehaviorDefinitions));
    }

    private static UserBehaviorDefinitionProfile Definition(bool includeInterrupt = false)
    {
        var handlers = new List<UserBehaviorHandlerProfile>
        {
            new(
                "hold",
                [],
                [new UserBehaviorStatementProfile("layer_on", value: "layer_name")]),
            new(
                "tap",
                [],
                [new UserBehaviorStatementProfile("send", value: "tap_key")])
        };

        if (includeInterrupt)
        {
            handlers.Add(new UserBehaviorHandlerProfile(
                "interrupt",
                ["other"],
                [new UserBehaviorStatementProfile("send", value: "other")]));
        }

        return new UserBehaviorDefinitionProfile(
            "SMART_TH",
            ["tap_key", "layer_name"],
            handlers: handlers);
    }

    private static BehaviorInvocationProfile Invocation(params (string Name, string Value)[] options)
        => new(
            "SMART_TH",
            ["X", "NUM"],
            options.ToDictionary(
                option => option.Name,
                option => option.Value,
                StringComparer.OrdinalIgnoreCase));
}
