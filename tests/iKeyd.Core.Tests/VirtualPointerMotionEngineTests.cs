using iKeyd.Core.Desktop;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class VirtualPointerMotionEngineTests
{
    private static VirtualPointerMotionEngine ImmediateLinear()
        => new(new VirtualPointerMotionOptions
        {
            PressTimeConstantSeconds = 0,
            ReleaseTimeConstantSeconds = 0,
            ResponseCurve = PointerResponseCurve.Linear
        });

    [Fact]
    public void Diagonal_direction_is_normalized()
    {
        var engine = ImmediateLinear();
        engine.SetDirection(1, 1);

        var delta = engine.Step(1, 100);

        Assert.Equal(70, delta.X);
        Assert.Equal(70, delta.Y);
        Assert.InRange(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y), 98, 100);
    }

    [Fact]
    public void Socd_zero_target_stops_the_axis_instead_of_choosing_a_side()
    {
        var engine = ImmediateLinear();
        engine.SetDirection(0, 0);

        Assert.Equal(default(PointerMotionDelta), engine.Step(0.1, 1000));
        Assert.True(engine.IsIdle);
    }

    [Fact]
    public void Subpixel_remainder_is_preserved_across_ticks()
    {
        var engine = ImmediateLinear();
        engine.SetDirection(1, 0);

        Assert.Equal(0, engine.Step(0.004, 100).X);
        Assert.Equal(0, engine.Step(0.004, 100).X);
        Assert.Equal(1, engine.Step(0.004, 100).X);
    }

    [Fact]
    public void Integration_depends_on_elapsed_time_not_tick_count()
    {
        var singleStep = ImmediateLinear();
        singleStep.SetDirection(1, 0);
        var expected = singleStep.Step(0.1, 1000).X;

        var manySteps = ImmediateLinear();
        manySteps.SetDirection(1, 0);
        var actual = 0;
        for (var i = 0; i < 10; i++)
            actual += manySteps.Step(0.01, 1000).X;

        Assert.Equal(expected, actual);
        Assert.Equal(100, actual);
    }

    [Fact]
    public void Press_and_release_move_the_virtual_stick_without_long_cursor_inertia()
    {
        var engine = new VirtualPointerMotionEngine(new VirtualPointerMotionOptions
        {
            PressTimeConstantSeconds = 0.045,
            ReleaseTimeConstantSeconds = 0.020,
            ResponseCurve = PointerResponseCurve.Linear
        });

        engine.SetDirection(1, 0);
        engine.Step(0.045, 1000);
        Assert.InRange(engine.AxisX, 0.62, 0.64);

        engine.SetDirection(0, 0);
        engine.Step(0.020, 1000);
        Assert.InRange(engine.AxisX, 0.22, 0.24);
        Assert.False(engine.IsIdle);

        engine.Step(0.200, 1000);
        Assert.True(engine.IsIdle);
        Assert.Equal(0, engine.AxisX);
    }

    [Fact]
    public void Reversing_direction_crosses_smoothly_instead_of_resetting_a_hold_timer()
    {
        var engine = new VirtualPointerMotionEngine(new VirtualPointerMotionOptions
        {
            PressTimeConstantSeconds = 0.045,
            ReleaseTimeConstantSeconds = 0.020,
            ResponseCurve = PointerResponseCurve.Linear
        });

        engine.SetDirection(1, 0);
        engine.Step(0.1, 1000);
        var before = engine.AxisX;

        engine.SetDirection(-1, 0);
        engine.Step(0.005, 1000);

        Assert.True(engine.AxisX < before);
        Assert.True(engine.AxisX > -1);
    }

    [Fact]
    public void Reset_clears_motion_and_fractional_state()
    {
        var engine = ImmediateLinear();
        engine.SetDirection(1, 0);
        engine.Step(0.004, 100);

        engine.Reset();
        engine.SetDirection(1, 0);

        Assert.Equal(0, engine.Step(0.004, 100).X);
    }
}
