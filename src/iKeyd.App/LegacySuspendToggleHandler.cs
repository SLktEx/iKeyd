using iKeyd.Core.Input;
using iKeyd.Windows.Input;

namespace iKeyd.App;

/// <summary>
/// Preserves hotkeySKG's ^Esc::Suspend,Toggle behavior around the complete input
/// router. While suspended, physical input passes through unchanged and only the
/// suspend hotkey itself remains active so the user can resume processing.
/// </summary>
internal sealed class LegacySuspendToggleHandler : IKeyboardEventHandler, IInputStateResettable
{
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;

    private readonly object _gate = new();
    private readonly KeyboardState _keyboardState;
    private readonly IKeyboardEventHandler _inner;
    private bool _suspended;
    private bool _suppressEscapeUp;

    public LegacySuspendToggleHandler(KeyboardState keyboardState, IKeyboardEventHandler inner)
    {
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    internal bool IsSuspended
    {
        get
        {
            lock (_gate)
                return _suspended;
        }
    }

    public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.Origin != KeyEventOrigin.Physical)
            return _inner.OnKeyboardEvent(keyboardEvent);

        var resetInner = false;
        lock (_gate)
        {
            if (keyboardEvent.Key.VirtualKey == WindowsKeyMap.Escape)
            {
                if (keyboardEvent.Kind == KeyEventKind.Up && _suppressEscapeUp)
                {
                    _suppressEscapeUp = false;
                    return KeyboardDisposition.Suppress;
                }

                if (keyboardEvent.Kind == KeyEventKind.Down && IsControlPressed())
                {
                    _suspended = !_suspended;
                    _suppressEscapeUp = true;
                    resetInner = true;
                }
            }

            if (!resetInner && _suspended)
                return KeyboardDisposition.PassThrough;
        }

        if (resetInner)
        {
            ResetInner();
            return KeyboardDisposition.Suppress;
        }

        return _inner.OnKeyboardEvent(keyboardEvent);
    }

    public void ResetInputState()
    {
        lock (_gate)
            _suppressEscapeUp = false;
        ResetInner();
    }

    private void ResetInner()
    {
        if (_inner is IInputStateResettable resettable)
            resettable.ResetInputState();
    }

    private bool IsControlPressed()
        => _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control) ||
           _keyboardState.IsVirtualKeyPressed(LeftControl) ||
           _keyboardState.IsVirtualKeyPressed(RightControl);
}
