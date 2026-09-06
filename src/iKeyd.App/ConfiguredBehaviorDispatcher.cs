using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Core.Runtime;

namespace iKeyd.App;

internal enum ConfiguredBehaviorDisposition
{
    PassThrough,
    Suppress,
    SuppressUntilKeyUp
}

/// <summary>
/// Windows projection of the platform-neutral configurable behavior runtime.
/// Legacy hotkeySKG layer behavior remains outside this class and is reached only
/// when no configured behavior consumes or remaps the event.
/// </summary>
internal sealed class ConfiguredBehaviorDispatcher
{
    private readonly KeyBehaviorProfile _profile;
    private readonly ConfiguredKeyBehaviorRuntime _runtime;
    private readonly LegacySendOutput _send;
    private readonly List<ushort> _modifiers = new(4);

    public ConfiguredBehaviorDispatcher(KeyBehaviorProfile profile, LegacySendOutput send)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _runtime = new ConfiguredKeyBehaviorRuntime(profile);
        _send = send ?? throw new ArgumentNullException(nameof(send));
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
                SendTap(transition.Action);
        }
    }

    private void SendTap(KeyBehaviorAction action)
    {
        switch (action.Kind)
        {
            case KeyBehaviorActionKind.Key:
                if (!WindowsKeyMap.TryResolveNamedKey(action.Value, out var key))
                    throw new InvalidOperationException($"Configured behavior output key '{action.Value}' is not supported on Windows.");
                _send.SendKey(key.VirtualKey);
                break;
            case KeyBehaviorActionKind.Text:
                _send.Send(action.Value);
                break;
            default:
                throw new InvalidOperationException($"Configured tap action '{action.Kind}' cannot be emitted directly.");
        }
    }

    private bool TryDispatchHeldKey(KeyId keyId, KeyboardKey physicalKey)
    {
        KeyBehaviorAction? mapped = null;
        for (var index = _runtime.ActiveHoldCount - 1; index >= 0; index--)
        {
            var hold = _runtime.GetActiveHoldAt(index).Action;
            if (hold.Kind == KeyBehaviorActionKind.Layer &&
                _profile.TryGetLayerAction(hold.Value, keyId, out var action))
            {
                mapped = action;
                break;
            }
        }

        CollectModifiers();
        if (mapped is { } mappedAction)
        {
            if (mappedAction.Kind == KeyBehaviorActionKind.Text)
            {
                // text(...) is literal output by design; virtual held modifiers do
                // not transform its characters.
                _send.Send(mappedAction.Value);
                return true;
            }

            if (mappedAction.Kind != KeyBehaviorActionKind.Key ||
                !WindowsKeyMap.TryResolveNamedKey(mappedAction.Value, out var mappedKey))
            {
                throw new InvalidOperationException($"Configured layer output '{mappedAction.Kind}:{mappedAction.Value}' is not supported on Windows.");
            }

            SendKeyWithModifiers(mappedKey.VirtualKey);
            return true;
        }

        if (_modifiers.Count == 0)
            return false;

        SendKeyWithModifiers(physicalKey.VirtualKey);
        return true;
    }

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
            if (action.Kind != KeyBehaviorActionKind.Modifier)
                continue;

            switch (action.GetModifier())
            {
                case KeyBehaviorModifier.Control: control = true; break;
                case KeyBehaviorModifier.Shift: shift = true; break;
                case KeyBehaviorModifier.Alt: alt = true; break;
                case KeyBehaviorModifier.Gui: gui = true; break;
            }
        }

        if (control) _modifiers.Add(WindowsKeyMap.Control);
        if (shift) _modifiers.Add(WindowsKeyMap.Shift);
        if (alt) _modifiers.Add(WindowsKeyMap.Alt);
        if (gui) _modifiers.Add(WindowsKeyMap.LeftWin);
    }

    private void SendKeyWithModifiers(ushort virtualKey)
    {
        switch (_modifiers.Count)
        {
            case 0:
                _send.SendKey(virtualKey);
                break;
            case 1:
                _send.SendChord(_modifiers[0], virtualKey);
                break;
            case 2:
                _send.SendChord(_modifiers[0], _modifiers[1], virtualKey);
                break;
            default:
                _send.SendChord(_modifiers, virtualKey);
                break;
        }
    }
}
