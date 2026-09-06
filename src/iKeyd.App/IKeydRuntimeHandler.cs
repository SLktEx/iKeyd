using iKeyd.Core.Chords;
using iKeyd.Core.Clipboard;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using iKeyd.Core.Layers;
using iKeyd.Core.Macros;
using iKeyd.Core.Modes;
using iKeyd.Windows.Input;

namespace iKeyd.App;

internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IInputStateResettable, IMacroActionDispatcher, IDisposable
{
    private static readonly ushort[] NoModifiers = [];

    private readonly object _gate = new();
    private readonly IKeydConfiguration _configuration;
    private readonly IInputMethod _inputMethod;
    private readonly KeyboardState _keyboardState;
    private readonly LegacySendOutput _send;
    private readonly IDesktopBackend _desktop;
    private readonly IClipboardHistoryActions? _clipboard;
    private readonly DesktopActionService _desktopActions;
    private readonly WindowGroupController _windowGroups;
    private readonly ChordEngine<string> _sEngine;
    private readonly ChordEngine<string> _kEngine;
    private readonly HashSet<ushort> _suppressedKeys = new(64);
    private readonly Dictionary<ushort, LayerEvent> _heldLayerPresses = new(4);
    private readonly Timer _chordTimer;
    private readonly KeyboardMouseMotion _mouseMotion;
    private readonly InputDiagnosticsBuffer _diagnostics = new();

    private ILegacyMacroSlotActions? _macroSlots;
    private InputModeState _mode;
    private LayerRuntimeState _layers = LayerRuntimeState.Empty;
    private KeymapMode? _timerMode;
    private long _timerDueAt;
    private bool _disposed;

    public IKeydRuntimeHandler(
        IKeydConfiguration configuration,
        IInputMethod inputMethod,
        KeyboardState keyboardState,
        LegacySendOutput send,
        IDesktopBackend desktop,
        IClipboardHistoryActions? clipboard = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _inputMethod = inputMethod ?? throw new ArgumentNullException(nameof(inputMethod));
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _clipboard = clipboard;
        _desktopActions = new DesktopActionService(desktop);
        _windowGroups = new WindowGroupController(desktop);
        _sEngine = new ChordEngine<string>(configuration.SKeymap, configuration.ChordWindowMs);
        _kEngine = new ChordEngine<string>(configuration.KKeymap, configuration.ChordWindowMs);
        _mode = InputModeState.Initial.SwitchTo(configuration.StartupMode);
        _chordTimer = new Timer(OnChordTimeout, null, Timeout.Infinite, Timeout.Infinite);
        _mouseMotion = new KeyboardMouseMotion(desktop, keyboardState);
    }

    public InputModeState Mode
    {
        get
        {
            lock (_gate)
                return _mode;
        }
    }

    internal string ExportInputDiagnostics()
    {
        lock (_gate)
            return _diagnostics.ExportText();
    }

    internal InputDiagnosticEntry[] GetInputDiagnosticSnapshot()
    {
        lock (_gate)
            return _diagnostics.Snapshot();
    }

    internal void AttachMacroSlotActions(ILegacyMacroSlotActions macroSlots)
    {
        ArgumentNullException.ThrowIfNull(macroSlots);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_macroSlots is not null)
                throw new InvalidOperationException("Legacy macro-slot actions are already attached.");
            _macroSlots = macroSlots;
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

        if (keyboardEvent.Kind == KeyEventKind.Down && keyboardEvent.Key.VirtualKey == WindowsKeyMap.Escape)
        {
            ILegacyMacroSlotActions? slots;
            lock (_gate)
                slots = _macroSlots;
            slots?.Cancel();
        }

        lock (_gate)
        {
            if (_disposed)
                return KeyboardDisposition.PassThrough;

            var before = CaptureDiagnosticState();
            try
            {
                var disposition = HandlePhysicalKeyboardEventCore(keyboardEvent);
                var after = CaptureDiagnosticState();
                _diagnostics.RecordEvent(keyboardEvent, before, after, disposition);
                RecoverIfInvariantBroken(keyboardEvent.TimestampMs);
                return disposition;
            }
            catch (Exception error)
            {
                var failed = CaptureDiagnosticState();
                ResetInputStateCore();
                var recovered = CaptureDiagnosticState();
                _diagnostics.RecordMarker(
                    keyboardEvent.TimestampMs,
                    InputDiagnosticKind.Exception,
                    failed,
                    recovered,
                    error.HResult);
                throw;
            }
        }
    }

    private KeyboardDisposition HandlePhysicalKeyboardEventCore(KeyboardEvent keyboardEvent)
    {
        if (TryHandleLayerKey(keyboardEvent))
            return KeyboardDisposition.Suppress;

        if (keyboardEvent.Kind == KeyEventKind.Up && _mouseMotion.TryRelease(keyboardEvent.Key.VirtualKey))
        {
            _suppressedKeys.Remove(keyboardEvent.Key.VirtualKey);
            return KeyboardDisposition.Suppress;
        }

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
            SendKeymapOutput(timedOutOutput);
        if (engine.TryOnKeyDown(keyId.Value, keyboardEvent.TimestampMs, out var output))
            SendKeymapOutput(output);

        if (engine.State == ChordEngineState.PendingSingle)
            ScheduleTimeout(route.Keymap.Value);
        else
            CancelTimeout();

        _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);
        return KeyboardDisposition.Suppress;
    }

    public ValueTask DispatchAsync(MacroHotkey hotkey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var slot = char.ToUpperInvariant(hotkey.Key);
        if (slot is 'H' or 'Y')
        {
            ILegacyMacroSlotActions? macroSlots;
            lock (_gate)
            {
                ThrowIfDisposed();
                macroSlots = _macroSlots;
            }

            if (macroSlots is null)
                return ValueTask.CompletedTask;

            return hotkey.State.ToUpperInvariant() switch
            {
                "M" => macroSlots.RunAsync(slot, cancellationToken),
                "MH" => macroSlots.EditTemplateAsync(slot, cancellationToken),
                "HM" => macroSlots.EditRepeatAsync(cancellationToken),
                _ => ValueTask.CompletedTask
            };
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!KeyId.TryFromCharacter(hotkey.Key, out var key) || !DispatchFunctionKey(key, hotkey.State))
                throw new NotSupportedException($"Macro hotkey '{{hk {hotkey.State}{hotkey.Key}}}' is not mapped by the Windows v1 runtime.");
        }

        return ValueTask.CompletedTask;
    }

    public void ResetInputState()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var before = CaptureDiagnosticState();
            ResetInputStateCore();
            _diagnostics.RecordMarker(
                Environment.TickCount64,
                InputDiagnosticKind.Reset,
                before,
                CaptureDiagnosticState(),
                detailCode: 1);
        }
    }

    public void Dispose()
    {
        ILegacyMacroSlotActions? slots;
        lock (_gate)
        {
            if (_disposed)
                return;
            var before = CaptureDiagnosticState();
            ResetInputStateCore();
            _diagnostics.RecordMarker(
                Environment.TickCount64,
                InputDiagnosticKind.Reset,
                before,
                CaptureDiagnosticState(),
                detailCode: 2);
            _disposed = true;
            slots = _macroSlots;
        }
        slots?.Cancel();
        _mouseMotion.Dispose();
        _chordTimer.Dispose();
    }

    private void ResetInputStateCore()
    {
        CancelTimeout();
        _sEngine.Cancel();
        _kEngine.Cancel();
        _layers = LayerRuntimeState.Empty;
        _heldLayerPresses.Clear();
        _suppressedKeys.Clear();
        _mouseMotion.Reset();
    }

    private bool TryHandleLayerKey(KeyboardEvent keyboardEvent)
    {
        var virtualKey = keyboardEvent.Key.VirtualKey;
        if (!IsLayerTrigger(virtualKey))
            return false;

        if (keyboardEvent.Kind == KeyEventKind.Down)
        {
            // Low-level keyboard hooks receive repeated Down events while a key is
            // held. A layer press is an edge, not a repeatable action. Reapplying
            // MDown/SpaceDown used to reset Consumed and KanaDown could even toggle
            // K repeatedly, eventually corrupting the state machine.
            if (_heldLayerPresses.ContainsKey(virtualKey))
                return true;

            var pressEvent = ResolveLayerPress(virtualKey);
            _heldLayerPresses.Add(virtualKey, pressEvent);
            ApplyLayerEvent(pressEvent);
            return true;
        }

        // Release must match the variant selected at key-down. Recomputing from
        // the current Alt state means Alt+Convert followed by Alt-up, Convert-up
        // incorrectly becomes HUp instead of AltHUp and can leave A/H stuck.
        if (!_heldLayerPresses.Remove(virtualKey, out var originalPress))
            return true;

        var releaseEvent = ResolveLayerRelease(originalPress);
        if (releaseEvent is { } value)
            ApplyLayerEvent(value);
        return true;
    }

    private void ApplyLayerEvent(LayerEvent layerEvent)
    {
        FlushAllPending();
        var transition = LayerStateMachine.Apply(_layers, layerEvent);
        _layers = transition.State;
        foreach (var action in transition.Actions)
            SendLayerAction(action);
    }

    private LayerEvent ResolveLayerPress(ushort virtualKey)
    {
        var altPressed = IsAltPressed();
        return virtualKey switch
        {
            WindowsKeyMap.NonConvert => LayerEvent.MDown,
            WindowsKeyMap.Convert when altPressed => LayerEvent.AltHDown,
            WindowsKeyMap.Convert => LayerEvent.HDown,
            WindowsKeyMap.Space when altPressed => LayerEvent.AltSpaceDown,
            WindowsKeyMap.Space => LayerEvent.SpaceDown,
            WindowsKeyMap.Kana when altPressed => LayerEvent.AltKanaDown,
            WindowsKeyMap.Kana => LayerEvent.KanaDown,
            _ => throw new ArgumentOutOfRangeException(nameof(virtualKey))
        };
    }

    private static LayerEvent? ResolveLayerRelease(LayerEvent pressEvent)
        => pressEvent switch
        {
            LayerEvent.MDown => LayerEvent.MUp,
            LayerEvent.HDown => LayerEvent.HUp,
            LayerEvent.AltHDown => LayerEvent.AltHUp,
            LayerEvent.SpaceDown => LayerEvent.SpaceUp,
            LayerEvent.AltSpaceDown => LayerEvent.AltSpaceUp,
            LayerEvent.KanaDown or LayerEvent.AltKanaDown => null,
            _ => null
        };

    private bool IsAltPressed()
        => _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt) ||
           _keyboardState.IsVirtualKeyPressed(0xA4) ||
           _keyboardState.IsVirtualKeyPressed(0xA5);

    private static bool IsLayerTrigger(ushort virtualKey)
        => virtualKey is WindowsKeyMap.NonConvert or WindowsKeyMap.Convert or WindowsKeyMap.Space or WindowsKeyMap.Kana;

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
            return DispatchMouseMedia(key, virtualKey);

        return DispatchFunctionKey(key, state);
    }

    private bool DispatchFunctionKey(KeyId key, LayerState state)
    {
        if (TrySwitchLegacyModeKey(key.Code))
            return true;

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

            case KeyCode.F:
                if (state.IsExact(LayerKey.M)) { SendLegacyVirtualScanF(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _send.SendKey(WindowsKeyMap.CapsLock); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _send.SendKey(WindowsKeyMap.Insert); return true; }
                break;

            case KeyCode.G:
                if (state.IsExact(LayerKey.M)) { _windowGroups.ActivateNext(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Tab); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, WindowsKeyMap.Tab); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.S)) { _windowGroups.ToggleActiveWindow(); return true; }
                break;

            case KeyCode.B:
                if (state.IsExact(LayerKey.M)) { _desktopActions.ActivateBottomWindowOfActiveClass(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Escape); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Shift, WindowsKeyMap.Escape); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.S)) { _windowGroups.ResetAndAdvance(); return true; }
                break;

            case KeyCode.V:
                if (_clipboard is null)
                    break;
                if (state.IsExact(LayerKey.M)) { _clipboard.ShowPickerAndPaste(); return true; }
                if (state.IsExact(LayerKey.M, LayerKey.H)) { _clipboard.CaptureLatest(); return true; }
                if (state.IsExact(LayerKey.H, LayerKey.M)) { _clipboard.PasteCaptured(); return true; }
                break;

            case KeyCode.Y:
                return DispatchMacroSlotDetached('Y', state);

            case KeyCode.H:
                return DispatchMacroSlotDetached('H', state);
        }

        return false;
    }

    private bool DispatchFunctionKey(KeyId key, string state)
    {
        if (TrySwitchLegacyModeKey(key.Code))
            return true;

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

        if (name == "F")
        {
            if (state == "M") { SendLegacyVirtualScanF(); return true; }
            if (state == "MH") { _send.SendKey(WindowsKeyMap.CapsLock); return true; }
            if (state == "HM") { _send.SendKey(WindowsKeyMap.Insert); return true; }
        }

        if (name == "G")
        {
            if (state == "M") { _windowGroups.ActivateNext(); return true; }
            if (state == "MH") { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Tab); return true; }
            if (state == "HM") { _send.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, WindowsKeyMap.Tab); return true; }
            if (state == "MS") { _windowGroups.ToggleActiveWindow(); return true; }
        }

        if (name == "B")
        {
            if (state == "M") { _desktopActions.ActivateBottomWindowOfActiveClass(); return true; }
            if (state == "MH") { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Escape); return true; }
            if (state == "HM") { _send.SendChord(WindowsKeyMap.Alt, WindowsKeyMap.Shift, WindowsKeyMap.Escape); return true; }
            if (state == "MS") { _windowGroups.ResetAndAdvance(); return true; }
        }

        if (name == "V" && _clipboard is not null)
        {
            if (state == "M") { _clipboard.ShowPickerAndPaste(); return true; }
            if (state == "MH") { _clipboard.CaptureLatest(); return true; }
            if (state == "HM") { _clipboard.PasteCaptured(); return true; }
        }

        if (name is "Y" or "H")
            return true;

        return false;
    }

    private bool TrySwitchLegacyModeKey(KeyCode key)
    {
        InputMode? target = key switch
        {
            KeyCode.Digit1 => InputMode.S,
            KeyCode.Digit2 => InputMode.R,
            KeyCode.Digit3 => InputMode.T,
            KeyCode.Digit4 => InputMode.K,
            _ => null
        };

        if (target is null)
            return false;

        FlushAllPending();
        _mode = _mode.SwitchTo(target.Value);
        return true;
    }

    private bool DispatchMacroSlotDetached(char slot, LayerState state)
    {
        var macroSlots = _macroSlots;
        if (macroSlots is not null)
        {
            if (state.IsExact(LayerKey.M))
                ObserveDetached(macroSlots.RunAsync(slot));
            else if (state.IsExact(LayerKey.M, LayerKey.H))
                ObserveDetached(macroSlots.EditTemplateAsync(slot));
            else if (state.IsExact(LayerKey.H, LayerKey.M))
                ObserveDetached(macroSlots.EditRepeatAsync());
        }

        return true;
    }

    private static void ObserveDetached(ValueTask action)
    {
        if (action.IsCompletedSuccessfully)
            return;
        _ = ObserveDetachedAsync(action);
    }

    private static async Task ObserveDetachedAsync(ValueTask action)
    {
        try
        {
            await action.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private bool DispatchMouseMedia(KeyId key, ushort virtualKey)
    {
        if (_mouseMotion.TryStart(key, virtualKey))
            return true;

        switch (key.Code)
        {
            case KeyCode.D:
            case KeyCode.E:
            case KeyCode.C:
                return true;
            case KeyCode.U:
                _desktop.Click(DesktopMouseButton.Left);
                return true;
            case KeyCode.O:
                _desktop.Click(DesktopMouseButton.Right);
                return true;
            case KeyCode.Y:
                ToggleMouseButton(DesktopMouseButton.Left);
                return true;
            case KeyCode.H:
                ToggleMouseButton(DesktopMouseButton.Right);
                return true;
            case KeyCode.N:
                MovePointerToActiveWindowCorner(bottomRight: false);
                return true;
            case KeyCode.M:
                MovePointerToActiveWindowCorner(bottomRight: true);
                return true;
            case KeyCode.Comma:
                _desktop.Click(DesktopMouseButton.Middle);
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

    private void ToggleMouseButton(DesktopMouseButton button)
        => _desktop.SetMouseButton(button, !_desktop.IsMouseButtonDown(button));

    private void MovePointerToActiveWindowCorner(bool bottomRight)
    {
        var bounds = _desktop.GetWindowBounds(_desktop.GetActiveWindow());
        if (bounds.X < 0)
            return;

        _desktop.MovePointer(bottomRight
            ? new DesktopPoint(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1)
            : new DesktopPoint(bounds.X + 1, bounds.Y + 1));
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

    private void SendKeymapOutput(string output)
    {
        if (output.Length == 0)
            return;

        // S/K map values are romaji intended for the active Japanese IME. The old
        // path used SendText(), which becomes KEYEVENTF_UNICODE and therefore
        // bypasses IME composition entirely ("fa" stays literal "fa"). When the
        // complete output is representable as ordinary JIS keys, inject key presses
        // instead. Legacy tokens / uncommon symbols retain the existing parser.
        foreach (var character in output)
        {
            if (!WindowsKeyMap.TryResolveCharacter(character, out _))
            {
                _diagnostics.RecordOutput(
                    Environment.TickCount64,
                    InputDiagnosticKind.KeymapOutputLegacy,
                    CaptureDiagnosticState(),
                    output);
                _send.Send(output);
                return;
            }
        }

        _diagnostics.RecordOutput(
            Environment.TickCount64,
            InputDiagnosticKind.KeymapOutputKeys,
            CaptureDiagnosticState(),
            output);
        foreach (var character in output)
        {
            WindowsKeyMap.TryResolveCharacter(character, out var key);
            _send.SendChord(NoModifiers, key.VirtualKey);
        }
    }

    private void SendLegacyVirtualScanF()
    {
        const string legacyKey = "{vkF3sc029}";
        _diagnostics.RecordOutput(
            Environment.TickCount64,
            InputDiagnosticKind.LegacyVirtualScan,
            CaptureDiagnosticState(),
            legacyKey,
            detailCode: KeyCode.F.GetHashCode());
        _send.Send(legacyKey);
    }

    private ChordEngine<string> GetEngine(KeymapMode mode)
        => mode == KeymapMode.S ? _sEngine : _kEngine;

    private void FlushAllPending()
    {
        if (_sEngine.TryFlush(out var sOutput))
            SendKeymapOutput(sOutput);
        if (_kEngine.TryFlush(out var kOutput))
            SendKeymapOutput(kOutput);
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

            var before = CaptureDiagnosticState();
            var mode = _timerMode.Value;
            _timerMode = null;
            _timerDueAt = 0;
            if (GetEngine(mode).TryFlush(out var output))
                SendKeymapOutput(output);
            _diagnostics.RecordMarker(
                Environment.TickCount64,
                InputDiagnosticKind.ChordTimeout,
                before,
                CaptureDiagnosticState(),
                detailCode: (int)mode);
            RecoverIfInvariantBroken(Environment.TickCount64);
        }
    }

    private InputDiagnosticState CaptureDiagnosticState()
    {
        var physical = _keyboardState.GetSummary();
        return new InputDiagnosticState(
            _layers.Layers.Modifiers,
            _layers.Layers.Count,
            _layers.Consumed,
            _heldLayerPresses.Count,
            physical.PressedCount,
            physical.Modifiers,
            _suppressedKeys.Count,
            _sEngine.State,
            _kEngine.State,
            _timerMode,
            _timerDueAt);
    }

    private void RecoverIfInvariantBroken(long timestampMs)
    {
        var violation = GetInputInvariantViolationCode();
        if (violation == 0)
            return;

        var before = CaptureDiagnosticState();
        ResetInputStateCore();
        _diagnostics.RecordMarker(
            timestampMs,
            InputDiagnosticKind.InvariantViolation,
            before,
            CaptureDiagnosticState(),
            violation);
    }

    private int GetInputInvariantViolationCode()
    {
        if ((_timerMode is null) != (_timerDueAt == 0))
            return 1;
        if (_sEngine.State == ChordEngineState.PendingSingle && _kEngine.State == ChordEngineState.PendingSingle)
            return 2;
        if (_timerMode == KeymapMode.S && _sEngine.State != ChordEngineState.PendingSingle)
            return 3;
        if (_timerMode == KeymapMode.K && _kEngine.State != ChordEngineState.PendingSingle)
            return 4;
        if (_timerMode is null &&
            (_sEngine.State == ChordEngineState.PendingSingle || _kEngine.State == ChordEngineState.PendingSingle))
            return 5;
        if (_heldLayerPresses.Count > 4)
            return 6;
        foreach (var virtualKey in _heldLayerPresses.Keys)
        {
            if (!IsLayerTrigger(virtualKey))
                return 7;
        }
        return 0;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}