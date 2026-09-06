using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Windows.Input;

namespace iKeyd.App;

/// <summary>
/// Time-based pointer movement for the legacy S+M mouse layer. Physical keyboard
/// repeat is intentionally ignored: one held direction starts a short-period
/// timer and key-up stops that direction immediately.
/// </summary>
internal sealed class KeyboardMouseMotion : IDisposable
{
    internal const int TickIntervalMs = 8;
    internal const int MaxIntegratedTickMs = 32;
    internal const double InitialSpeedPixelsPerSecond = 110.0;
    internal const double MaxSpeedPixelsPerSecond = 1700.0;
    internal const double AccelerationDurationMs = 650.0;

    private readonly object _gate = new();
    private readonly IDesktopBackend _desktop;
    private readonly KeyboardState _keyboardState;
    private readonly HashSet<ushort> _directions = [];
    private readonly Timer _timer;

    private long _startedAt;
    private long _lastTickAt;
    private double _remainderX;
    private double _remainderY;
    private bool _disposed;

    public KeyboardMouseMotion(IDesktopBackend desktop, KeyboardState keyboardState)
    {
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
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

            // A WH_KEYBOARD_LL down event is repeated by Windows while a key is
            // held. The movement loop already represents the hold, so duplicate
            // downs must not create extra movement or restart acceleration.
            if (!_directions.Add(virtualKey))
                return true;

            // A tap can be shorter than the first timer tick. Give every fresh
            // direction press exactly one pixel of deterministic movement so fine
            // positioning remains possible without restoring the old 10/30/100px
            // jumps that made keyboard mouse movement feel coarse.
            var (nudgeX, nudgeY) = DirectionUnit(key.Code);
            _desktop.MovePointerBy(nudgeX, nudgeY);

            if (_directions.Count == 1)
            {
                var now = Environment.TickCount64;
                _startedAt = now;
                _lastTickAt = now;
                _remainderX = 0;
                _remainderY = 0;
                _timer.Change(TickIntervalMs, TickIntervalMs);
            }

            return true;
        }
    }

    public bool TryRelease(ushort virtualKey)
    {
        lock (_gate)
        {
            if (!_directions.Remove(virtualKey))
                return false;

            if (_directions.Count == 0)
                StopCore();
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

    internal static double SpeedAt(long heldMilliseconds, double multiplier = 1.0)
    {
        var progress = Math.Clamp(heldMilliseconds / AccelerationDurationMs, 0.0, 1.0);
        // smoothstep removes the visible velocity discontinuities of stepped
        // acceleration while keeping both ends of the curve gentle.
        var smooth = progress * progress * (3.0 - (2.0 * progress));
        return (InitialSpeedPixelsPerSecond +
            ((MaxSpeedPixelsPerSecond - InitialSpeedPixelsPerSecond) * smooth)) * multiplier;
    }

    private void OnTick(object? state)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _directions.Count == 0)
                    return;

                var now = Environment.TickCount64;
                var elapsed = now - _lastTickAt;
                if (elapsed <= 0)
                    return;

                _lastTickAt = now;
                // Never replay an arbitrarily long scheduling delay as one huge
                // pointer jump. Lost time is deliberately dropped under CPU load.
                var integratedMs = Math.Min(elapsed, MaxIntegratedTickMs);

                var x = (_directions.Contains((ushort)'L') ? 1.0 : 0.0) -
                        (_directions.Contains((ushort)'J') ? 1.0 : 0.0);
                var y = (_directions.Contains((ushort)'K') ? 1.0 : 0.0) -
                        (_directions.Contains((ushort)'I') ? 1.0 : 0.0);
                if (x == 0 && y == 0)
                    return;

                if (x != 0 && y != 0)
                {
                    const double inverseSqrtTwo = 0.7071067811865476;
                    x *= inverseSqrtTwo;
                    y *= inverseSqrtTwo;
                }

                var speed = SpeedAt(now - _startedAt, GetSpeedMultiplier());
                var distance = speed * integratedMs / 1000.0;
                _remainderX += x * distance;
                _remainderY += y * distance;

                var deltaX = (int)Math.Truncate(_remainderX);
                var deltaY = (int)Math.Truncate(_remainderY);
                _remainderX -= deltaX;
                _remainderY -= deltaY;

                if (deltaX != 0 || deltaY != 0)
                    _desktop.MovePointerBy(deltaX, deltaY);
            }
        }
        catch
        {
            // A ThreadPool timer exception must never terminate iKeyd. Stop the
            // movement session and require a fresh physical press after failure.
            Reset();
        }
    }

    private double GetSpeedMultiplier()
    {
        // Preserve the old D/E/C speed-modifier idea but apply it as a multiplier
        // to continuous motion instead of swapping between giant fixed jumps.
        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))
            return 0.18;
        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))
            return 0.45;
        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))
            return 2.5;
        return 1.0;
    }

    private void StopCore()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _startedAt = 0;
        _lastTickAt = 0;
        _remainderX = 0;
        _remainderY = 0;
    }

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
