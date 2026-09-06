using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class KeyBehaviorProfileJsonTests
{
    [Fact]
    public void Parses_and_serializes_optional_behavior_sections()
    {
        var json = """
        {
          "source": { "chordWindowMs": 40 },
          "singleStroke": {
            "S": { "Q": "q" },
            "K": { "Q": "q" }
          },
          "chords": {
            "S": [],
            "K": []
          },
          "layers": {
            "NAV": {
              "H": { "kind": "key", "value": "Left" }
            }
          },
          "behaviors": {
            "Space": {
              "tap": { "kind": "key", "value": "Space" },
              "hold": { "kind": "layer", "value": "NAV" },
              "timeoutMs": 180,
              "interrupt": "hold"
            },
            "A": {
              "tap": { "kind": "key", "value": "A" },
              "hold": { "kind": "modifier", "value": "Control" },
              "timeoutMs": 200,
              "interrupt": "tap"
            }
          }
        }
        """;

        var profile = AutomationProfileJson.Parse(json);

        Assert.False(profile.KeyBehaviors.IsEmpty);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("NAV", "H", out var nav));
        Assert.Equal(KeyBehaviorAction.Key("Left"), nav);
        Assert.True(profile.KeyBehaviors.TryGetBehavior("Space", out var space));
        Assert.Equal(KeyBehaviorAction.Layer("NAV"), space.Hold);
        Assert.Equal(180, space.TimeoutMs);
        Assert.True(profile.KeyBehaviors.TryGetBehavior("A", out var a));
        Assert.Equal(KeyBehaviorModifier.Control, a.Hold.GetModifier());
        Assert.Equal(TapHoldInterruptPolicy.Tap, a.Interrupt);

        var reparsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        Assert.True(reparsed.KeyBehaviors.TryGetBehavior("Space", out var roundTripSpace));
        Assert.Equal(space, roundTripSpace);
        Assert.True(reparsed.KeyBehaviors.TryGetLayerAction("NAV", "H", out var roundTripNav));
        Assert.Equal(nav, roundTripNav);
    }

    [Fact]
    public void Legacy_profile_without_behaviors_remains_empty()
    {
        var json = """
        {
          "source": { "chordWindowMs": 40 },
          "singleStroke": { "S": { "Q": "q" }, "K": { "Q": "q" } },
          "chords": { "S": [], "K": [] }
        }
        """;

        var profile = AutomationProfileJson.Parse(json);

        Assert.True(profile.KeyBehaviors.IsEmpty);
        var serialized = AutomationProfileJson.Serialize(profile);
        Assert.DoesNotContain("\"behaviors\"", serialized);
        Assert.DoesNotContain("\"layers\"", serialized);
    }

    [Fact]
    public void Unknown_behavior_layer_is_rejected()
    {
        var json = """
        {
          "singleStroke": { "S": { "Q": "q" }, "K": { "Q": "q" } },
          "chords": { "S": [], "K": [] },
          "behaviors": {
            "Space": {
              "tap": { "kind": "key", "value": "Space" },
              "hold": { "kind": "layer", "value": "MISSING" }
            }
          }
        }
        """;

        Assert.Throws<InvalidDataException>(() => AutomationProfileJson.Parse(json));
    }
}
