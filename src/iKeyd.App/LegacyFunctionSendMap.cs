using iKeyd.Core.Chords;
using iKeyd.Profiles.HotkeySkg.Layers;

namespace iKeyd.App;

/// <summary>
/// Exact resolved arguments passed to hotkeySKG's withFuncKey(mkey,mhkey,hmkey,mskey).
/// Values come from the pinned-source #56 Send reachability inventory, not from
/// reinterpreting AHK string literals at runtime.
/// </summary>
internal static class LegacyFunctionSendMap
{
    internal readonly record struct Values(string M, string MH, string HM, string MS);

    public static bool TryResolve(KeyCode key, LayerState state, out string sendText)
    {
        var slot = state.IsExact(LayerKey.M) ? 0
            : state.IsExact(LayerKey.M, LayerKey.H) ? 1
            : state.IsExact(LayerKey.H, LayerKey.M) ? 2
            : state.IsExact(LayerKey.M, LayerKey.S) ? 3
            : -1;

        if (slot < 0 || !TryGetValues(key, out var values))
        {
            sendText = string.Empty;
            return false;
        }

        sendText = slot switch
        {
            0 => values.M,
            1 => values.MH,
            2 => values.HM,
            3 => values.MS,
            _ => throw new UnreachableException()
        };
        return true;
    }

    public static bool TryResolve(KeyCode key, string state, out string sendText)
    {
        var slot = state switch
        {
            "M" => 0,
            "MH" => 1,
            "HM" => 2,
            "MS" => 3,
            _ => -1
        };

        if (slot < 0 || !TryGetValues(key, out var values))
        {
            sendText = string.Empty;
            return false;
        }

        sendText = slot switch
        {
            0 => values.M,
            1 => values.MH,
            2 => values.HM,
            3 => values.MS,
            _ => throw new UnreachableException()
        };
        return true;
    }

    internal static bool TryGetValues(KeyCode key, out Values values)
    {
        values = key switch
        {
            KeyCode.Q => new("(", "\"", "'", ""),
            KeyCode.W => new("!{F4}", "^{F4}", "", ""),
            KeyCode.U => new("{HOME}", "+{HOME}", "^{HOME}", "^+{HOME}"),
            KeyCode.I => new("{UP}", "+{UP}", "^{UP}", "^+{UP}"),
            KeyCode.O => new("{END}", "+{END}", "^{END}", "^+{END}"),
            KeyCode.P => new("{PGUP}", "+{PGUP}", "^{PGUP}", "^+{PGUP}"),
            KeyCode.At => new("{ESC}", "{AppsKey}", "!{Space}", ""),
            KeyCode.A => new("[]{LEFT}", "{{}", "{{}{ENTER}{ENTER}{}}{UP}{END}", ""),
            KeyCode.S => new("(){LEFT}", "{}}", "{{}{}}{LEFT}", ""),
            KeyCode.D => new("-", "=", "%", "~"),
            KeyCode.J => new("{LEFT}", "+{LEFT}", "^{LEFT}", "^+{LEFT}"),
            KeyCode.K => new("{DOWN}", "+{DOWN}", "^{DOWN}", "^+{DOWN}"),
            KeyCode.L => new("{RIGHT}", "+{RIGHT}", "^{RIGHT}", "^+{RIGHT}"),
            KeyCode.SColon => new("{PGDN}", "+{PGDN}", "^{PGDN}", "^+{PGDN}"),
            KeyCode.Colon => new("$", "{#}", "&", "{^}"),
            KeyCode.Z => new("\\", "/", "|", ""),
            KeyCode.X => new("\"\"{LEFT}", "''{LEFT}", "%%{LEFT}", ""),
            KeyCode.C => new("_", ">", "<", ""),
            KeyCode.N => new("{BS}", "!{RIGHT}", "!{LEFT}", "^+n"),
            KeyCode.M => new("{DEL}", "{END}{SHIFT DOWN}{HOME}{LEFT}{SHIFT UP}", "{HOME}+{END}", "#m"),
            KeyCode.Comma => new("{SPACE}", "{TAB}", "{ENTER}", ""),
            KeyCode.Dot => new(")", "<>{LEFT}", "</>{LEFT}", ""),
            KeyCode.Slash => new("{!}", "/*  */{LEFT 3}", "{END}+{HOME}^x\\begin{{}^v{}}{ENTER 2}\\end{{}^v{}}{UP}", ""),
            KeyCode.Digit5 => new("", "", "", ""),
            KeyCode.Digit6 => new("", "", "", ""),
            KeyCode.Digit7 => new("", "", "", ""),
            KeyCode.Digit8 => new("", "", "", ""),
            KeyCode.Digit9 => new("", "", "", ""),
            KeyCode.Digit0 => new("", "", "", ""),
            _ => default
        };

        return key is
            KeyCode.Q or KeyCode.W or KeyCode.U or KeyCode.I or KeyCode.O or KeyCode.P or
            KeyCode.At or KeyCode.A or KeyCode.S or KeyCode.D or KeyCode.J or KeyCode.K or
            KeyCode.L or KeyCode.SColon or KeyCode.Colon or KeyCode.Z or KeyCode.X or KeyCode.C or
            KeyCode.N or KeyCode.M or KeyCode.Comma or KeyCode.Dot or KeyCode.Slash or
            KeyCode.Digit5 or KeyCode.Digit6 or KeyCode.Digit7 or KeyCode.Digit8 or KeyCode.Digit9 or KeyCode.Digit0;
    }
}
