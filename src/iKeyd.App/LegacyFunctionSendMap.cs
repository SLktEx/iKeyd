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
        string AMS);

    private static readonly ExpandedValues[] Expanded = BuildExpanded();

    public static bool TryResolve(KeyCode key, LayerState state, out string sendText)
    {
        var slot = ResolveSlot(state);
        if (slot < 0 || !IsDirectKey(key))
        {
            sendText = string.Empty;
            return false;
        }

        sendText = GetExpanded(key, slot);
        return true;
    }

    public static bool TryResolve(KeyCode key, string state, out string sendText)
    {
        var slot = ResolveSlot(state);
        if (slot < 0 || !IsDirectKey(key))
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
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    private static ExpandedValues[] BuildExpanded()
    {
        var result = new ExpandedValues[(int)KeyCode.At + 1];
        foreach (var key in DirectKeys())
        {
            if (!TryGetValues(key, out var values))
                continue;

            result[(int)key] = new ExpandedValues(
                values.M,
                values.MH,
                values.HM,
                values.MS,
                Prefix('^', values.M),
                Prefix('^', values.MH),
                Prefix('^', values.HM),
                Prefix('^', values.MS),
                Prefix('!', values.M),
                Prefix('!', values.MH),
                Prefix('!', values.HM),
                Prefix('!', values.MS));
        }
        return result;
    }

    private static string Prefix(char prefix, string value)
        => value.Length == 0 ? string.Empty : string.Concat(prefix, value);

    private static IEnumerable<KeyCode> DirectKeys()
    {
        for (var value = (int)KeyCode.A; value <= (int)KeyCode.At; value++)
        {
            var key = (KeyCode)value;
            if (IsDirectKey(key))
                yield return key;
        }
    }

    private static bool IsDirectKey(KeyCode key)
        => key is
            KeyCode.Q or KeyCode.W or KeyCode.U or KeyCode.I or KeyCode.O or KeyCode.P or
            KeyCode.At or KeyCode.A or KeyCode.S or KeyCode.D or KeyCode.J or KeyCode.K or
            KeyCode.L or KeyCode.SColon or KeyCode.Colon or KeyCode.Z or KeyCode.X or KeyCode.C or
            KeyCode.N or KeyCode.M or KeyCode.Comma or KeyCode.Dot or KeyCode.Slash or
            KeyCode.Digit5 or KeyCode.Digit6 or KeyCode.Digit7 or KeyCode.Digit8 or KeyCode.Digit9 or KeyCode.Digit0;
}
