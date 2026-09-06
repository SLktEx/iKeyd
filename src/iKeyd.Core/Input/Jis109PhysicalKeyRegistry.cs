using iKeyd.Core.Chords;

namespace iKeyd.Core.Input;

/// <summary>
/// Target-neutral physical surface of a Japanese 109-key keyboard.
/// The registry deliberately identifies positions by <see cref="KeyCode"/>;
/// characters produced by a keyboard layout are a separate concern.
/// </summary>
public static class Jis109PhysicalKeyRegistry
{
    private static readonly Jis109PhysicalKey[] Registry = Build();

    public static IReadOnlyList<Jis109PhysicalKey> Keys => Registry;

    private static Jis109PhysicalKey[] Build()
    {
        var result = new List<Jis109PhysicalKey>(109);

        AddRow(result, "function", [
            KeyCode.Escape,
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4,
            KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8,
            KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12,
            KeyCode.PrintScreen, KeyCode.ScrollLock, KeyCode.Pause
        ]);

        AddRow(result, "number", [
            KeyCode.HankakuZenkaku,
            KeyCode.Digit1, KeyCode.Digit2, KeyCode.Digit3, KeyCode.Digit4, KeyCode.Digit5,
            KeyCode.Digit6, KeyCode.Digit7, KeyCode.Digit8, KeyCode.Digit9, KeyCode.Digit0,
            KeyCode.Minus, KeyCode.Caret, KeyCode.Yen, KeyCode.Backspace
        ]);

        AddRow(result, "q", [
            KeyCode.Tab,
            KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T,
            KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P,
            KeyCode.At, KeyCode.LBracket, KeyCode.Enter
        ]);

        AddRow(result, "a", [
            KeyCode.CapsLock,
            KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G,
            KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L,
            KeyCode.SColon, KeyCode.Colon, KeyCode.RBracket
        ]);

        AddRow(result, "z", [
            KeyCode.LeftShift,
            KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B,
            KeyCode.N, KeyCode.M, KeyCode.Comma, KeyCode.Dot, KeyCode.Slash, KeyCode.Ro,
            KeyCode.RightShift
        ]);

        AddRow(result, "bottom", [
            KeyCode.LeftCtrl, KeyCode.LeftWin, KeyCode.LeftAlt, KeyCode.NonConvert,
            KeyCode.Space, KeyCode.Convert, KeyCode.Kana,
            KeyCode.RightAlt, KeyCode.RightWin, KeyCode.Apps, KeyCode.RightCtrl
        ]);

        AddRow(result, "edit", [
            KeyCode.Insert, KeyCode.Home, KeyCode.PageUp,
            KeyCode.Delete, KeyCode.End, KeyCode.PageDown
        ]);

        AddRow(result, "arrows", [
            KeyCode.Up, KeyCode.Left, KeyCode.Down, KeyCode.Right
        ]);

        AddRow(result, "numpad", [
            KeyCode.NumLock, KeyCode.NumpadDivide, KeyCode.NumpadMultiply, KeyCode.NumpadSubtract,
            KeyCode.Numpad7, KeyCode.Numpad8, KeyCode.Numpad9, KeyCode.NumpadAdd,
            KeyCode.Numpad4, KeyCode.Numpad5, KeyCode.Numpad6,
            KeyCode.Numpad1, KeyCode.Numpad2, KeyCode.Numpad3, KeyCode.NumpadEnter,
            KeyCode.Numpad0, KeyCode.NumpadDecimal
        ]);

        if (result.Count != 109)
            throw new InvalidOperationException($"JIS109 registry must contain exactly 109 physical keys, got {result.Count}.");
        if (result.Select(item => item.Code).Distinct().Count() != result.Count)
            throw new InvalidOperationException("JIS109 registry contains a duplicate physical key identity.");

        return result.ToArray();
    }

    private static void AddRow(List<Jis109PhysicalKey> target, string row, KeyCode[] keys)
    {
        for (var index = 0; index < keys.Length; index++)
            target.Add(new Jis109PhysicalKey(keys[index], row, index + 1));
    }
}

public readonly record struct Jis109PhysicalKey(KeyCode Code, string Row, int Column)
{
    public string Name => new KeyId(Code).Value;
}
