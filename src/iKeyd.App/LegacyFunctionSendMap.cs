using iKeyd.Core.Chords;
using iKeyd.Profiles.HotkeySkg.Layers;

namespace iKeyd.App;

/// <summary>
/// Exact resolved arguments passed to hotkeySKG's withFuncKey(mkey,mhkey,hmkey,mskey)
/// plus the legacy SHKey_* table used by the Space -> Convert number/function layer.
/// Values come from the pinned legacy source rather than being inferred from runtime output.
/// </summary>
internal static class LegacyFunctionSendMap
{
    internal readonly record struct Values(string M, string MH, string HM, string MS);

    private readonly record struct ExpandedValues(
        string M,
        string MH,
        string HM,
        string MS,
        string KM,
        string KMH,
        string KHM,
        string KMS,
        string AM,
        string AMH,
        string AHM,
        string AMS,
        string SH,
        string KSH,
        string ASH);

    private static readonly ExpandedValues[] Expanded = BuildExpanded();

    public static bool TryResolve(KeyCode key, LayerState state, out string sendText)
        => TryResolveSlot(key, ResolveSlot(state), out sendText);

    public static bool TryResolve(KeyCode key, string state, out string sendText)
        => TryResolveSlot(key, ResolveSlot(state), out sendText);

    private static bool TryResolveSlot(KeyCode key, int slot, out string sendText)
    {
        var isSupported = slot switch
        {
            >= 0 and <= 11 => IsDirectKey(key),
            >= 12 and <= 14 => IsShiftNumberKey(key),
            _ => false
        };

        if (!isSupported)
        {
            sendText = string.Empty;
            return false;
        }

        sendText = GetExpanded(key, slot);
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

        return IsDirectKey(key);
    }

    internal static bool TryGetShiftNumberValue(KeyCode key, out string value)
    {
        value = key switch
        {
            KeyCode.Q => "#1",
            KeyCode.W => "#2",
            KeyCode.E => "#3",
            KeyCode.R => "#4",
            KeyCode.T => "#5",
            KeyCode.Y => "#6",
            KeyCode.U => "#7",
            KeyCode.I => "#8",
            KeyCode.O => "#9",
            KeyCode.P => "#0",
            KeyCode.At => "{F11}",
            KeyCode.A => "1",
            KeyCode.S => "2",
            KeyCode.D => "3",
            KeyCode.F => "4",
            KeyCode.G => "5",
            KeyCode.H => "6",
            KeyCode.J => "7",
            KeyCode.K => "8",
            KeyCode.L => "9",
            KeyCode.SColon => "0",
            KeyCode.Colon => "{F12}",
            KeyCode.Z => "{F1}",
            KeyCode.X => "{F2}",
            KeyCode.C => "{F3}",
            KeyCode.V => "{F4}",
            KeyCode.B => "{F5}",
            KeyCode.N => "{F6}",
            KeyCode.M => "{F7}",
            KeyCode.Comma => "{F8}",
            KeyCode.Dot => "{F9}",
            KeyCode.Slash => "{F10}",
            KeyCode.Digit1 => "{F1}",
            KeyCode.Digit2 => "{F2}",
            KeyCode.Digit3 => "{F3}",
            KeyCode.Digit4 => "{F4}",
            KeyCode.Digit5 => "{F5}",
            KeyCode.Digit6 => "{F6}",
            KeyCode.Digit7 => "{F7}",
            KeyCode.Digit8 => "{F8}",
            KeyCode.Digit9 => "{F9}",
            KeyCode.Digit0 => "{F10}",
            _ => string.Empty
        };

        return IsShiftNumberKey(key);
    }

    private static int ResolveSlot(LayerState state)
        => state.IsExact(LayerKey.M) ? 0
            : state.IsExact(LayerKey.M, LayerKey.H) ? 1
            : state.IsExact(LayerKey.H, LayerKey.M) ? 2
            : state.IsExact(LayerKey.M, LayerKey.S) ? 3
            : state.IsExact(LayerKey.K, LayerKey.M) ? 4
            : state.IsExact(LayerKey.K, LayerKey.M, LayerKey.H) ? 5
            : state.IsExact(LayerKey.K, LayerKey.H, LayerKey.M) ? 6
            : state.IsExact(LayerKey.K, LayerKey.M, LayerKey.S) ? 7
            : state.IsExact(LayerKey.A, LayerKey.M) ? 8
            : state.IsExact(LayerKey.A, LayerKey.M, LayerKey.H) ? 9
            : state.IsExact(LayerKey.A, LayerKey.H, LayerKey.M) ? 10
            : state.IsExact(LayerKey.A, LayerKey.M, LayerKey.S) ? 11
            : state.IsExact(LayerKey.S, LayerKey.H) ? 12
            : state.IsExact(LayerKey.K, LayerKey.S, LayerKey.H) ? 13
            : state.IsExact(LayerKey.A, LayerKey.S, LayerKey.H) ? 14
            : -1;

    private static int ResolveSlot(string state)
        => state switch
        {
            "M" => 0,
            "MH" => 1,
            "HM" => 2,
            "MS" => 3,
            "KM" => 4,
            "KMH" => 5,
            "KHM" => 6,
            "KMS" => 7,
            "AM" => 8,
            "AMH" => 9,
            "AHM" => 10,
            "AMS" => 11,
            "SH" => 12,
            "KSH" => 13,
            "ASH" => 14,
            _ => -1
        };

    private static string GetExpanded(KeyCode key, int slot)
    {
        var values = Expanded[(int)key];
        return slot switch
        {
            0 => values.M,
            1 => values.MH,
            2 => values.HM,
            3 => values.MS,
            4 => values.KM,
            5 => values.KMH,
            6 => values.KHM,
            7 => values.KMS,
            8 => values.AM,
            9 => values.AMH,
            10 => values.AHM,
            11 => values.AMS,
            12 => values.SH,
            13 => values.KSH,
            14 => values.ASH,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    private static ExpandedValues[] BuildExpanded()
    {
        var result = new ExpandedValues[(int)KeyCode.At + 1];
        for (var raw = (int)KeyCode.A; raw <= (int)KeyCode.At; raw++)
        {
            var key = (KeyCode)raw;
            var hasDirect = TryGetValues(key, out var values);
            var hasShiftNumber = TryGetShiftNumberValue(key, out var shiftNumber);
            if (!hasDirect && !hasShiftNumber)
                continue;

            result[(int)key] = new ExpandedValues(
                hasDirect ? values.M : string.Empty,
                hasDirect ? values.MH : string.Empty,
                hasDirect ? values.HM : string.Empty,
                hasDirect ? values.MS : string.Empty,
                hasDirect ? Prefix('^', values.M) : string.Empty,
                hasDirect ? Prefix('^', values.MH) : string.Empty,
                hasDirect ? Prefix('^', values.HM) : string.Empty,
                hasDirect ? Prefix('^', values.MS) : string.Empty,
                hasDirect ? Prefix('!', values.M) : string.Empty,
                hasDirect ? Prefix('!', values.MH) : string.Empty,
                hasDirect ? Prefix('!', values.HM) : string.Empty,
                hasDirect ? Prefix('!', values.MS) : string.Empty,
                hasShiftNumber ? shiftNumber : string.Empty,
                hasShiftNumber ? Prefix('^', shiftNumber) : string.Empty,
                hasShiftNumber ? Prefix('!', shiftNumber) : string.Empty);
        }
        return result;
    }

    private static string Prefix(char prefix, string value)
        => value.Length == 0 ? string.Empty : string.Concat(prefix, value);

    private static bool IsDirectKey(KeyCode key)
        => key is
            KeyCode.Q or KeyCode.W or KeyCode.U or KeyCode.I or KeyCode.O or KeyCode.P or
            KeyCode.At or KeyCode.A or KeyCode.S or KeyCode.D or KeyCode.J or KeyCode.K or
            KeyCode.L or KeyCode.SColon or KeyCode.Colon or KeyCode.Z or KeyCode.X or KeyCode.C or
            KeyCode.N or KeyCode.M or KeyCode.Comma or KeyCode.Dot or KeyCode.Slash or
            KeyCode.Digit5 or KeyCode.Digit6 or KeyCode.Digit7 or KeyCode.Digit8 or KeyCode.Digit9 or KeyCode.Digit0;

    private static bool IsShiftNumberKey(KeyCode key)
        => key is
            KeyCode.Q or KeyCode.W or KeyCode.E or KeyCode.R or KeyCode.T or
            KeyCode.Y or KeyCode.U or KeyCode.I or KeyCode.O or KeyCode.P or KeyCode.At or
            KeyCode.A or KeyCode.S or KeyCode.D or KeyCode.F or KeyCode.G or
            KeyCode.H or KeyCode.J or KeyCode.K or KeyCode.L or KeyCode.SColon or KeyCode.Colon or
            KeyCode.Z or KeyCode.X or KeyCode.C or KeyCode.V or KeyCode.B or
            KeyCode.N or KeyCode.M or KeyCode.Comma or KeyCode.Dot or KeyCode.Slash or
            KeyCode.Digit1 or KeyCode.Digit2 or KeyCode.Digit3 or KeyCode.Digit4 or KeyCode.Digit5 or
            KeyCode.Digit6 or KeyCode.Digit7 or KeyCode.Digit8 or KeyCode.Digit9 or KeyCode.Digit0;
}
