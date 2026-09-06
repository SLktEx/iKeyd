using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;

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
    private readonly Dictionary<string, BehaviorRuntime> _behaviorRuntimes;
    private readonly Dictionary<string, Keymap<string>> _keymaps;
    private readonly List<string> _activeLayers = [];
    private readonly HashSet<KeyId> _activeBehaviorKeys = [];
    private readonly HashSet<KeyboardKey> _layerMappedKeys = [];
    private bool _disposed;

    public BehaviorWindowsInputRouter(
        AutomationProfile profile,
        Func<string?> baseKeymapName,
        LegacySendOutput send,
        IKeyboardOutput keyboard,
        IKeyboardEventHandler fallback)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _baseKeymapName = baseKeymapName ?? throw new ArgumentNullException(nameof(baseKeymapName));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

        _keymaps = new Dictionary<string, Keymap<string>>(StringComparer.OrdinalIgnoreCase);
        _behaviorRuntimes = new Dictionary<string, BehaviorRuntime>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in profile.Keymaps)
        {
            _keymaps.Add(pair.Key, pair.Value.BuildKeymap());
            if (pair.Value.BehaviorMappings.Count != 0)
                _behaviorRuntimes.Add(pair.Key, new BehaviorRuntime(pair.Value.BuildBehaviorBindings(profile.BehaviorDefinitions)));
        }

        ValidateLayerTapTargets();
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

        _activeBehaviorKeys.Clear();
        _layerMappedKeys.Clear();
        _activeLayers.Clear();
    }

    private KeyboardDisposition HandleKeyDown(KeyboardEvent keyboardEvent, KeyId keyId)
    {
        // First let every already-active behavior observe this physical key. An LT
        // can resolve to hold here, which may change the active named layer before
        // this very same key is looked up.
        foreach (var activeRuntime in _behaviorRuntimes.Values)
            ApplyActions(activeRuntime.ObserveKeyDown(keyId, keyboardEvent.TimestampMs).Actions);

        // Auto-repeat belongs to the behavior instance that consumed the original
        // down even if that behavior has since activated a different layer. Route
        // the repeated down back to that owner so repeatable output actions can
        // react without replaying stateful layer/modifier transitions.
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

        var targetKeymap = ResolveTargetKeymapName();
        if (targetKeymap is not null &&
            _behaviorRuntimes.TryGetValue(targetKeymap, out var targetRuntime) &&
            targetRuntime.IsBound(keyId))
        {
            var started = targetRuntime.BeginKeyDown(keyId, keyboardEvent.TimestampMs);
            ApplyActions(started.Actions);
            if (started.Suppress)
            {
                _activeBehaviorKeys.Add(keyId);
                return KeyboardDisposition.Suppress;
            }
        }

        // Named layers activated by behaviors are intentionally evaluated before
        // the existing HotkeySKG state machine. Unmapped keys remain transparent.
        var activeLayer = ActiveLayer;
        if (activeLayer is not null &&
            _keymaps.TryGetValue(activeLayer, out var layerKeymap) &&
            layerKeymap.TryGetSingle(keyId, out var output))
        {
            _send.Send(output);
            _layerMappedKeys.Add(keyboardEvent.Key);
            return KeyboardDisposition.Suppress;
        }

        return _fallback.OnKeyboardEvent(keyboardEvent);
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

        return suppress
            ? KeyboardDisposition.Suppress
            : _fallback.OnKeyboardEvent(keyboardEvent);
    }

    private string? ResolveTargetKeymapName()
        => ActiveLayer ?? _baseKeymapName();

    private string? ActiveLayer
        => _activeLayers.Count == 0 ? null : _activeLayers[^1];

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
                    if (action.Name is null || !_keymaps.ContainsKey(action.Name))
                        throw new InvalidOperationException($"Behavior tried to activate unknown layer '{action.Name}'.");
                    _activeLayers.Add(action.Name);
                    break;

                case BehaviorActionKind.LayerOff:
                    if (action.Name is not null)
                        RemoveLastLayer(action.Name);
                    break;

                case BehaviorActionKind.ModifierDown:
                    _keyboard.SendKey(ResolveModifier(action.Name), KeyEventKind.Down);
                    break;

                case BehaviorActionKind.ModifierUp:
                    _keyboard.SendKey(ResolveModifier(action.Name), KeyEventKind.Up);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown behavior action.");
            }
        }
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

    private void ValidateLayerTapTargets()
    {
        foreach (var keymap in _profile.Keymaps.Values)
        {
            foreach (var mapping in keymap.BehaviorMappings)
            {
                if (!string.Equals(mapping.Invocation.Name, "LT", StringComparison.OrdinalIgnoreCase) ||
                    mapping.Invocation.Arguments.Count < 1)
                {
                    continue;
                }

                var layer = mapping.Invocation.Arguments[0];
                if (!_profile.Keymaps.ContainsKey(layer))
                    throw new InvalidDataException($"LT on '{keymap.Name}.{mapping.Key}' references unknown layer '{layer}'.");
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
