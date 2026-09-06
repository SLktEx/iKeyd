using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class MouseMotionProfileTests
{
    private const string MinimalProfilePrefix = """
        {
          "source": { "chordWindowMs": 40 },
          "singleStroke": { "S": {}, "K": {} },
          "chords": { "S": [], "K": [] },
        """;

    [Fact]
    public void Mouse_json_is_projected_on_top_of_the_generic_automation_profile()
    {
        var json = MinimalProfilePrefix + """
          "mouse": {
            "engine": "virtual_stick",
            "updateMs": 4,
            "response": { "pressMs": 30, "releaseMs": 1, "curve": "linear" },
            "speed": { "normal": 1800, "precision": 500, "fine": 120, "fast": 3600 },
            "socd": "neutral",
            "tapNudgePixels": 2,
            "maxCatchupMs": 24
          }
        }
        """;

        var generic = AutomationProfileJson.Parse(json);
        Assert.Equal(MouseMotionProfile.Default, generic.Mouse);

        var projected = MouseMotionProfileJson.Apply(generic, json);
        Assert.Equal(4, projected.Mouse.UpdateIntervalMs);
        Assert.Equal(30, projected.Mouse.PressMs);
        Assert.Equal(1, projected.Mouse.ReleaseMs);
        Assert.Equal("linear", projected.Mouse.Curve);
        Assert.Equal(1800, projected.Mouse.NormalSpeed);
        Assert.Equal(500, projected.Mouse.PrecisionSpeed);
        Assert.Equal(120, projected.Mouse.FineSpeed);
        Assert.Equal(3600, projected.Mouse.FastSpeed);
        Assert.Equal(2, projected.Mouse.TapNudgePixels);
        Assert.Equal(24, projected.Mouse.MaxCatchupMs);
    }

    [Fact]
    public void Missing_mouse_json_keeps_runtime_defaults()
    {
        var json = MinimalProfilePrefix + """
          "startupMode": "S"
        }
        """;
        var generic = AutomationProfileJson.Parse(json);

        var projected = MouseMotionProfileJson.Apply(generic, json);

        Assert.Same(generic, projected);
        Assert.Equal(MouseMotionProfile.Default, projected.Mouse);
    }

    [Fact]
    public void Unsupported_mouse_curve_is_rejected()
    {
        var json = MinimalProfilePrefix + """
          "mouse": {
            "response": { "curve": "banana" }
          }
        }
        """;
        var generic = AutomationProfileJson.Parse(json);

        var error = Assert.Throws<InvalidDataException>(() => MouseMotionProfileJson.Apply(generic, json));
        Assert.Contains("Mouse curve", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
