using System.Globalization;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Converts the profile/IR representation of standard and user-defined behavior
/// invocations into executable behavior definitions. The event runtime remains
/// generic and does not branch on behavior names.
/// </summary>
public static class BehaviorDefinitionFactory
{
    private static readonly string[] TapHoldOptionNames =
    [
        "tapping_term",
        "hold_on_other_key_press"
    ];

    public static BehaviorDefinition Create(BehaviorInvocationProfile invocation)
        => Create(invocation, new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase));

    public static BehaviorDefinition Create(
        BehaviorInvocationProfile invocation,
        IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> userDefinitions)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(userDefinitions);

        if (string.Equals(invocation.Name, "LT", StringComparison.OrdinalIgnoreCase))
            return CreateLayerTap(invocation);
        if (string.Equals(invocation.Name, "MT", StringComparison.OrdinalIgnoreCase))
            return CreateModTap(invocation);

        if (TryCreatePcBehavior(invocation, out var standard))
            return standard;
        if (userDefinitions.TryGetValue(invocation.Name, out var userDefinition))
            return new ScriptedBehaviorDefinition(userDefinition, invocation);

        throw new NotSupportedException($"Unknown behavior '{invocation.Name}'.");
    }

    private static bool TryCreatePcBehavior(BehaviorInvocationProfile invocation, out BehaviorDefinition definition)
    {
        var name = invocation.Name.ToUpperInvariant();
        if (name is not ("MO" or "MOD" or "TEXT" or "MOUSE_MOVE" or "MOUSE_CLICK" or "SCROLL" or "MEDIA" or "WINDOW" or "CLIPBOARD" or "MACRO"))
        {
            definition = null!;
            return false;
        }

        definition = name switch
        {
            "MO" => CreateMomentaryLayer(invocation),
            "MOD" => CreateModifierHold(invocation),
            "TEXT" => CreateText(invocation),
            "MOUSE_MOVE" => CreateMouseMove(invocation),
            "MOUSE_CLICK" => CreateNamedPress(invocation, "mouse button", BehaviorAction.MouseClick, "Left", "Right", "Middle"),
            "SCROLL" => CreateScroll(invocation),
            "MEDIA" => CreateNamedPress(invocation, "media command", BehaviorAction.Media, "VolumeUp", "VolumeMute", "VolumeDown", "NextTrack", "PreviousTrack", "PlayPause"),
            "WINDOW" => CreateNamedPress(invocation, "window command", BehaviorAction.Window, "Minimize", "ToggleMaximize", "LeftHalf", "RightHalf", "TopHalf", "BottomHalf", "ToggleTopMost", "OpacityUp", "OpacityDown", "ToggleCaption", "ActivateBottomSameClass"),
            "CLIPBOARD" => CreateNamedPress(invocation, "clipboard command", BehaviorAction.Clipboard, "History", "Capture", "Paste"),
            "MACRO" => CreateMacro(invocation),
            _ => throw new InvalidOperationException($"Unsupported standard behavior '{invocation.Name}'.")
        };
        return true;
    }

    private static BehaviorDefinition CreateLayerTap(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count != 2)
            throw new InvalidDataException("LT requires exactly two arguments: LT(layer, tap_key).");

        ValidateKnownOptions(invocation, TapHoldOptionNames);
        var layer = invocation.Arguments[0];
        var tapKey = new KeyId(invocation.Arguments[1]);
        var options = new LayerTapOptions
        {
            TappingTermMs = ReadDurationMs(
                invocation,
                "tapping_term",
                LayerTapOptions.DefaultTappingTermMs),
            HoldOnOtherKeyPress = ReadBoolean(invocation, "hold_on_other_key_press", true)
        };
        return StandardBehaviors.LT(layer, tapKey, options);
    }

    private static BehaviorDefinition CreateModTap(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count != 2)
            throw new InvalidDataException("MT requires exactly two arguments: MT(modifier, tap_key).");

        ValidateKnownOptions(invocation, TapHoldOptionNames);
        var modifier = invocation.Arguments[0];
        var tapKey = new KeyId(invocation.Arguments[1]);
        var options = new ModTapOptions
        {
            TappingTermMs = ReadDurationMs(
                invocation,
                "tapping_term",
                ModTapOptions.DefaultTappingTermMs),
            HoldOnOtherKeyPress = ReadBoolean(invocation, "hold_on_other_key_press", true)
        };
        return StandardBehaviors.MT(modifier, tapKey, options);
    }

    private static BehaviorDefinition CreateMomentaryLayer(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "MO(layer)");
        return StandardBehaviors.MO(invocation.Arguments[0]);
    }

    private static BehaviorDefinition CreateModifierHold(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "MOD(modifier)");
        return StandardBehaviors.MOD(NormalizeModifier(invocation.Arguments[0]));
    }

    private static BehaviorDefinition CreateText(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
            return StandardBehaviors.Press(BehaviorAction.SendText(invocation.Arguments[0]));

        RequireCount(invocation, 0, "TEXT() { value = \"...\" }");
        ValidateKnownOptions(invocation, ["value"]);
        return StandardBehaviors.Press(BehaviorAction.SendText(RequireOption(invocation, "value")));
    }

    private static BehaviorDefinition CreateMacro(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
            return StandardBehaviors.Press(BehaviorAction.Macro(invocation.Arguments[0]));

        RequireCount(invocation, 0, "MACRO() { template = \"...\" }");
        ValidateKnownOptions(invocation, ["template"]);
        return StandardBehaviors.Press(BehaviorAction.Macro(RequireOption(invocation, "template")));
    }

    private static BehaviorDefinition CreateMouseMove(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count == 2 && invocation.Options.Count == 0)
        {
            return StandardBehaviors.Press(BehaviorAction.MouseMove(
                ParseInteger(invocation.Arguments[0], "mouse X delta"),
                ParseInteger(invocation.Arguments[1], "mouse Y delta")));
        }

        RequireCount(invocation, 0, "MOUSE_MOVE() { x = -30; y = 10 }");
        ValidateKnownOptions(invocation, ["x", "y"]);
        return StandardBehaviors.Press(BehaviorAction.MouseMove(
            ParseInteger(RequireOption(invocation, "x"), "mouse X delta"),
            ParseInteger(RequireOption(invocation, "y"), "mouse Y delta")));
    }

    private static BehaviorDefinition CreateScroll(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "SCROLL(direction_or_delta)");
        var value = invocation.Arguments[0];
        var delta = value.ToUpperInvariant() switch
        {
            "UP" => 120,
            "DOWN" => -120,
            _ => ParseInteger(value, "scroll delta")
        };
        return StandardBehaviors.Press(BehaviorAction.Scroll(delta));
    }

    private static BehaviorDefinition CreateNamedPress(
        BehaviorInvocationProfile invocation,
        string description,
        Func<string, BehaviorAction> createAction,
        params string[] allowed)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, $"{invocation.Name}(...)");
        return StandardBehaviors.Press(createAction(Normalize(invocation.Arguments[0], description, allowed)));
    }

    private static void RequireNoOptions(BehaviorInvocationProfile invocation)
    {
        if (invocation.Options.Count != 0)
            throw new InvalidDataException($"{invocation.Name} does not support per-instance options.");
    }

    private static string RequireOption(BehaviorInvocationProfile invocation, string name)
        => invocation.Options.TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException($"{invocation.Name} requires option '{name}'.");

    private static string NormalizeModifier(string value)
        => value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => "Control",
            "SHIFT" => "Shift",
            "ALT" => "Alt",
            "GUI" or "WIN" or "SUPER" => "Gui",
            _ => throw new InvalidDataException($"Unknown modifier '{value}'.")
        };

    private static string Normalize(string value, string description, params string[] allowed)
    {
        foreach (var choice in allowed)
            if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
                return choice;
        throw new InvalidDataException($"Unknown {description} '{value}'. Allowed: {string.Join(", ", allowed)}.");
    }

    private static int ParseInteger(string value, string description)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Invalid {description} '{value}'; expected an integer.");

    private static void RequireCount(BehaviorInvocationProfile invocation, int expected, string signature)
    {
        if (invocation.Arguments.Count != expected)
            throw new InvalidDataException($"{invocation.Name} requires {expected} argument(s): {signature}.");
    }

    private static void ValidateKnownOptions(
        BehaviorInvocationProfile invocation,
        IReadOnlyCollection<string> knownNames)
    {
        foreach (var option in invocation.Options.Keys)
        {
            if (!knownNames.Contains(option, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"{invocation.Name} does not support option '{option}'.");
        }
    }

    private static int ReadDurationMs(
        BehaviorInvocationProfile invocation,
        string optionName,
        int defaultValue)
    {
        if (!invocation.Options.TryGetValue(optionName, out var raw))
            return defaultValue;

        if (!raw.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(raw.AsSpan(0, raw.Length - 2), out var value) ||
            value < 0)
        {
            throw new InvalidDataException(
                $"{invocation.Name}.{optionName} must be a non-negative duration such as '170ms'.");
        }

        return value;
    }

    private static bool ReadBoolean(
        BehaviorInvocationProfile invocation,
        string optionName,
        bool defaultValue)
    {
        if (!invocation.Options.TryGetValue(optionName, out var raw))
            return defaultValue;
        if (bool.TryParse(raw, out var value))
            return value;

        throw new InvalidDataException($"{invocation.Name}.{optionName} must be true or false.");
    }
}
