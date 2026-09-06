using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ProfileCompilerTests
{
    [Fact]
    public void Generated_profile_preserves_behavior_options()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 40 },
          "startupMode": "S",
          "singleStroke": {
            "S": {},
            "K": {},
            "NUM": { "B": "num-b" }
          },
          "chords": {
            "S": [],
            "K": [],
            "NUM": []
          },
          "behaviors": {
            "S": {
              "A": {
                "name": "LT",
                "arguments": ["NUM", "Z"],
                "options": {
                  "tapping_term": "170ms",
                  "hold_on_other_key_press": false
                }
              },
              "C": {
                "name": "MT",
                "arguments": ["Ctrl", "X"]
              }
            }
          }
        }
        """;

        var source = ProfileCompiler.Compile(json);

        Assert.Contains("new BehaviorInvocationProfile(\"LT\"", source, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, string>(\"tapping_term\", \"170ms\")", source, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, string>(\"hold_on_other_key_press\", \"false\")", source, StringComparison.Ordinal);
        Assert.Contains("new BehaviorInvocationProfile(\"MT\"", source, StringComparison.Ordinal);
    }
}
