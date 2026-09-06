namespace iKeyd.Core.Desktop;

public enum PointerResponseCurve
{
    Linear,
    SmoothStep
}

public sealed record VirtualPointerMotionOptions
{
    public double PressTimeConstantSeconds { get; init; } = 0.045;
    public double ReleaseTimeConstantSeconds { get; init; } = 0.020;
    public PointerResponseCurve ResponseCurve { get; init; } = PointerResponseCurve.SmoothStep;
    public double IdleEpsilon { get; init; } = 0.0001;
}

public readonly record struct PointerMotionDelta(int X, int Y);

/// <summary>
/// Converts digital direction keys into a smooth two-dimensional virtual stick,
/// then integrates that stick into relative pointer deltas. The engine is fully
/// deterministic and OS-independent; callers provide elapsed time and the current
/// pointer speed on every step.
/// </summary>
public sealed class VirtualPointerMotionEngine
{
    private readonly VirtualPointerMotionOptions _options;
    private double _targetX;
    private double _targetY;
    private double _axisX;
    private double _axisY;
    private double _remainderX;
    private double _remainderY;

    public VirtualPointerMotionEngine(VirtualPointerMotionOptions? options = null)
    {
        _options = options ?? new VirtualPointerMotionOptions();
        if (_options.PressTimeConstantSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Press time constant must be non-negative.");
        if (_options.ReleaseTimeConstantSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Release time constant must be non-negative.");
        if (_options.IdleEpsilon <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Idle epsilon must be positive.");
    }

    public double AxisX => _axisX;
    public double AxisY => _axisY;

    public bool IsIdle
        => _targetX == 0 &&
           _targetY == 0 &&
           Math.Abs(_axisX) <= _options.IdleEpsilon &&
           Math.Abs(_axisY) <= _options.IdleEpsilon;

    public void SetDirection(int x, int y)
    {
        var targetX = Math.Clamp(x, -1, 1);
        var targetY = Math.Clamp(y, -1, 1);
        Normalize(ref targetX, ref targetY);
        _targetX = targetX;
        _targetY = targetY;
    }

    public PointerMotionDelta Step(double elapsedSeconds, double pixelsPerSecond)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!double.IsFinite(pixelsPerSecond) || pixelsPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerSecond));
        if (elapsedSeconds == 0 || pixelsPerSecond == 0)
            return default;

        _axisX = AdvanceAxis(_axisX, _targetX, elapsedSeconds);
        _axisY = AdvanceAxis(_axisY, _targetY, elapsedSeconds);

        SnapIdleAxis(ref _axisX, _targetX);
        SnapIdleAxis(ref _axisY, _targetY);

        var outputX = ApplyCurve(_axisX);
        var outputY = ApplyCurve(_axisY);
        Normalize(ref outputX, ref outputY);

        var exactX = _remainderX + outputX * pixelsPerSecond * elapsedSeconds;
        var exactY = _remainderY + outputY * pixelsPerSecond * elapsedSeconds;
        var deltaX = (int)Math.Truncate(exactX);
        var deltaY = (int)Math.Truncate(exactY);
        _remainderX = exactX - deltaX;
        _remainderY = exactY - deltaY;

        if (IsIdle)
        {
            // Once a gesture is fully stopped, do not let a fractional remainder
            // leak into the next unrelated gesture.
            _remainderX = 0;
            _remainderY = 0;
        }

        return new PointerMotionDelta(deltaX, deltaY);
    }

    public void Reset()
    {
        _targetX = 0;
        _targetY = 0;
        _axisX = 0;
        _axisY = 0;
        _remainderX = 0;
        _remainderY = 0;
    }

    private double AdvanceAxis(double current, double target, double elapsedSeconds)
    {
        var timeConstant = target == 0
            ? _options.ReleaseTimeConstantSeconds
            : _options.PressTimeConstantSeconds;
        if (timeConstant == 0)
            return target;

        var alpha = 1 - Math.Exp(-elapsedSeconds / timeConstant);
        return current + (target - current) * alpha;
    }

    private void SnapIdleAxis(ref double axis, double target)
    {
        if (target == 0 && Math.Abs(axis) <= _options.IdleEpsilon)
            axis = 0;
    }

    private double ApplyCurve(double value)
    {
        if (_options.ResponseCurve == PointerResponseCurve.Linear)
            return value;

        var magnitude = Math.Clamp(Math.Abs(value), 0, 1);
        var curved = magnitude * magnitude * (3 - 2 * magnitude);
        return Math.CopySign(curved, value);
    }

    private static void Normalize(ref int x, ref int y)
    {
        if (x == 0 || y == 0)
            return;

        // Integer direction vectors can only exceed unit length diagonally. Keep
        // the public target representation digital and normalize after conversion.
        // The double overload below performs the actual normalization.
        var dx = (double)x;
        var dy = (double)y;
        Normalize(ref dx, ref dy);
        _ = dx;
        _ = dy;
    }

    private static void Normalize(ref double x, ref double y)
    {
        var magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude <= 1)
            return;
        x /= magnitude;
        y /= magnitude;
    }
}
