namespace iKeyd.Core.Input;

public interface IInputMethod
{
    bool IsKanaInputActive();
}

/// <summary>
/// Optional richer IME state used only when behavior depends on whether the
/// focused application currently owns an unconfirmed composition string.
/// IME-open/kana mode alone is not sufficient for that distinction.
/// </summary>
public interface IInputCompositionState
{
    bool IsCompositionActive();
}
