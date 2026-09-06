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
              "H": { "kind": "key", "value": "Left" },
              "U": { "kind": "mouse_move", "value": "-30,10" },
              "I": { "kind": "mouse_click", "value": "Left" },
              "O": { "kind": "scroll", "value": "Up" },
              "P": { "kind": "media", "value": "PlayPause" },
              "At": { "kind": "window", "value": "LeftHalf" }
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
              "tap": { "kind": "window", "value": "ToggleMaximize" },
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
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("NAV", "U", out var move));
        Assert.Equal(KeyBehaviorAction.MouseMove(-30, 10), move);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("NAV", "I", out var click));
        Assert.Equal(KeyBehaviorAction.MouseClick("Left"), click);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("NAV", "O", out var scroll));
        Assert.Equal(KeyBehaviorAction.Scroll("Up"), scroll);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("NAV", "P", out var media));
        Assert.Equal(KeyBehaviorAction.Media("PlayPause"), media);
        Assert.True(profile.KeyBehaviors.TryGetLayerAction("NAV", "At", out var window));
        Assert.Equal(KeyBehaviorAction.Window("LeftHalf"), window);

        Assert.True(profile.KeyBehaviors.TryGetBehavior("Space", out var space));
        Assert.Equal(KeyBehaviorAction.Layer("NAV"), space.Hold);
        Assert.Equal(180, space.TimeoutMs);
        Assert.True(profile.KeyBehaviors.TryGetBehavior("A", out var a));
        Assert.Equal(KeyBehaviorAction.Window("ToggleMaximize"), a.Tap);
        Assert.Equal(KeyBehaviorModifier.Control, a.Hold.GetModifier());
        Assert.Equal(TapHoldInterruptPolicy.Tap, a.Interrupt);

        var reparsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        Assert.True(reparsed.KeyBehaviors.TryGetBehavior("Space", out var roundTripSpace));
        Assert.Equal(space, roundTripSpace);
        Assert.True(reparsed.KeyBehaviors.TryGetLayerAction("NAV", "U", out var roundTripMove));
        Assert.Equal(move, roundTripMove);
        Assert.True(reparsed.KeyBehaviors.TryGetLayerAction("NAV", "P", out var roundTripMedia));
        Assert.Equal(media, roundTripMedia);
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

    [Fact]
    public void Invalid_desktop_action_value_is_rejected()
    {
        var json = """
        {
          "singleStroke": { "S": { "Q": "q" }, "K": { "Q": "q" } },
          "chords": { "S": [], "K": [] },
          "layers": {
            "DESKTOP": {
              "H": { "kind": "media", "value": "ExplodeSpeakers" }
            }
          }
        }
        """;

        Assert.Throws<InvalidDataException>(() => AutomationProfileJson.Parse(json));
    }
}
