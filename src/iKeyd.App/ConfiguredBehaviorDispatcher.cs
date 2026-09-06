using System.Globalization;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Runtime;

namespace iKeyd.App;

internal enum ConfiguredBehaviorDisposition
{
    PassThrough,
    Suppress,
    SuppressUntilKeyUp
}

internal sealed class ConfiguredBehaviorDispatcher
{
    private readonly KeyBehaviorProfile _profile;
    private readonly ConfiguredKeyBehaviorRuntime _runtime;
    private readonly IKeyboardOutput _keyboard;
    private readonly IDesktopBackend _desktop;
    private readonly DesktopActionService _desktopActions;
    private readonly IConfiguredHostActionSink _hostActions;
    private readonly List<KeyboardKey> _modifiers = new(4);

    public ConfiguredBehaviorDispatcher(
        KeyBehaviorProfile profile,
        IKeyboardOutput keyboard,
        IDesktopBackend desktop,
        IConfiguredHostActionSink hostActions)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _runtime = new ConfiguredKeyBehaviorRuntime(profile);
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _desktopActions = new DesktopActionService(desktop);
        _hostActions = hostActions ?? throw new ArgumentNullException(nameof(hostActions));
    }

    public bool Enabled => !_profile.IsEmpty;

    public ConfiguredBehaviorDisposition Handle(KeyboardEvent keyboardEvent, KeyId key)
    {
        if (!Enabled)
            return ConfiguredBehaviorDisposition.PassThrough;

        var result = keyboardEvent.Kind == KeyEventKind.Down
            ? _runtime.OnKeyDown(key, keyboardEvent.TimestampMs)
            : _runtime.OnKeyUp(key, keyboardEvent.TimestampMs);

        ApplyTransitions(result.Transitions);
        if (result.Consumed)
            return ConfiguredBehaviorDisposition.Suppress;

        if (keyboardEvent.Kind == KeyEventKind.Down && TryDispatchHeldKey(key, keyboardEvent.Key))
            return ConfiguredBehaviorDisposition.SuppressUntilKeyUp;

        return ConfiguredBehaviorDisposition.PassThrough;
    }

    public void Reset() => _runtime.Reset();

    private void ApplyTransitions(KeyBehaviorTransitionList transitions)
    {
        for (var index = 0; index < transitions.Count; index++)
        {
            var transition = transitions[index];
            if (transition.Kind == KeyBehaviorTransitionKind.Tap)
            {
                CollectModifiers();
                EmitOutputAction(transition.Action, applyModifiersToKey: true);
            }
        }
    }

    private bool TryDispatchHeldKey(KeyId keyId, KeyboardKey physicalKey)
    {
        KeyBehaviorAction? mapped = null;
        for (var index = _runtime.ActiveHoldCount - 1; index >= 0; index--)
        {
            var hold = _runtime.GetActiveHoldAt(index).Action;
            if (hold.Kind == KeyBehaviorActionKind.Layer && _profile.TryGetLayerAction(hold.Value, keyId, out var action))
            {
                mapped = action;
                break;
            }
        }

        CollectModifiers();
        if (mapped is { } mappedAction)
        {
            EmitOutputAction(mappedAction, applyModifiersToKey: true);
            return true;
        }
        if (_modifiers.Count == 0)
            return false;
        SendKeyWithModifiers(physicalKey);
        return true;
    }

    private void EmitOutputAction(KeyBehaviorAction action, bool applyModifiersToKey)
    {
        switch (action.Kind)
        {
            case KeyBehaviorActionKind.Key:
                if (!WindowsKeyMap.TryResolveNamedKey(action.Value, out var key))
                    throw new InvalidOperationException($"Configured behavior output key '{action.Value}' is not supported on Windows.");
                if (applyModifiersToKey) SendKeyWithModifiers(key); else _keyboard.SendKeyPress(key);
                return;
            case KeyBehaviorActionKind.Text:
                _keyboard.SendText(action.Value);
                return;
            case KeyBehaviorActionKind.MouseMove:
                ParseMouseMove(action.Value, out var deltaX, out var deltaY);
                _desktop.MovePointerBy(deltaX, deltaY);
                return;
            case KeyBehaviorActionKind.MouseClick:
                _desktop.Click(ParseMouseButton(action.Value));
                return;
            case KeyBehaviorActionKind.Scroll:
                _desktop.ScrollVertical(string.Equals(action.Value, "Up", StringComparison.OrdinalIgnoreCase) ? 120 : -120);
                return;
            case KeyBehaviorActionKind.Media:
                _desktop.SendMediaCommand(ParseMediaCommand(action.Value));
                return;
            case KeyBehaviorActionKind.Window:
                DispatchWindowAction(action.Value);
                return;
            case KeyBehaviorActionKind.Clipboard:
            case KeyBehaviorActionKind.Macro:
            case KeyBehaviorActionKind.Exec:
            case KeyBehaviorActionKind.Shell:
            case KeyBehaviorActionKind.Query:
                _hostActions.Post(action);
                return;
            default:
                throw new InvalidOperationException($"Configured output action '{action.Kind}:{action.Value}' cannot be emitted directly.");
        }
    }

    private void DispatchWindowAction(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "minimize": _desktopActions.MinimizeActive(); break;
            case "togglemaximize": _desktopActions.ToggleMaximizeActive(); break;
            case "lefthalf": _desktopActions.PlaceActive(DesktopPlacement.LeftHalf); break;
            case "righthalf": _desktopActions.PlaceActive(DesktopPlacement.RightHalf); break;
            case "tophalf": _desktopActions.PlaceActive(DesktopPlacement.TopHalf); break;
            case "bottomhalf": _desktopActions.PlaceActive(DesktopPlacement.BottomHalf); break;
            case "toggletopmost": _desktopActions.ToggleTopMostActive(); break;
            case "opacityup": _desktopActions.AdjustOpacityActive(30); break;
            case "opacitydown": _desktopActions.AdjustOpacityActive(-30); break;
            case "togglecaption": _desktopActions.ToggleCaptionActive(); break;
            case "activatebottomsameclass": _desktopActions.ActivateBottomWindowOfActiveClass(); break;
            default: throw new InvalidOperationException($"Configured window action '{command}' is not supported.");
        }
    }

    private static void ParseMouseMove(string value, out int deltaX, out int deltaY)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out deltaX) || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out deltaY))
            throw new InvalidOperationException($"Invalid configured mouse movement '{value}'.");
    }

    private static DesktopMouseButton ParseMouseButton(string value) => value.ToLowerInvariant() switch
    {
        "left" => DesktopMouseButton.Left,
        "right" => DesktopMouseButton.Right,
        "middle" => DesktopMouseButton.Middle,
        _ => throw new InvalidOperationException($"Configured mouse button '{value}' is not supported.")
    };

    private static DesktopMediaCommand ParseMediaCommand(string value) => value.ToLowerInvariant() switch
    {
        "volumeup" => DesktopMediaCommand.VolumeUp,
        "volumemute" => DesktopMediaCommand.VolumeMute,
        "volumedown" => DesktopMediaCommand.VolumeDown,
        "nexttrack" => DesktopMediaCommand.NextTrack,
        "playpause" => DesktopMediaCommand.PlayPause,
        "previoustrack" => DesktopMediaCommand.PreviousTrack,
        _ => throw new InvalidOperationException($"Configured media command '{value}' is not supported.")
    };

    private void CollectModifiers()
    {
        _modifiers.Clear();
        var control = false;
        var shift = false;
        var alt = false;
        var gui = false;
        for (var index = 0; index < _runtime.ActiveHoldCount; index++)
        {
            var action = _runtime.GetActiveHoldAt(index).Action;
            if (action.Kind != KeyBehaviorActionKind.Modifier) continue;
            switch (action.GetModifier())
            {
                case KeyBehaviorModifier.Control: control = true; break;
                case KeyBehaviorModifier.Shift: shift = true; break;
                case KeyBehaviorModifier.Alt: alt = true; break;
                case KeyBehaviorModifier.Gui: gui = true; break;
            }
        }
        if (control) _modifiers.Add(WindowsKeyMap.Keyboard(WindowsKeyMap.LeftControl));
        if (shift) _modifiers.Add(WindowsKeyMap.Keyboard(WindowsKeyMap.LeftShift));
        if (alt) _modifiers.Add(WindowsKeyMap.Keyboard(WindowsKeyMap.LeftAlt));
        if (gui) _modifiers.Add(WindowsKeyMap.Keyboard(WindowsKeyMap.LeftWin));
    }

    private void SendKeyWithModifiers(KeyboardKey key)
    {
        foreach (var modifier in _modifiers)
            _keyboard.SendKey(modifier, KeyEventKind.Down);
        try { _keyboard.SendKeyPress(key); }
        finally
        {
            for (var index = _modifiers.Count - 1; index >= 0; index--)
                _keyboard.SendKey(_modifiers[index], KeyEventKind.Up);
        }
    }
}
