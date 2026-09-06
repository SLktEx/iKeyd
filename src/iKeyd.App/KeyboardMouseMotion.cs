using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Windows.Input;

namespace iKeyd.App;

/// <summary>
/// Virtual-stick pointer movement for the legacy S+M mouse layer. Physical
/// keyboard repeat is ignored. Direction keys request a two-dimensional analog
/// stick target; the Core motion engine smooths toward that target and emits
/// time-based relative movement.
/// </summary>
internal sealed class KeyboardMouseMotion : IDisposable
{
    private readonly object _gate = new();
    private readonly IDesktopBackend _desktop;
    private readonly KeyboardState _keyboardState;
    private readonly MouseMotionProfile _profile;
    private readonly HashSet<ushort> _directions = [];
    private readonly VirtualPointerMotionEngine _motion;
    private readonly Timer _timer;

    private long _lastTickAt;
    private bool _disposed;

    public KeyboardMouseMotion(
        IDesktopBackend desktop,
        KeyboardState keyboardState,
        MouseMotionProfile profile)
    {
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _motion = new VirtualPointerMotionEngine(new VirtualPointerMotionOptions
        {
            PressTimeConstantSeconds = profile.PressMs / 1000.0,
            ReleaseTimeConstantSeconds = profile.ReleaseMs / 1000.0,
            ResponseCurve = ResolveCurve(profile.Curve)
        });
        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool TryStart(KeyId key, ushort virtualKey)
    {
        if (!IsDirection(key.Code))
            return false;

        lock (_gate)
        {
            if (_disposed)
                return true;

            if (!_directions.Add(virtualKey))
                return true;

            if (_directions.Count == 1 && _profile.TapNudgePixels != 0)
            {
                var (nudgeX, nudgeY) = DirectionUnit(key.Code);
                _desktop.MovePointerBy(
                    nudgeX * _profile.TapNudgePixels,
                    nudgeY * _profile.TapNudgePixels);
            }

            var (x, y) = UpdateTargetCore();
            EnsureTimerForTargetCore(x, y);
            return true;
        }
    }

    public bool TryRelease(ushort virtualKey)
    {
        lock (_gate)
        {
            if (!_directions.Remove(virtualKey))
                return false;

            var (x, y) = UpdateTargetCore();
            EnsureTimerForTargetCore(x, y);
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _directions.Clear();
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _directions.Clear();
            StopCore();
        }
        _timer.Dispose();
    }

    internal static double SpeedForModifiers(
        MouseMotionProfile profile,
        bool precision,
        bool fine,
        bool fast)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (precision)
            return profile.PrecisionSpeed;
        if (fine)
            return profile.FineSpeed;
        if (fast)
            return profile.FastSpeed;
        return profile.NormalSpeed;
    }

    internal static double SpeedForModifiers(bool precision, bool fine, bool fast)
        => SpeedForModifiers(MouseMotionProfile.Default, precision, fine, fast);

    private void OnTick(object? state)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _lastTickAt == 0)
                    return;

                var now = Environment.TickCount64;
                var elapsed = now - _lastTickAt;
                if (elapsed <= 0)
                    return;

                _lastTickAt = now;
                var integratedMs = Math.Min(elapsed, _profile.MaxCatchupMs);
                var delta = _motion.Step(
                    integratedMs / 1000.0,
                    GetSpeedPixelsPerSecond());

                if (delta.X != 0 || delta.Y != 0)
                    _desktop.MovePointerBy(delta.X, delta.Y);

                var (x, y) = CurrentDirectionCore();
                if (x == 0 && y == 0 && _motion.IsIdle)
                    StopCore();
            }
        }
        catch
        {
            Reset();
        }
    }

    private (int X, int Y) UpdateTargetCore()
    {
        var direction = CurrentDirectionCore();
        _motion.SetDirection(direction.X, direction.Y);
        return direction;
    }

    private (int X, int Y) CurrentDirectionCore()
    {
        var x = (_directions.Contains((ushort)'L') ? 1 : 0) -
                (_directions.Contains((ushort)'J') ? 1 : 0);
        var y = (_directions.Contains((ushort)'K') ? 1 : 0) -
                (_directions.Contains((ushort)'I') ? 1 : 0);
        return (x, y);
    }

    private void EnsureTimerForTargetCore(int x, int y)
    {
        if (x == 0 && y == 0)
            return;
        if (_lastTickAt != 0)
            return;

        _lastTickAt = Environment.TickCount64;
        _timer.Change(_profile.UpdateIntervalMs, _profile.UpdateIntervalMs);
    }

    private double GetSpeedPixelsPerSecond()
        => SpeedForModifiers(
            _profile,
            _keyboardState.IsVirtualKeyPressed((ushort)'D'),
            _keyboardState.IsVirtualKeyPressed((ushort)'E'),
            _keyboardState.IsVirtualKeyPressed((ushort)'C'));

    private void StopCore()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _lastTickAt = 0;
        _motion.Reset();
    }

    private static PointerResponseCurve ResolveCurve(string curve)
        => curve.ToLowerInvariant() switch
        {
            "linear" => PointerResponseCurve.Linear,
            "smoothstep" => PointerResponseCurve.SmoothStep,
            _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unsupported pointer response curve.")
        };

    private static bool IsDirection(KeyCode key)
        => key is KeyCode.I or KeyCode.J or KeyCode.K or KeyCode.L;

    private static (int X, int Y) DirectionUnit(KeyCode key)
        => key switch
        {
            KeyCode.I => (0, -1),
            KeyCode.J => (-1, 0),
            KeyCode.K => (0, 1),
            KeyCode.L => (1, 0),
            _ => (0, 0)
        };
}
