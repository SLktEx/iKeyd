using iKeyd.Core.Input;

namespace iKeyd.Profiles.HotkeySkg.Modes;

public enum InputMode
{
    S,
    R,
    T,
    K
}

public enum KeymapMode
{
    S,
    K
}

public enum InputRouteKind
{
    PassThrough,
    ChordEngine
}

public readonly record struct InputRoute(InputRouteKind Kind, KeymapMode? Keymap);

public readonly record struct InputModeState(InputMode Mode, KeymapMode? ActiveKeymap)
{
    public static InputModeState Initial => new(InputMode.S, KeymapMode.S);

    public InputModeState SwitchTo(InputMode mode) => mode switch
    {
        InputMode.S => new(InputMode.S, KeymapMode.S),
        InputMode.R => new(InputMode.R, null),
        InputMode.T => new(InputMode.T, ActiveKeymap),
        InputMode.K => new(InputMode.K, KeymapMode.K),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public InputRoute Route(IInputMethod inputMethod)
    {
        ArgumentNullException.ThrowIfNull(inputMethod);
        return Route(inputMethod.IsKanaInputActive());
    }

    public InputRoute Route(bool kanaInputActive)
    {
        var useChordEngine = Mode == InputMode.T ||
            ((Mode == InputMode.S || Mode == InputMode.K) && kanaInputActive);

        return useChordEngine
            ? new InputRoute(InputRouteKind.ChordEngine, ActiveKeymap)
            : new InputRoute(InputRouteKind.PassThrough, null);
    }
}
