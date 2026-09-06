using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class AutomationBehaviorProfileTests
{
    [Fact]
    public void Exec_shell_and_query_round_trip_through_canonical_profile()
    {
        var json = """
        {
          "singleStroke": { "S": { "Q": "q" }, "K": { "Q": "q" } },
          "chords": { "S": [], "K": [] },
          "layers": {
            "TOOLS": {
              "Q": { "kind": "exec", "value": "tool.exe", "args": ["hello world", "&literal", "quoted\"value"] },
              "W": { "kind": "shell", "value": "echo hello && echo world" },
              "E": { "kind": "query", "value": "foreground.process" }
            }
          }
        }
        """;

        var profile = AutomationProfileJson.Parse(json);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("TOOLS", "Q", out var exec));
        Assert.Equal(KeyBehaviorActionKind.Exec, exec.Kind);
        Assert.Equal("tool.exe", exec.Value);
        Assert.Equal(["hello world", "&literal", "quoted\"value"], exec.GetArguments());
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("TOOLS", "W", out var shell));
        Assert.Equal(KeyBehaviorAction.Shell("echo hello && echo world"), shell);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("TOOLS", "E", out var query));
        Assert.Equal(KeyBehaviorAction.Query("foreground.process"), query);

        var reparsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        Assert.True(reparsed.KeyBehaviors.TryGetLayerAction("TOOLS", "Q", out var roundTripExec));
        Assert.Equal(exec.Kind, roundTripExec.Kind);
        Assert.Equal(exec.Value, roundTripExec.Value);
        Assert.Equal(exec.GetArguments(), roundTripExec.GetArguments());
    }

    [Fact]
    public void Exec_args_must_be_strings()
    {
        var json = """
        {
          "singleStroke": { "S": { "Q": "q" }, "K": { "Q": "q" } },
          "chords": { "S": [], "K": [] },
          "layers": {
            "TOOLS": {
              "Q": { "kind": "exec", "value": "tool.exe", "args": [1] }
            }
          }
        }
        """;

        Assert.Throws<InvalidDataException>(() => AutomationProfileJson.Parse(json));
    }
}
