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

    [Fact]
    public void Generated_profile_preserves_user_behavior_definition_ir()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 40 },
          "startupMode": "S",
          "singleStroke": { "S": {}, "K": {}, "NUM": {} },
          "chords": { "S": [], "K": [], "NUM": [] },
          "behaviors": {
            "S": {
              "A": { "name": "SMART_LT", "arguments": ["X", "NUM"] }
            }
          },
          "behaviorDefinitions": {
            "SMART_LT": {
              "parameters": ["tap_key", "layer_name"],
              "locals": { "interrupted": false },
              "handlers": {
                "interrupt": {
                  "parameters": ["other"],
                  "statements": [
                    { "op": "set_bool", "target": "interrupted", "value": "true" },
                    { "op": "layer_on", "value": "layer_name" }
                  ]
                },
                "release": {
                  "parameters": [],
                  "statements": [
                    {
                      "op": "if_bool",
                      "condition": "interrupted",
                      "then": [{ "op": "layer_off", "value": "layer_name" }],
                      "else": [{ "op": "send", "value": "tap_key" }]
                    }
                  ]
                }
              }
            }
          }
        }
        """;

        var source = ProfileCompiler.Compile(json);

        Assert.Contains("new UserBehaviorDefinitionProfile(", source, StringComparison.Ordinal);
        Assert.Contains("\"SMART_LT\"", source, StringComparison.Ordinal);
        Assert.Contains("new UserBehaviorLocalProfile(\"interrupted\", false)", source, StringComparison.Ordinal);
        Assert.Contains("op: \"if_bool\"", source, StringComparison.Ordinal);
        Assert.Contains("condition: \"interrupted\"", source, StringComparison.Ordinal);
        Assert.Contains("op: \"layer_off\"", source, StringComparison.Ordinal);
        Assert.Contains("op: \"send\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_profile_preserves_clipboard_policy()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 40 },
          "startupMode": "S",
          "clipboard": {
            "history": true,
            "maxItems": 77,
            "persist": false,
            "images": false,
            "encryption": "user",
            "cipher": "chacha20-poly1305",
            "directory": "%LOCALAPPDATA%\\iKeyd-custom"
          },
          "singleStroke": { "S": {}, "K": {} },
          "chords": { "S": [], "K": [] }
        }
        """;

        var source = ProfileCompiler.Compile(json);

        Assert.Contains("clipboard: new ClipboardHistoryProfile(", source, StringComparison.Ordinal);
        Assert.Contains("maxItems: 77", source, StringComparison.Ordinal);
        Assert.Contains("persist: false", source, StringComparison.Ordinal);
        Assert.Contains("images: false", source, StringComparison.Ordinal);
        Assert.Contains("cipher: \"chacha20-poly1305\"", source, StringComparison.Ordinal);
        Assert.Contains("%LOCALAPPDATA%\\\\iKeyd-custom", source, StringComparison.Ordinal);
    }
}
