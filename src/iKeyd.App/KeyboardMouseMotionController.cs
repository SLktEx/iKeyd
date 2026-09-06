using System.Diagnostics;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;

namespace iKeyd.App;

/// <summary>
/// Drives the OS-independent virtual pointer engine from physical I/J/K/L state.
/// The timer is one-shot and only remains active while a gesture is moving or
/// while it is waiting for held direction keys to be released after a block.
/// </summary>
internal sealed class KeyboardMouseMotionController : IDisposable
{
    private const int TickMilliseconds = 8;
    private const double MaximumElapsedSeconds = 0.050;
    private const double NormalSpeed = 2200;
    private const double PrecisionSpeed = 800;
    private const double FineSpeed = 240;
    private const double MinimumFastSpeed = 4000;

    private readonly object _gate = new();
    private readonly KeyboardState _keyboardState;
    private readonly IDesktopBackend _desktop;
    private readonly Func<bool> _isEnabled;
    private readonly VirtualPointerMotionEngine _engine = new();
    private readonly Timer _timer;

    private bool _timerScheduled;
    private bool _suspended;
    private bool _blockUntilDirectionReleased;
    private bool _disposed;
    private long _lastTimestamp;

    public KeyboardMouseMotionController(
        KeyboardState keyboardState,
        IDesktopBackend desktop,
        Func<bool> isEnabled)
    {
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Wake()
    {
        if (!_isEnabled())
            return;

        lock (_gate)
        {
            if (_disposed || _suspended || _timerScheduled)
                return;
            ScheduleLocked();
        }
    }

    public void SetSuspended(bool suspended)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _suspended = suspended;
            _engine.Reset();
            StopTimerLocked();

            if (suspended)
            {
                // A direction held through suspend must not start moving the
                // pointer immediately when processing is resumed.
                _blockUntilDirectionReleased = HasDirectionPressed();
                return;
            }

            if (!HasDirectionPressed())
            {
                _blockUntilDirectionReleased = false;
                return;
            }

            // Poll until all held direction keys are released, then wait for the
            // next fresh key-down to wake normal motion.
            _blockUntilDirectionReleased = true;
            ScheduleLocked();
        }
    }

    public void BlockUntilDirectionReleased()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _engine.Reset();
            StopTimerLocked();
            _blockUntilDirectionReleased = HasDirectionPressed();
            if (_blockUntilDirectionReleased && !_suspended)
                ScheduleLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _engine.Reset();
            StopTimerLocked();
        }
        _timer.Dispose();
    }

    private void OnTick(object? state)
    {
        // Do not hold the controller lock while consulting the runtime predicate;
        // input dispatch may call Wake while holding its own runtime lock.
        var enabled = !_suspended && _isEnabled();

        lock (_gate)
        {
            if (_disposed)
                return;
            _timerScheduled = false;

            if (!enabled || _suspended)
            {
                _engine.Reset();
                return;
            }

            var (x, y) = ReadDirection();
            if (_blockUntilDirectionReleased)
            {
                _engine.Reset();
                if (x == 0 && y == 0)
                {
                    _blockUntilDirectionReleased = false;
                    return;
                }

                ScheduleLocked();
                return;
            }

            _engine.SetDirection(x, y);
            var now = Stopwatch.GetTimestamp();
            var elapsed = _lastTimestamp == 0
                ? TickMilliseconds / 1000.0
                : (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = now;
            elapsed = Math.Clamp(elapsed, 0, MaximumElapsedSeconds);

            var delta = _engine.Step(elapsed, GetSpeedPixelsPerSecond());
            if (delta.X != 0 || delta.Y != 0)
                _desktop.MovePointerBy(delta.X, delta.Y);

            if (x != 0 || y != 0 || !_engine.IsIdle)
                ScheduleLocked(preserveTimestamp: true);
            else
                _lastTimestamp = 0;
        }
    }

    private (int X, int Y) ReadDirection()
    {
        var left = _keyboardState.IsVirtualKeyPressed((ushort)'J');
        var right = _keyboardState.IsVirtualKeyPressed((ushort)'L');
        var up = _keyboardState.IsVirtualKeyPressed((ushort)'I');
        var down = _keyboardState.IsVirtualKeyPressed((ushort)'K');
        return ((right ? 1 : 0) - (left ? 1 : 0), (down ? 1 : 0) - (up ? 1 : 0));
    }

    private bool HasDirectionPressed()
    {
        var (x, y) = ReadDirection();
        if (x != 0 || y != 0)
            return true;

        // SOCD can produce a zero vector while both opposing keys are held.
        return _keyboardState.IsVirtualKeyPressed((ushort)'J') ||
               _keyboardState.IsVirtualKeyPressed((ushort)'L') ||
               _keyboardState.IsVirtualKeyPressed((ushort)'I') ||
               _keyboardState.IsVirtualKeyPressed((ushort)'K');
    }

    private double GetSpeedPixelsPerSecond()
    {
        // Keep the legacy modifier ordering, but map the old fixed jump sizes to
        // continuous velocity bands instead of per-repeat distances.
        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))
            return PrecisionSpeed;
        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))
            return FineSpeed;
        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))
            return Math.Max(MinimumFastSpeed, _desktop.GetPrimaryWorkArea().Width * 3.0);
        return NormalSpeed;
    }

    private void ScheduleLocked(bool preserveTimestamp = false)
    {
        if (_disposed || _suspended)
            return;
        if (!preserveTimestamp || _lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        _timerScheduled = true;
        _timer.Change(TickMilliseconds, Timeout.Infinite);
    }

    private void StopTimerLocked()
    {
        _timerScheduled = false;
        _lastTimestamp = 0;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
