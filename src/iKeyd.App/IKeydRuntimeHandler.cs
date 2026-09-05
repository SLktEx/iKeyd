using System.Runtime.InteropServices;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using iKeyd.Core.Layers;
using iKeyd.Core.Macros;
using iKeyd.Core.Modes;
using iKeyd.Profiles.HotkeySkg.Runtime;
using iKeyd.Windows.Input;

namespace iKeyd.App;

internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable
{
    private const uint WmCommand = 0x0111;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    private readonly object _gate = new();
    private readonly IKeydConfiguration _configuration;
    private readonly IInputMethod _inputMethod;
    private readonly KeyboardState _keyboardState;
    private readonly LegacySendOutput _send;
    private readonly IDesktopBackend _desktop;
    private readonly DesktopActionService _desktopActions;
    private readonly WindowGroupController _windowGroup;
    private readonly IHotkeySkgInteractiveActions? _interactiveActions;
    private readonly ChordEngine<string> _sEngine;
    private readonly ChordEngine<string> _kEngine;
    private readonly HashSet<ushort> _suppressedKeys = [];
    private readonly Timer _chordTimer;

    private InputModeState _mode;
    private LayerRuntimeState _layers = LayerRuntimeState.Empty;
    private KeymapMode? _timerMode;
    private long _timerDueAt;
    private bool _suspended;
    private bool _disposed;

    public IKeydRuntimeHandler(
        IKeydConfiguration configuration,
        IInputMethod inputMethod,
        KeyboardState keyboardState,
        LegacySendOutput send,
        IDesktopBackend desktop,
        IHotkeySkgInteractiveActions? interactiveActions = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _inputMethod = inputMethod ?? throw new ArgumentNullException(nameof(inputMethod));
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _desktopActions = new DesktopActionService(desktop);
        _windowGroup = new WindowGroupController(desktop);
        _interactiveActions = interactiveActions;
        _sEngine = new ChordEngine<string>(configuration.SKeymap, configuration.ChordWindowMs);
        _kEngine = new ChordEngine<string>(configuration.KKeymap, configuration.ChordWindowMs);
        _mode = InputModeState.Initial.SwitchTo(configuration.StartupMode);
        _chordTimer = new Timer(OnChordTimeout, null, Timeout.Infinite, Timeout.Infinite);
    }

    public InputModeState Mode
    {
        get
        {
            lock (_gate)
                return _mode;
        }
    }

    internal bool IsSuspended
    {
        get
        {
            lock (_gate)
                return _suspended;
        }
    }

    public void SetMode(InputMode mode)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushAllPending();
            _mode = _mode.SwitchTo(mode);
        }
    }

    public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.Origin != KeyEventOrigin.Physical)
            return KeyboardDisposition.PassThrough;

        lock (_gate)
        {
            if (_disposed)
                return KeyboardDisposition.PassThrough;

            if (TryHandleSuspendToggle(keyboardEvent))
                return KeyboardDisposition.Suppress;

            if (_suspended)
            {
                if (keyboardEvent.Kind == KeyEventKind.Up &&
                    _suppressedKeys.Remove(keyboardEvent.Key.VirtualKey))
                    return KeyboardDisposition.Suppress;
                return KeyboardDisposition.PassThrough;
            }

            if (TryHandleContextHotkey(keyboardEvent))
                return KeyboardDisposition.Suppress;

            if (TryHandleLayerKey(keyboardEvent))
                return KeyboardDisposition.Suppress;

            if (keyboardEvent.Kind == KeyEventKind.Up)
                return _suppressedKeys.Remove(keyboardEvent.Key.VirtualKey)
                    ? KeyboardDisposition.Suppress
                    : KeyboardDisposition.PassThrough;

            var keyId = WindowsKeyMap.TryResolveKeyId(keyboardEvent.Key.VirtualKey);

            if (_layers.Layers.Count != 0)
            {
                var handled = keyId is not null && DispatchLayeredKey(keyId.Value, keyboardEvent.Key.VirtualKey);
                _layers = _layers.MarkConsumed();
                if (handled)
                {
                    _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);
                    return KeyboardDisposition.Suppress;
                }

                return KeyboardDisposition.PassThrough;
            }

            var route = _mode.Route(_inputMethod);
            if (route.Kind != InputRouteKind.ChordEngine || route.Keymap is null || keyId is null)
            {
                FlushAllPending();
                return KeyboardDisposition.PassThrough;
            }

            var keymap = _configuration.GetKeymap(route.Keymap.Value);
            if (!keymap.TryGetSingle(keyId.Value, out _))
            {
                FlushAllPending();
                return KeyboardDisposition.PassThrough;
            }

            var engine = GetEngine(route.Keymap.Value);
            SendOutputs(engine.AdvanceTo(keyboardEvent.TimestampMs));
            SendOutputs(engine.OnKeyDown(keyId.Value, keyboardEvent.TimestampMs));

            if (engine.State == ChordEngineState.PendingSingle)
                ScheduleTimeout(route.Keymap.Value);
            else
                CancelTimeout();

            _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);
            return KeyboardDisposition.Suppress;
        }
    }

    public ValueTask DispatchAsync(MacroHotkey hotkey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!DispatchFunctionKey(new KeyId(hotkey.Key.ToString()), hotkey.State))
                throw new NotSupportedException($"Macro hotkey '{{hk {hotkey.State}{hotkey.Key}}}' is not mapped by the hotkeySKG compatibility runtime.");
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            CancelTimeout();
            _sEngine.Cancel();
            _kEngine.Cancel();
            _suppressedKeys.Clear();
        }
        _chordTimer.Dispose();
    }

    private bool TryHandleSuspendToggle(KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.Key.VirtualKey != WindowsKeyMap.Escape ||
            keyboardEvent.Kind != KeyEventKind.Down ||
            !_keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control) ||
            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt) ||
            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift) ||
            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||
            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.RightWin))
            return false;

        FlushAllPending();
        _suspended = !_suspended;
        _suppressedKeys.Add(WindowsKeyMap.Escape);
        return true;
    }

    private bool TryHandleContextHotkey(KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.Kind != KeyEventKind.Down)
            return false;

        var window = _desktop.GetActiveWindow();
        if (!_desktop.IsWindow(window))
            return false;

        var className = _desktop.GetWindowClass(window);
        if (string.IsNullOrEmpty(className))
            return false;

        var ctrl = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control);
        var alt = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt);
        var shift = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift);
        var win = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||
                  _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.RightWin);

        if (string.Equals(className, "ConsoleWindowClass", StringComparison.Ordinal) &&
            ctrl && !alt && !shift && !win)
        {
            if (keyboardEvent.Key.VirtualKey == (ushort)'V')
            {
                FlushAllPending();
                _send.Send("!{Space}ep");
                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);
                return true;
            }

            if (keyboardEvent.Key.VirtualKey == (ushort)'X')
            {
                FlushAllPending();
                _send.Send("!{Space}ek");
                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);
                return true;
            }
        }

        if (string.Equals(className, "gsview_class", StringComparison.Ordinal) &&
            alt && !ctrl && !shift && !win &&
            keyboardEvent.Key.VirtualKey == (ushort)'E')
        {
            FlushAllPending();
            NativeMethods.PostMessageW(window.Value, WmCommand, 105, 0);
            _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);
            return true;
        }

        return false;
    }

    private bool TryHandleLayerKey(KeyboardEvent keyboardEvent)
    {
        var altPressed = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt);
        LayerEvent? layerEvent = keyboardEvent.Key.VirtualKey switch
        {
            WindowsKeyMap.NonConvert => keyboardEvent.Kind == KeyEventKind.Down ? LayerEvent.MDown : LayerEvent.MUp,
            WindowsKeyMap.Convert when altPressed => keyboardEvent.Kind == KeyEventKind.Down ? LayerEvent.AltHDown : LayerEvent.AltHUp,
            WindowsKeyMap.Convert => keyboardEvent.Kind == KeyEventKind.Down ? LayerEvent.HDown : LayerEvent.HUp,
            WindowsKeyMap.Space when altPressed => keyboardEvent.Kind == KeyEventKind.Down ? LayerEvent.AltSpaceDown : LayerEvent.AltSpaceUp,
            WindowsKeyMap.Space => keyboardEvent.Kind == KeyEventKind.Down ? LayerEvent.SpaceDown : LayerEvent.SpaceUp,
            WindowsKeyMap.Kana when keyboardEvent.Kind == KeyEventKind.Down && altPressed => LayerEvent.AltKanaDown,
            WindowsKeyMap.Kana when keyboardEvent.Kind == KeyEventKind.Down => LayerEvent.KanaDown,
            _ => null
        };

        if (keyboardEvent.Key.VirtualKey == WindowsKeyMap.Kana && keyboardEvent.Kind == KeyEventKind.Up)
            return true;
        if (layerEvent is null)
            return false;

        FlushAllPending();
        var transition = LayerStateMachine.Apply(_layers, layerEvent.Value);
        _layers = transition.State;
        foreach (var action in transition.Actions)
            SendLayerAction(action);
        return true;
    }

    private bool DispatchLayeredKey(KeyId key, ushort virtualKey)
    {
        var state = _layers.Layers.ToString();
        switch (state)
        {
            case "H":
                _send.SendChord(WindowsKeyMap.Control, virtualKey);
                return true;
            case "S":
                _send.SendChord(WindowsKeyMap.Shift, virtualKey);
                return true;
            case "HS":
                _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, virtualKey);
                return true;
            case "K":
                _send.SendChord(WindowsKeyMap.LeftWin, virtualKey);
                _layers = _layers with { Layers = _layers.Layers.Release(LayerKey.K), Consumed = true };
                return true;
            case "A":
                _send.SendChord(WindowsKeyMap.Alt, virtualKey);
                _layers = _layers with { Layers = _layers.Layers.Release(LayerKey.A), Consumed = true };
                return true;
            case "KH":
                _send.SendChord(WindowsKeyMap.LeftWin, WindowsKeyMap.Control, virtualKey);
                return true;
            case "KS":
                _send.SendChord(WindowsKeyMap.LeftWin, WindowsKeyMap.Shift, virtualKey);
                return true;
            case "AH":
                _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Control, virtualKey);
                return true;
            case "AS":
                _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Shift, virtualKey);
                return true;
            case "SH":
            case "KSH":
            case "ASH":
                return DispatchShiftHomeKey(key, state);
            case "SM":
                return DispatchMouseMedia(key);
            default:
                return DispatchFunctionKey(key, state);
        }
    }

    private bool DispatchShiftHomeKey(KeyId key, string state)
    {
        var output = key.Value.ToUpperInvariant() switch
        {
            "Q" => "#1",
            "W" => "#2",
            "E" => "#3",
            "R" => "#4",
            "T" => "#5",
            "Y" => "#6",
            "U" => "#7",
            "I" => "#8",
            "O" => "#9",
            "P" => "#0",
            "AT" => "{F11}",
            "A" => "1",
            "S" => "2",
            "D" => "3",
            "F" => "4",
            "G" => "5",
            "H" => "6",
            "J" => "7",
            "K" => "8",
            "L" => "9",
            "SCOLON" => "0",
            "COLON" => "{F12}",
            "Z" or "1" => "{F1}",
            "X" or "2" => "{F2}",
            "C" or "3" => "{F3}",
            "V" or "4" => "{F4}",
            "B" or "5" => "{F5}",
            "N" or "6" => "{F6}",
            "M" or "7" => "{F7}",
            "COMMA" or "8" => "{F8}",
            "DOT" or "9" => "{F9}",
            "SLASH" or "0" => "{F10}",
            _ => null
        };

        if (output is null)
            return false;

        var prefix = state switch
        {
            "KSH" => "^",
            "ASH" => "!",
            _ => string.Empty
        };
        _send.Send(prefix + output);
        return true;
    }

    private bool DispatchFunctionKey(KeyId key, string state)
    {
        var name = key.Value.ToUpperInvariant();

        switch (name)
        {
            case "Q":
                return SendWithFuncKey(state, "(", "\"", "'");
            case "W":
                return SendWithFuncKey(state, "!{F4}", "^{F4}");
            case "U":
                return SendWithFuncKey(state, "{HOME}", "+{HOME}", "^{HOME}", "^+{HOME}");
            case "I":
                return SendWithFuncKey(state, "{UP}", "+{UP}", "^{UP}", "^+{UP}");
            case "O":
                return SendWithFuncKey(state, "{END}", "+{END}", "^{END}", "^+{END}");
            case "P":
                return SendWithFuncKey(state, "{PGUP}", "+{PGUP}", "^{PGUP}", "^+{PGUP}");
            case "AT":
                return SendWithFuncKey(state, "{ESC}", "{AppsKey}", "!{Space}");
            case "A":
                return SendWithFuncKey(state, "[]{LEFT}", "{{}", "{{}{ENTER}{ENTER}{}}{UP}{END}");
            case "S":
                return SendWithFuncKey(state, "(){LEFT}", "{}}", "{{}{}}{LEFT}");
            case "D":
                return SendWithFuncKey(state, "-", "=", "%", "~");
            case "J":
                return SendWithFuncKey(state, "{LEFT}", "+{LEFT}", "^{LEFT}", "^+{LEFT}");
            case "K":
                return SendWithFuncKey(state, "{DOWN}", "+{DOWN}", "^{DOWN}", "^+{DOWN}");
            case "L":
                return SendWithFuncKey(state, "{RIGHT}", "+{RIGHT}", "^{RIGHT}", "^+{RIGHT}");
            case "SCOLON":
                return SendWithFuncKey(state, "{PGDN}", "+{PGDN}", "^{PGDN}", "^+{PGDN}");
            case "COLON":
                return SendWithFuncKey(state, "$", "{#}", "&", "{^}");
            case "Z":
                return SendWithFuncKey(state, "\\", "/", "|");
            case "X":
                return SendWithFuncKey(state, "\"\"{LEFT}", "''{LEFT}", "%%{LEFT}");
            case "C":
                return SendWithFuncKey(state, "_", ">", "<");
            case "N":
                return SendWithFuncKey(state, "{BS}", "!{RIGHT}", "!{LEFT}", "^+n");
            case "M":
                return SendWithFuncKey(state, "{DEL}", "{END}{SHIFT DOWN}{HOME}{LEFT}{SHIFT UP}", "{HOME}+{END}", "#m");
            case "COMMA":
                return SendWithFuncKey(state, "{SPACE}", "{TAB}", "{ENTER}");
            case "DOT":
                return SendWithFuncKey(state, ")", "<>{LEFT}", "</>{LEFT}");
            case "SLASH":
                return SendWithFuncKey(state, "{!}", "/*  */{LEFT 3}", "{END}+{HOME}^x\\begin{{}^v{}}{ENTER 2}\\end{{}^v{}}{UP}");
            case "E":
                if (state == "M") { _desktopActions.MinimizeActive(); return true; }
                if (state == "MH") { _send.Send("#{UP}"); return true; }
                if (state == "HM") { _send.Send("#{DOWN}"); return true; }
                return false;
            case "R":
                if (state == "M") { _desktopActions.ToggleMaximizeActive(); return true; }
                if (state == "MH") { _send.Send("#{RIGHT}"); return true; }
                if (state == "HM") { _send.Send("#{LEFT}"); return true; }
                if (state == "MS") { _send.Send("#r"); return true; }
                return false;
            case "T":
                if (state == "M") { _desktopActions.ToggleTopMostActive(); return true; }
                if (state == "MH") { _desktopActions.AdjustOpacityActive(-30); return true; }
                if (state == "HM") { _desktopActions.AdjustOpacityActive(30); return true; }
                if (state == "MS") { _desktopActions.ToggleCaptionActive(); return true; }
                return false;
            case "Y":
            case "H":
                return DispatchMacroKey(name[0], state);
            case "F":
                if (state == "M") { _send.Send("{vkF3sc029}"); return true; }
                if (state == "MH") { _send.SendKey(WindowsKeyMap.CapsLock); return true; }
                if (state == "HM") { _send.Send("{Ins}"); return true; }
                return false;
            case "G":
                if (state == "M") { _windowGroup.ActivateNext(); return true; }
                if (state == "MH") { _send.Send("^{TAB}"); return true; }
                if (state == "HM") { _send.Send("^+{TAB}"); return true; }
                if (state == "MS") { _windowGroup.ToggleActiveWindow(); return true; }
                return false;
            case "V":
                if (state == "M") { _interactiveActions?.ShowClipboardHistory(); return true; }
                if (state == "MH") { _interactiveActions?.CaptureLatestClipboard(); return true; }
                if (state == "HM") { _interactiveActions?.PasteCapturedClipboard(); return true; }
                return false;
            case "B":
                if (state == "M") { _desktopActions.ActivateBottomWindowOfActiveClass(); return true; }
                if (state == "MH") { _send.Send("!{ESC}"); return true; }
                if (state == "HM") { _send.Send("!+{ESC}"); return true; }
                if (state == "MS") { _windowGroup.ResetAndAdvance(); return true; }
                return false;
            case "1":
                _mode = _mode.SwitchTo(InputMode.S);
                return true;
            case "2":
                _mode = _mode.SwitchTo(InputMode.R);
                return true;
            case "3":
                _mode = _mode.SwitchTo(InputMode.T);
                return true;
            case "4":
                _mode = _mode.SwitchTo(InputMode.K);
                return true;
            case "5":
            case "6":
            case "7":
            case "8":
            case "9":
            case "0":
                return SendWithFuncKey(state, string.Empty, string.Empty, string.Empty, string.Empty);
            default:
                return false;
        }
    }

    private bool DispatchMacroKey(char slot, string state)
    {
        if (state == "M")
        {
            _interactiveActions?.RunMacro(slot);
            return true;
        }
        if (state == "MH")
        {
            _interactiveActions?.EditMacro(slot);
            return true;
        }
        if (state == "HM")
        {
            _interactiveActions?.EditMacroRepeat();
            return true;
        }
        return false;
    }

    private bool SendWithFuncKey(string state, string m = "", string mh = "", string hm = "", string ms = "")
    {
        var (output, heldModifier) = state switch
        {
            "M" => (m, (string?)null),
            "MH" => (mh, (string?)null),
            "HM" => (hm, (string?)null),
            "MS" => (ms, (string?)null),
            "KM" => (m, "CTRL"),
            "KMH" => (mh, "CTRL"),
            "KHM" => (hm, "CTRL"),
            "KMS" => (ms, "CTRL"),
            "AM" => (m, "ALT"),
            "AMH" => (mh, "ALT"),
            "AHM" => (hm, "ALT"),
            "AMS" => (ms, "ALT"),
            _ => ((string?)null, (string?)null)
        };

        if (output is null)
            return false;

        if (heldModifier is null)
        {
            _send.Send(output);
            return true;
        }

        _send.Send($"{{{heldModifier} DOWN}}");
        try
        {
            _send.Send(output);
        }
        finally
        {
            _send.Send($"{{{heldModifier} UP}}");
        }
        return true;
    }

    private bool DispatchMouseMedia(KeyId key)
    {
        var name = key.Value.ToUpperInvariant();
        switch (name)
        {
            case "D":
            case "E":
            case "C":
                return true;
            case "J":
                MoveMouse(-1, 0);
                return true;
            case "K":
                MoveMouse(0, 1);
                return true;
            case "L":
                MoveMouse(1, 0);
                return true;
            case "I":
                MoveMouse(0, -1);
                return true;
            case "U":
                _desktop.Click(DesktopMouseButton.Left);
                return true;
            case "O":
                _desktop.Click(DesktopMouseButton.Right);
                return true;
            case "P":
                _desktop.ScrollVertical(120);
                return true;
            case "SCOLON":
                _desktop.ScrollVertical(-120);
                return true;
            case "AT":
                _desktop.ScrollVertical(120, controlModifier: true);
                return true;
            case "COLON":
                _desktop.ScrollVertical(-120, controlModifier: true);
                return true;
            case "COMMA":
                _desktop.Click(DesktopMouseButton.Middle);
                return true;
            case "Y":
                _desktopActions.ToggleMouseButton(DesktopMouseButton.Left);
                return true;
            case "H":
                // Preserve the legacy typo `if s tate = U`: the right button can be released,
                // but the legacy script never reaches the right-button-down branch.
                if (_desktop.IsMouseButtonDown(DesktopMouseButton.Right))
                    _desktop.SetMouseButton(DesktopMouseButton.Right, false);
                return true;
            case "Q":
                _desktop.SendMediaCommand(DesktopMediaCommand.VolumeUp);
                return true;
            case "A":
                _desktop.SendMediaCommand(DesktopMediaCommand.VolumeMute);
                return true;
            case "Z":
                _desktop.SendMediaCommand(DesktopMediaCommand.VolumeDown);
                return true;
            case "R":
                _desktop.SendMediaCommand(DesktopMediaCommand.NextTrack);
                return true;
            case "F":
                _desktop.SendMediaCommand(DesktopMediaCommand.PlayPause);
                return true;
            case "V":
                _desktop.SendMediaCommand(DesktopMediaCommand.PreviousTrack);
                return true;
            case "N":
                MovePointerToLegacyWindowCorner(bottomRight: false);
                return true;
            case "M":
                MovePointerToLegacyWindowCorner(bottomRight: true);
                return true;
            default:
                return false;
        }
    }

    private void MoveMouse(int xDirection, int yDirection)
    {
        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))
        {
            _desktop.MovePointerBy(xDirection * 30, yDirection * 30);
            return;
        }
        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))
        {
            _desktop.MovePointerBy(xDirection * 10, yDirection * 10);
            return;
        }
        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))
        {
            var screenWidth = Math.Max(1, NativeMethods.GetSystemMetrics(SmCxScreen));
            var screenHeight = Math.Max(1, NativeMethods.GetSystemMetrics(SmCyScreen));
            _desktop.MovePointerBy(xDirection * Math.Max(1, screenWidth / 4), yDirection * Math.Max(1, screenHeight / 4));
            return;
        }
        _desktop.MovePointerBy(xDirection * 100, yDirection * 100);
    }

    private void MovePointerToLegacyWindowCorner(bool bottomRight)
    {
        var window = _desktop.GetActiveWindow();
        if (!_desktop.IsWindow(window))
            return;
        var bounds = _desktop.GetWindowBounds(window);
        if (bounds.X < 0)
            return;
        var point = bottomRight
            ? new DesktopPoint(bounds.Right - 1, bounds.Bottom - 1)
            : new DesktopPoint(bounds.X + 1, bounds.Y + 1);
        _desktop.MovePointer(point);
    }

    private void SendLayerAction(LayerAction action)
    {
        switch (action)
        {
            case LayerAction.Tab: _send.SendKey(WindowsKeyMap.Tab); break;
            case LayerAction.ShiftTab: _send.SendChord(WindowsKeyMap.Shift, WindowsKeyMap.Tab); break;
            case LayerAction.ShiftEnter: _send.SendChord(WindowsKeyMap.Shift, WindowsKeyMap.Enter); break;
            case LayerAction.ShiftSpace: _send.SendChord(WindowsKeyMap.Shift, WindowsKeyMap.Space); break;
            case LayerAction.Ctrl: _send.SendKey(WindowsKeyMap.Control); break;
            case LayerAction.Space: _send.SendKey(WindowsKeyMap.Space); break;
            case LayerAction.Enter: _send.SendKey(WindowsKeyMap.Enter); break;
            case LayerAction.CtrlSpace: _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Space); break;
            case LayerAction.CtrlEnter: _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Enter); break;
            case LayerAction.AltEnter: _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Enter); break;
            case LayerAction.AltSpace: _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Space); break;
            case LayerAction.CtrlEsc: _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Escape); break;
            case LayerAction.Muhenkan: _send.SendKey(WindowsKeyMap.NonConvert); break;
            case LayerAction.Henkan: _send.SendKey(WindowsKeyMap.Convert); break;
            case LayerAction.EndEnter:
                _send.SendKey(WindowsKeyMap.End);
                _send.SendKey(WindowsKeyMap.Enter);
                break;
            case LayerAction.UpEndEnter:
                _send.SendKey(WindowsKeyMap.Up);
                _send.SendKey(WindowsKeyMap.End);
                _send.SendKey(WindowsKeyMap.Enter);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private ChordEngine<string> GetEngine(KeymapMode mode)
        => mode == KeymapMode.S ? _sEngine : _kEngine;

    private void SendOutputs(IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
            _send.Send(output);
    }

    private void FlushAllPending()
    {
        SendOutputs(_sEngine.Flush());
        SendOutputs(_kEngine.Flush());
        CancelTimeout();
    }

    private void ScheduleTimeout(KeymapMode mode)
    {
        _timerMode = mode;
        _timerDueAt = Environment.TickCount64 + _configuration.ChordWindowMs + 1L;
        _chordTimer.Change(_configuration.ChordWindowMs + 1, Timeout.Infinite);
    }

    private void CancelTimeout()
    {
        _timerMode = null;
        _timerDueAt = 0;
        _chordTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnChordTimeout(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _timerMode is null)
                return;

            var remaining = _timerDueAt - Environment.TickCount64;
            if (remaining > 0)
            {
                _chordTimer.Change((int)Math.Min(int.MaxValue, remaining), Timeout.Infinite);
                return;
            }

            var mode = _timerMode.Value;
            _timerMode = null;
            _timerDueAt = 0;
            SendOutputs(GetEngine(mode).Flush());
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
    }
}
