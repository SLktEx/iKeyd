using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;

namespace iKeyd.App;

/// <summary>
/// Preserves the small set of pinned #IfWinActive hotkeys that sit outside the
/// normal hotkeySKG keymap/layer state machine.
/// </summary>
internal sealed class LegacyContextualHotkeyHandler : IKeyboardEventHandler, IInputStateResettable
{
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;
    private const ushort LeftAlt = 0xA4;
    private const ushort RightAlt = 0xA5;

    private readonly object _gate = new();
    private readonly KeyboardState _keyboardState;
    private readonly IDesktopBackend _desktop;
    private readonly IKeyboardOutput _keyboard;
    private readonly LegacySendOutput _send;
    private readonly Action<WindowHandle, uint> _postCommand;
    private readonly IKeyboardEventHandler _fallback;
    private readonly HashSet<ushort> _suppressedKeys = [];

    public LegacyContextualHotkeyHandler(
        KeyboardState keyboardState,
        IDesktopBackend desktop,
        IKeyboardOutput keyboard,
        LegacySendOutput send,
        Action<WindowHandle, uint> postCommand,
        IKeyboardEventHandler fallback)
    {
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _postCommand = postCommand ?? throw new ArgumentNullException(nameof(postCommand));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.Origin != KeyEventOrigin.Physical)
            return _fallback.OnKeyboardEvent(keyboardEvent);

        var virtualKey = keyboardEvent.Key.VirtualKey;
        lock (_gate)
        {
            if (keyboardEvent.Kind == KeyEventKind.Up && _suppressedKeys.Remove(virtualKey))
                return KeyboardDisposition.Suppress;
        }
        if (keyboardEvent.Kind != KeyEventKind.Down)
            return _fallback.OnKeyboardEvent(keyboardEvent);

        if (!TryGetActiveContext(out var window, out var windowClass))
            return _fallback.OnKeyboardEvent(keyboardEvent);

        if (string.Equals(windowClass, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) &&
            IsControlPressed())
        {
            if (virtualKey == 'V')
                return HandleConsoleHotkey(virtualKey, "ep");
            if (virtualKey == 'X')
                return HandleConsoleHotkey(virtualKey, "ek");
        }

        if (string.Equals(windowClass, "gsview_class", StringComparison.OrdinalIgnoreCase) &&
            virtualKey == 'E' &&
            IsAltPressed())
        {
            _postCommand(window, 105);
            lock (_gate)
                _suppressedKeys.Add(virtualKey);
            return KeyboardDisposition.Suppress;
        }

        return _fallback.OnKeyboardEvent(keyboardEvent);
    }

    public void ResetInputState()
    {
        lock (_gate)
            _suppressedKeys.Clear();

        if (_fallback is IInputStateResettable resettable)
            resettable.ResetInputState();
    }

    private KeyboardDisposition HandleConsoleHotkey(ushort triggerVirtualKey, string menuKeys)
    {
        // AHK's normal Send mode releases physically-held modifiers that are not
        // part of the requested Send, then restores them. Without this, the Ctrl
        // that triggered ^V/^X would leak into Alt+Space and the following menu
        // letters in the console system menu.
        var controls = _keyboardState.Snapshot()
            .Where(item => IsControlVirtualKey(item.VirtualKey))
            .ToArray();

        foreach (var control in controls)
            _keyboard.SendKey(control, KeyEventKind.Up);
        try
        {
            _send.Send(string.Concat("!{Space}", menuKeys));
        }
        finally
        {
            foreach (var control in controls)
                _keyboard.SendKey(control, KeyEventKind.Down);
        }

        lock (_gate)
            _suppressedKeys.Add(triggerVirtualKey);
        return KeyboardDisposition.Suppress;
    }

    private bool TryGetActiveContext(out WindowHandle window, out string? windowClass)
    {
        try
        {
            window = _desktop.GetActiveWindow();
            if (window.IsEmpty || !_desktop.IsWindow(window))
            {
                windowClass = null;
                return false;
            }
            windowClass = _desktop.GetWindowClass(window);
            return windowClass is not null;
        }
        catch (ArgumentException)
        {
            window = default;
            windowClass = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            window = default;
            windowClass = null;
            return false;
        }
    }

    private bool IsControlPressed()
        => _keyboardState.Snapshot().Any(item => IsControlVirtualKey(item.VirtualKey));

    private bool IsAltPressed()
        => _keyboardState.Snapshot().Any(item => item.VirtualKey is WindowsKeyMap.Alt or LeftAlt or RightAlt);

    private static bool IsControlVirtualKey(ushort virtualKey)
        => virtualKey is WindowsKeyMap.Control or LeftControl or RightControl;
}
