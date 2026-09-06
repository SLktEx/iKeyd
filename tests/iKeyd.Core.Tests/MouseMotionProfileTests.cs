using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class MouseMotionProfileTests
{
    [Fact]
    public void Mouse_json_parses_independently_from_generic_automation_profile()
    {
        const string json = """
        {
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

        var mouse = MouseMotionProfileJson.Parse(json);

        Assert.Equal(4, mouse.UpdateIntervalMs);
        Assert.Equal(30, mouse.PressMs);
        Assert.Equal(1, mouse.ReleaseMs);
        Assert.Equal("linear", mouse.Curve);
        Assert.Equal(1800, mouse.NormalSpeed);
        Assert.Equal(500, mouse.PrecisionSpeed);
        Assert.Equal(120, mouse.FineSpeed);
        Assert.Equal(3600, mouse.FastSpeed);
        Assert.Equal(2, mouse.TapNudgePixels);
        Assert.Equal(24, mouse.MaxCatchupMs);
    }

    [Fact]
    public void Missing_mouse_json_keeps_runtime_defaults()
    {
        var mouse = MouseMotionProfileJson.Parse("{}");
        Assert.Equal(MouseMotionProfile.Default, mouse);
    }

    [Fact]
    public void Unsupported_mouse_curve_is_rejected()
    {
        const string json = """
        {
          "mouse": {
            "response": { "curve": "banana" }
          }
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => MouseMotionProfileJson.Parse(json));
        Assert.Contains("Mouse curve", error.Message);
    }
}
