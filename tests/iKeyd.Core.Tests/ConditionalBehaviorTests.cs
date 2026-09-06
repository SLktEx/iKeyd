using iKeyd.Core.Automation;
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
            new KeyValuePair<string, string>(SystemQueryKeys.KeyboardCapsLock, "true"),
        ]);

        Assert.True(new SystemQueryCondition(SystemQueryKeys.ForegroundProcess, SystemQueryConditionOperator.Equals, "code.EXE").Evaluate(snapshot));
        Assert.True(new SystemQueryCondition(SystemQueryKeys.ForegroundProcess, SystemQueryConditionOperator.NotEquals, "explorer.exe").Evaluate(snapshot));
        Assert.True(new SystemQueryCondition(SystemQueryKeys.KeyboardCapsLock, SystemQueryConditionOperator.Equals, "TRUE").Evaluate(snapshot));
        Assert.False(new SystemQueryCondition(SystemQueryKeys.ForegroundTitle, SystemQueryConditionOperator.NotEquals, "anything").Evaluate(snapshot));
    }

    [Fact]
    public void Profile_collects_queries_from_nested_conditions_and_query_actions()
    {
        var nested = KeyBehaviorAction.When(
            new SystemQueryCondition(SystemQueryKeys.KeyboardNumLock, SystemQueryConditionOperator.Equals, "true"),
            KeyBehaviorAction.Query(SystemQueryKeys.ForegroundTitle),
            KeyBehaviorAction.Key("F3"));
        var conditional = KeyBehaviorAction.When(
            new SystemQueryCondition(SystemQueryKeys.ForegroundProcess, SystemQueryConditionOperator.Equals, "Code.exe"),
            nested,
            KeyBehaviorAction.Key("Escape"));
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("APP"))],
            [new KeyBehaviorLayer("APP", [new KeyBehaviorLayerBinding(KeyCode.H, conditional)])]);

        Assert.Equal(
            [SystemQueryKeys.ForegroundProcess, SystemQueryKeys.ForegroundTitle, SystemQueryKeys.KeyboardNumLock],
            profile.SystemQueries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void Conditional_action_round_trips_through_profile_json()
    {
        var json = """
        {
          "singleStroke": { "S": { "Q": "q" }, "K": { "Q": "q" } },
          "chords": { "S": [], "K": [] },
          "layers": {
            "APP": {
              "H": {
                "kind": "when",
                "condition": { "query": "foreground.process", "operator": "equals", "value": "Code.exe" },
                "then": { "kind": "exec", "value": "tool.exe", "args": ["hello world", "&literal"] },
                "else": {
                  "kind": "when",
                  "condition": { "query": "keyboard.capslock", "operator": "not_equals", "value": "false" },
                  "then": { "kind": "key", "value": "Escape" }
                }
              }
            }
          }
        }
        """;

        var profile = AutomationProfileJson.Parse(json);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("APP", "H", out var action));
        var outer = action.GetConditional();
        Assert.Equal(SystemQueryKeys.ForegroundProcess, outer.Condition.Query);
        Assert.Equal(SystemQueryConditionOperator.Equals, outer.Condition.Operator);
        Assert.Equal(KeyBehaviorActionKind.Exec, outer.Then.Kind);
        Assert.Equal(["hello world", "&literal"], outer.Then.GetArguments());
        Assert.Equal(KeyBehaviorActionKind.When, outer.Else!.Value.Kind);

        var reparsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        Assert.True(reparsed.KeyBehaviors.TryGetLayerAction("APP", "H", out var roundTrip));
        var roundTripOuter = roundTrip.GetConditional();
        Assert.Equal(outer.Condition, roundTripOuter.Condition);
        Assert.Equal(outer.Then, roundTripOuter.Then);
        Assert.Equal(KeyBehaviorActionKind.When, roundTripOuter.Else!.Value.Kind);
    }
}
