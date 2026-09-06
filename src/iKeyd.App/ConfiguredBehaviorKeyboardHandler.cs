using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;

namespace iKeyd.App;

internal sealed class ConfiguredBehaviorKeyboardHandler : IKeyboardEventHandler
{
    private readonly ConfiguredBehaviorDispatcher _configured;
    private readonly IKeyboardEventHandler _fallback;
    private readonly HashSet<KeyboardKey> _suppressedKeys = new(16);

    public ConfiguredBehaviorKeyboardHandler(
        KeyBehaviorProfile profile,
        LegacySendOutput send,
        IDesktopBackend desktop,
        IKeyboardEventHandler fallback)
    {
        _configured = new ConfiguredBehaviorDispatcher(profile, send, desktop);
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
    {
        if (!_configured.Enabled || keyboardEvent.Origin != KeyEventOrigin.Physical)
            return _fallback.OnKeyboardEvent(keyboardEvent);

        var keyId = WindowsKeyMap.TryResolveKeyId(keyboardEvent.Key);
        if (keyId is { } key)
        {
            var disposition = _configured.Handle(keyboardEvent, key);
            if (disposition == ConfiguredBehaviorDisposition.Suppress)
                return KeyboardDisposition.Suppress;
            if (disposition == ConfiguredBehaviorDisposition.SuppressUntilKeyUp)
            {
                _suppressedKeys.Add(keyboardEvent.Key);
                return KeyboardDisposition.Suppress;
            }
        }

        if (keyboardEvent.Kind == KeyEventKind.Up && _suppressedKeys.Remove(keyboardEvent.Key))
            return KeyboardDisposition.Suppress;

        return _fallback.OnKeyboardEvent(keyboardEvent);
    }
}
