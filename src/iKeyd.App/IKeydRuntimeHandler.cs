using System.Runtime.InteropServices;
using iKeyd.Core.Chords;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using iKeyd.Core.Layers;
using iKeyd.Core.Macros;
using iKeyd.Core.Modes;
using iKeyd.Windows.Input;

namespace iKeyd.App;

internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable
{
    private const uint WmCommand = 0x0111;
    private readonly object _gate = new();
    private readonly IKeydConfiguration _configuration;
    private readonly IInputMethod _inputMethod;
    private readonly KeyboardState _keyboardState;
    private readonly LegacySendOutput _send;
    private readonly IDesktopBackend _desktop;
    private readonly DesktopActionService _desktopActions;
    private readonly WindowGroupController _windowGroup;
    private readonly ChordEngine<string> _sEngine;
    private readonly ChordEngine<string> _kEngine;
    private readonly HashSet<ushort> _suppressedKeys = new(64);
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
        IDesktopBackend desktop)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _inputMethod = inputMethod ?? throw new ArgumentNullException(nameof(inputMethod));
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _desktopActions = new DesktopActionService(desktop);
        _windowGroup = new WindowGroupController(desktop);
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
            if (engine.TryAdvanceTo(keyboardEvent.TimestampMs, out var timedOutOutput))
                _send.Send(timedOutOutput);
            if (engine.TryOnKeyDown(keyId.Value, keyboardEvent.TimestampMs, out var output))
                _send.Send(output);

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
            if (!KeyId.TryFromCharacter(hotkey.Key, out var key) || !DispatchFunctionKey(key, hotkey.State))
                throw new NotSupportedException($"Macro hotkey '{{hk {hotkey.State}{hotkey.Key}}}' is not mapped by the Windows v1 runtime.");
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
            _keyboardState.IsVirtualKeyPressed(0x5C))
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
                  _keyboardState.IsVirtualKeyPressed(0x5C);

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
        var state = _layers.Layers;

        if (state.IsExact(LayerKey.H))
        {
            _send.SendChord(WindowsKeyMap.Control, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.S))
        {
            _send.SendChord(WindowsKeyMap.Shift, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.H, LayerKey.S))
        {
            _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.K))
        {
            _send.SendChord(WindowsKeyMap.LeftWin, virtualKey);
            _layers = _layers with { Layers = _layers.Layers.Release(LayerKey.K), Consumed = true };
            return true;
        }
        if (state.IsExact(LayerKey.A))
        {
            _send.SendChord(WindowsKeyMap.Alt, virtualKey);
            _layers = _layers with { Layers = _layers.Layers.Release(LayerKey.A), Consumed = true };
            return true;
        }
        if (state.IsExact(LayerKey.K, LayerKey.H))
        {
            _send.SendChord(WindowsKeyMap.LeftWin, WindowsKeyMap.Control, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.K, LayerKey.S))
        {
            _send.SendChord(WindowsKeyMap.LeftWin, WindowsKeyMap.Shift, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.A, LayerKey.H))
        {
            _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Control, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.A, LayerKey.S))
        {
            _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Shift, virtualKey);
            return true;
        }
        if (state.IsExact(LayerKey.S, LayerKey.M))
            return DispatchMouseMedia(key);

        return DispatchFunctionKey(key, state);
    }

    private bool DispatchFunctionKey(KeyId key, LayerState state)
    {
        if (LegacyFunctionSendMap.TryResolve(key.Code, state, out var legacySend))
        {
            if (legacySend.Length != 0)
                _send.Send(legacySend);
            return true;
        }

        switch (key.Code)
        {
            case KeyCode.E:
                if (state.IsExact(LayerKey.M)) { _desktopActions.MinimizeActive(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _desktopActions.PlaceActive(DesktopPlacement.TopHalf); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _desktopActions.PlaceActive(DesktopPlacement.BottomHalf); return true; }
                break;

            case KeyCode.R:
                if (state.IsExact(LayerKey.M)) { _desktopActions.ToggleMaximizeActive(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _desktopActions.PlaceActive(DesktopPlacement.RightHalf); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _desktopActions.PlaceActive(DesktopPlacement.LeftHalf); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.S)) { _send.SendChord(WindowsKeyMap.LeftWin, (ushort)'R'); return true; }
                break;

            case KeyCode.T:
                if (state.IsExact(LayerKey.M)) { _desktopActions.ToggleTopMostActive(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _desktopActions.AdjustOpacityActive(-30); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _desktopActions.AdjustOpacityActive(30); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.S)) { _desktopActions.ToggleCaptionActive(); return true; }
                break;

            case KeyCode.G:
                if (state.IsExact(LayerKey.M)) { _windowGroup.ActivateNext(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Tab); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, WindowsKeyMap.Tab); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.S)) { _windowGroup.ToggleActiveWindow(); return true; }
                break;

            case KeyCode.B:
                if (state.IsExact(LayerKey.M)) { _desktopActions.ActivateBottomWindowOfActiveClass(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Escape); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Shift, WindowsKeyMap.Escape); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.S)) { _windowGroup.ResetAndAdvance(); return true; }
                break;
        }

        return false;
    }

    // Macro actions use legacy state strings and are not on the physical keyboard
    // hot path. Keep this overload for compatibility without making layered input
    // stringify LayerState on every event.
    private bool DispatchFunctionKey(KeyId key, string state)
    {
        if (LegacyFunctionSendMap.TryResolve(key.Code, state, out var legacySend))
        {
            if (legacySend.Length != 0)
                _send.Send(legacySend);
            return true;
        }

        var name = key.Value;

        if (name == "E")
        {
            if (state == "M") { _desktopActions.MinimizeActive(); return true; }
            if (state == "MH") { _desktopActions.PlaceActive(DesktopPlacement.TopHalf); return true; }
            if (state == "HM") { _desktopActions.PlaceActive(DesktopPlacement.BottomHalf); return true; }
        }

        if (name == "R")
        {
            if (state == "M") { _desktopActions.ToggleMaximizeActive(); return true; }
            if (state == "MH") { _desktopActions.PlaceActive(DesktopPlacement.RightHalf); return true; }
            if (state == "HM") { _desktopActions.PlaceActive(DesktopPlacement.LeftHalf); return true; }
            if (state == "MS") { _send.SendChord(WindowsKeyMap.LeftWin, (ushort)'R'); return true; }
        }

        if (name == "T")
        {
            if (state == "M") { _desktopActions.ToggleTopMostActive(); return true; }
            if (state == "MH") { _desktopActions.AdjustOpacityActive(-30); return true; }
            if (state == "HM") { _desktopActions.AdjustOpacityActive(30); return true; }
            if (state == "MS") { _desktopActions.ToggleCaptionActive(); return true; }
        }

        if (name == "G")
        {
            if (state == "M") { _windowGroup.ActivateNext(); return true; }
            if (state == "MH") { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Tab); return true; }
            if (state == "HM") { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, WindowsKeyMap.Tab); return true; }
            if (state == "MS") { _windowGroup.ToggleActiveWindow(); return true; }
        }

        if (name == "B")
        {
            if (state == "M") { _desktopActions.ActivateBottomWindowOfActiveClass(); return true; }
            if (state == "MH") { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Escape); return true; }
            if (state == "HM") { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Shift, WindowsKeyMap.Escape); return true; }
            if (state == "MS") { _windowGroup.ResetAndAdvance(); return true; }
        }

        return false;
    }

    private bool DispatchMouseMedia(KeyId key)
    {
        switch (key.Code)
        {
            case KeyCode.D:
            case KeyCode.E:
            case KeyCode.C:
                return true;
            case KeyCode.J:
                MoveMouse(-1, 0);
                return true;
            case KeyCode.K:
                MoveMouse(0, 1);
                return true;
            case KeyCode.L:
                MoveMouse(1, 0);
                return true;
            case KeyCode.I:
                MoveMouse(0, -1);
                return true;
            case KeyCode.U:
                _desktop.Click(DesktopMouseButton.Left);
                return true;
            case KeyCode.O:
                _desktop.Click(DesktopMouseButton.Right);
                return true;
            case KeyCode.Comma:
                _desktop.Click(DesktopMouseButton.Middle);
                return true;
            case KeyCode.Y:
                _desktopActions.ToggleMouseButton(DesktopMouseButton.Left);
                return true;
            case KeyCode.H:
                // Preserve the pinned legacy typo: right-button down is unreachable,
                // but an already-held right button is released.
                if (_desktop.IsMouseButtonDown(DesktopMouseButton.Right))
                    _desktop.SetMouseButton(DesktopMouseButton.Right, false);
                return true;
            case KeyCode.N:
                MovePointerToLegacyWindowCorner(bottomRight: false);
                return true;
            case KeyCode.M:
                MovePointerToLegacyWindowCorner(bottomRight: true);
                return true;
            case KeyCode.P:
                _desktop.ScrollVertical(120);
                return true;
            case KeyCode.SColon:
                _desktop.ScrollVertical(-120);
                return true;
            case KeyCode.At:
                _desktop.ScrollVertical(120, controlModifier: true);
                return true;
            case KeyCode.Colon:
                _desktop.ScrollVertical(-120, controlModifier: true);
                return true;
            case KeyCode.Q:
                _desktop.SendMediaCommand(DesktopMediaCommand.VolumeUp);
                return true;
            case KeyCode.A:
                _desktop.SendMediaCommand(DesktopMediaCommand.VolumeMute);
                return true;
            case KeyCode.Z:
                _desktop.SendMediaCommand(DesktopMediaCommand.VolumeDown);
                return true;
            case KeyCode.R:
                _desktop.SendMediaCommand(DesktopMediaCommand.NextTrack);
                return true;
            case KeyCode.F:
                _desktop.SendMediaCommand(DesktopMediaCommand.PlayPause);
                return true;
            case KeyCode.V:
                _desktop.SendMediaCommand(DesktopMediaCommand.PreviousTrack);
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
            var area = _desktop.GetPrimaryWorkArea();
            _desktop.MovePointerBy(
                xDirection * Math.Max(1, area.Width / 4),
                yDirection * Math.Max(1, area.Height / 4));
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

    private void FlushAllPending()
    {
        if (_sEngine.TryFlush(out var sOutput))
            _send.Send(sOutput);
        if (_kEngine.TryFlush(out var kOutput))
            _send.Send(kOutput);
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
            if (GetEngine(mode).TryFlush(out var output))
                _send.Send(output);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    }
}
