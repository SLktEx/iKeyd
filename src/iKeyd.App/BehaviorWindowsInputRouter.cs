using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using iKeyd.Core.State;

namespace iKeyd.App;

/// <summary>
/// Opt-in Windows bridge for first-class behaviors. Existing input continues to
/// the legacy runtime unless a configured behavior or an active named behavior
/// layer consumes the event.
/// </summary>
internal sealed class BehaviorWindowsInputRouter : IKeyboardEventHandler, IInputStateResettable, IDisposable
{
    private readonly object _gate = new();
    private readonly AutomationProfile _profile;
    private readonly Func<string?> _baseKeymapName;
    private readonly LegacySendOutput _send;
    private readonly IKeyboardOutput _keyboard;
    private readonly IKeyboardEventHandler _fallback;
    private readonly Action<BehaviorAction>? _postHostAction;
    private readonly IRuntimeStateStore _runtimeState;
    private readonly Dictionary<string, BehaviorRuntime> _behaviorRuntimes;
    private readonly Dictionary<string, Keymap<string>> _keymaps;
    private readonly List<string> _activeLayers = [];
    private readonly List<string> _persistentLayers = [];
    private readonly HashSet<KeyId> _activeBehaviorKeys = [];
    private readonly HashSet<KeyboardKey> _layerMappedKeys = [];
    private string? _armedOneShotLayer;
    private string? _consumedOneShotLayer;
    private KeyboardKey? _oneShotConsumedKey;
    private KeyboardKey? _armedOneShotModifier;
    private KeyboardKey? _consumedOneShotModifier;
    private KeyboardKey? _oneShotModifierConsumedKey;
    private bool _disposed;

    public BehaviorWindowsInputRouter(
        AutomationProfile profile,
        Func<string?> baseKeymapName,
        LegacySendOutput send,
        IKeyboardOutput keyboard,
        IKeyboardEventHandler fallback,
        Action<BehaviorAction>? postHostAction = null,
        ISystemQuerySnapshot? systemQueries = null,
        IRuntimeStateStore? runtimeState = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _baseKeymapName = baseKeymapName ?? throw new ArgumentNullException(nameof(baseKeymapName));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _postHostAction = postHostAction;
        systemQueries ??= EmptySystemQuerySnapshot.Instance;
        _runtimeState = runtimeState ?? (profile.State.Count == 0
            ? EmptyRuntimeStateStore.Instance
            : new RuntimeStateStore(profile.State));

        _keymaps = new Dictionary<string, Keymap<string>>(StringComparer.OrdinalIgnoreCase);
        _behaviorRuntimes = new Dictionary<string, BehaviorRuntime>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in profile.Keymaps)
        {
            _keymaps.Add(pair.Key, pair.Value.BuildKeymap());
            if (pair.Value.BehaviorMappings.Count != 0)
            {
                _behaviorRuntimes.Add(
                    pair.Key,
                    new BehaviorRuntime(pair.Value.BuildBehaviorBindings(
                        profile.BehaviorDefinitions,
                        systemQueries,
                        profile.State,
                        _runtimeState)));
            }
        }

        ValidateLayerTargets();
    }

    public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
    {
        if (_disposed || keyboardEvent.Origin != KeyEventOrigin.Physical || _behaviorRuntimes.Count == 0)
            return _fallback.OnKeyboardEvent(keyboardEvent);

        var keyId = WindowsKeyMap.TryResolveKeyId(keyboardEvent.Key);
        if (keyId is null)
            return _fallback.OnKeyboardEvent(keyboardEvent);

        lock (_gate)
        {
            return keyboardEvent.Kind switch
            {
                KeyEventKind.Down => HandleKeyDown(keyboardEvent, keyId.Value),
                KeyEventKind.Up => HandleKeyUp(keyboardEvent, keyId.Value),
                _ => _fallback.OnKeyboardEvent(keyboardEvent)
            };
        }
    }

    public void ResetInputState()
    {
        lock (_gate)
        {
            if (!_disposed)
                ResetLocalState();
        }

        if (_fallback is IInputStateResettable resettable)
            resettable.ResetInputState();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            ResetLocalState();
            _disposed = true;
        }
    }

    private void ResetLocalState()
    {
        foreach (var runtime in _behaviorRuntimes.Values)
            ApplyActions(runtime.CancelAll());

        ReleaseConsumedOneShotModifier();
        _activeBehaviorKeys.Clear();
        _layerMappedKeys.Clear();
        _activeLayers.Clear();
        _persistentLayers.Clear();
        _armedOneShotLayer = null;
        _consumedOneShotLayer = null;
        _oneShotConsumedKey = null;
        _armedOneShotModifier = null;
        _runtimeState.Reset();
    }

    private KeyboardDisposition HandleKeyDown(KeyboardEvent keyboardEvent, KeyId keyId)
    {
        ConsumeArmedOneShot(keyboardEvent.Key);
        ConsumeArmedOneShotModifier(keyboardEvent.Key);

        foreach (var activeRuntime in _behaviorRuntimes.Values)
            ApplyActions(activeRuntime.ObserveKeyDown(keyId, keyboardEvent.TimestampMs).Actions);

        if (_activeBehaviorKeys.Contains(keyId))
        {
            foreach (var activeRuntime in _behaviorRuntimes.Values)
            {
                if (!activeRuntime.IsActive(keyId))
                    continue;

                ApplyActions(activeRuntime.BeginKeyDown(keyId, keyboardEvent.TimestampMs).Actions);
                break;
            }
            return KeyboardDisposition.Suppress;
        }

        if (TryHandleLayeredKeyDown(keyboardEvent, keyId))
            return KeyboardDisposition.Suppress;

        return _fallback.OnKeyboardEvent(keyboardEvent);
    }

    private void ConsumeArmedOneShot(KeyboardKey key)
    {
        if (_armedOneShotLayer is null || _oneShotConsumedKey is not null)
            return;

        _consumedOneShotLayer = _armedOneShotLayer;
        _armedOneShotLayer = null;
        _oneShotConsumedKey = key;
    }

    private void ConsumeArmedOneShotModifier(KeyboardKey key)
    {
        if (_armedOneShotModifier is not KeyboardKey modifier || _oneShotModifierConsumedKey is not null)
            return;

        _armedOneShotModifier = null;
        _consumedOneShotModifier = modifier;
        _oneShotModifierConsumedKey = key;
        _keyboard.SendKey(modifier, KeyEventKind.Down);
    }

    private bool TryHandleLayeredKeyDown(KeyboardEvent keyboardEvent, KeyId keyId)
    {
        // Layers are transparent overlays. Search the newest momentary activation
        // first, then the one-shot layer consumed by this physical key lifecycle,
        // then persistent selections and finally the base behavior map. Base
        // ordinary mappings intentionally remain in the legacy fallback so this
        // bridge does not bypass the existing chord/simultaneous-key engine.
        for (var index = _activeLayers.Count - 1; index >= 0; index--)
        {
            if (TryHandleKeymapKeyDown(_activeLayers[index], keyboardEvent, keyId, includeSingles: true))
                return true;
        }

        if (_consumedOneShotLayer is not null &&
            _oneShotConsumedKey is KeyboardKey consumedKey &&
            consumedKey == keyboardEvent.Key &&
            TryHandleKeymapKeyDown(_consumedOneShotLayer, keyboardEvent, keyId, includeSingles: true))
        {
            return true;
        }

        for (var index = _persistentLayers.Count - 1; index >= 0; index--)
        {
            if (TryHandleKeymapKeyDown(_persistentLayers[index], keyboardEvent, keyId, includeSingles: true))
                return true;
        }

        var baseKeymap = _baseKeymapName();
        return baseKeymap is not null &&
               TryHandleKeymapKeyDown(baseKeymap, keyboardEvent, keyId, includeSingles: false);
    }

    private bool TryHandleKeymapKeyDown(
        string keymapName,
        KeyboardEvent keyboardEvent,
        KeyId keyId,
        bool includeSingles)
    {
        if (_behaviorRuntimes.TryGetValue(keymapName, out var runtime) && runtime.IsBound(keyId))
        {
            var started = runtime.BeginKeyDown(keyId, keyboardEvent.TimestampMs);
            ApplyActions(started.Actions);
            if (started.Suppress)
            {
                _activeBehaviorKeys.Add(keyId);
                return true;
            }
        }

        if (includeSingles &&
            _keymaps.TryGetValue(keymapName, out var keymap) &&
            keymap.TryGetSingle(keyId, out var output))
        {
            _send.Send(output);
            _layerMappedKeys.Add(keyboardEvent.Key);
            return true;
        }

        return false;
    }

    private KeyboardDisposition HandleKeyUp(KeyboardEvent keyboardEvent, KeyId keyId)
    {
        var suppress = false;
        foreach (var runtime in _behaviorRuntimes.Values)
        {
            var result = runtime.OnKeyUp(keyId, keyboardEvent.TimestampMs);
            ApplyActions(result.Actions);
            suppress |= result.Suppress;
        }

        if (_activeBehaviorKeys.Remove(keyId))
            suppress = true;
        if (_layerMappedKeys.Remove(keyboardEvent.Key))
            suppress = true;

        if (_oneShotConsumedKey is KeyboardKey consumedKey && consumedKey == keyboardEvent.Key)
        {
            _oneShotConsumedKey = null;
            _consumedOneShotLayer = null;
        }

        var releaseOneShotModifier =
            _oneShotModifierConsumedKey is KeyboardKey modifierConsumedKey &&
            modifierConsumedKey == keyboardEvent.Key;

        try
        {
            return suppress
                ? KeyboardDisposition.Suppress
                : _fallback.OnKeyboardEvent(keyboardEvent);
        }
        finally
        {
            if (releaseOneShotModifier)
                ReleaseConsumedOneShotModifier();
        }
    }

    private void ReleaseConsumedOneShotModifier()
    {
        if (_consumedOneShotModifier is KeyboardKey modifier)
            _keyboard.SendKey(modifier, KeyEventKind.Up);

        _consumedOneShotModifier = null;
        _oneShotModifierConsumedKey = null;
    }

    private void ApplyActions(IReadOnlyList<BehaviorAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case BehaviorActionKind.SendKey:
                    if (!WindowsKeyMap.TryResolveOutputKey(action.Key, out var outputKey))
                        throw new InvalidOperationException($"Behavior output key '{action.Key}' is not supported by the Windows backend.");
                    _keyboard.SendKeyPress(outputKey);
                    break;

                case BehaviorActionKind.SendUnicode:
                case BehaviorActionKind.SendText:
                    if (action.Text is null)
                        throw new InvalidOperationException($"Behavior {action.Kind} action is missing its text payload.");
                    _keyboard.SendText(action.Text);
                    break;

                case BehaviorActionKind.LayerOn:
                    _activeLayers.Add(RequireKnownLayer(action.Name));
                    break;

                case BehaviorActionKind.LayerOff:
                    if (action.Name is not null)
                        RemoveLastLayer(action.Name);
                    break;

                case BehaviorActionKind.LayerToggle:
                    TogglePersistentLayer(RequireKnownLayer(action.Name));
                    break;

                case BehaviorActionKind.LayerSet:
                    _persistentLayers.Clear();
                    _persistentLayers.Add(RequireKnownLayer(action.Name));
                    break;

                case BehaviorActionKind.LayerOneShot:
                    _armedOneShotLayer = RequireKnownLayer(action.Name);
                    break;

                case BehaviorActionKind.ModifierDown:
                    _keyboard.SendKey(ResolveModifier(action.Name), KeyEventKind.Down);
                    break;

                case BehaviorActionKind.ModifierUp:
                    _keyboard.SendKey(ResolveModifier(action.Name), KeyEventKind.Up);
                    break;

                case BehaviorActionKind.ModifierOneShot:
                    _armedOneShotModifier = ResolveModifier(action.Name);
                    break;

                case BehaviorActionKind.StateSet:
                    if (action.Name is null || action.Text is null)
                        throw new InvalidOperationException("StateSet action is missing field/value data.");
                    _runtimeState.SetScalar(action.Name, action.Text);
                    break;

                case BehaviorActionKind.StateToggle:
                    if (action.Name is null)
                        throw new InvalidOperationException("StateToggle action is missing a field name.");
                    _runtimeState.Toggle(action.Name);
                    break;

                case BehaviorActionKind.Exec:
                case BehaviorActionKind.Shell:
                case BehaviorActionKind.Query:
                    if (_postHostAction is null)
                        throw new InvalidOperationException($"Behavior action '{action.Kind}' requires a host-action sink.");
                    _postHostAction(action);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown behavior action.");
            }
        }
    }

    private string RequireKnownLayer(string? layer)
    {
        if (layer is null || !_keymaps.ContainsKey(layer))
            throw new InvalidOperationException($"Behavior tried to activate unknown layer '{layer}'.");
        return layer;
    }

    private void TogglePersistentLayer(string layer)
    {
        for (var index = _persistentLayers.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(_persistentLayers[index], layer, StringComparison.OrdinalIgnoreCase))
                continue;
            _persistentLayers.RemoveAt(index);
            return;
        }

        _persistentLayers.Add(layer);
    }

    private void RemoveLastLayer(string layer)
    {
        for (var index = _activeLayers.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(_activeLayers[index], layer, StringComparison.OrdinalIgnoreCase))
                continue;
            _activeLayers.RemoveAt(index);
            return;
        }
    }

    private void ValidateLayerTargets()
    {
        foreach (var keymap in _profile.Keymaps.Values)
        {
            foreach (var mapping in keymap.BehaviorMappings)
            {
                var helper = mapping.Invocation.Name.ToUpperInvariant();
                if (helper is not ("LT" or "MO" or "TG" or "TO" or "OSL") ||
                    mapping.Invocation.Arguments.Count < 1)
                {
                    continue;
                }

                var layer = mapping.Invocation.Arguments[0];
                if (!_profile.Keymaps.ContainsKey(layer))
                {
                    throw new InvalidDataException(
                        $"{helper} on '{keymap.Name}.{mapping.Key}' references unknown layer '{layer}'.");
                }
            }
        }
    }

    private static KeyboardKey ResolveModifier(string? modifier)
    {
        if (modifier is null)
            throw new InvalidOperationException("Modifier action is missing a modifier name.");

        if (modifier.Equals("GUI", StringComparison.OrdinalIgnoreCase) ||
            modifier.Equals("WIN", StringComparison.OrdinalIgnoreCase) ||
            modifier.Equals("LWIN", StringComparison.OrdinalIgnoreCase))
        {
            if (WindowsKeyMap.TryResolveOutputKey(new KeyId(KeyCode.LeftWin), out var win))
                return win;
        }

        if (WindowsKeyMap.TryResolveNamedKey(modifier, out var key) &&
            key.VirtualKey is WindowsKeyMap.Control or WindowsKeyMap.Shift or WindowsKeyMap.Alt or
                WindowsKeyMap.LeftControl or WindowsKeyMap.RightControl or
                WindowsKeyMap.LeftShift or WindowsKeyMap.RightShift or
                WindowsKeyMap.LeftAlt or WindowsKeyMap.RightAlt)
        {
            return key;
        }

        throw new InvalidOperationException($"Unknown Windows modifier '{modifier}'.");
    }
}
