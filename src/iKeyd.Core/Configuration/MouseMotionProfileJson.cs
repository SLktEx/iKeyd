using System.Text.Json;

namespace iKeyd.Core.Configuration;

public static class MouseMotionProfileJson
{
    public static AutomationProfile Apply(AutomationProfile profile, string json)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Profile JSON must not be empty.", nameof(json));

        using var document = JsonDocument.Parse(json);
        var mouse = Parse(document.RootElement);
        if (mouse == MouseMotionProfile.Default)
            return profile;

        return new AutomationProfile(
            profile.ChordWindowMs,
            profile.Keymaps.Values,
            profile.StartupMode,
            profile.Hotkeys,
            profile.BehaviorDefinitions.Values,
            profile.Clipboard,
            mouse);
    }

    public static MouseMotionProfile Parse(JsonElement root)
    {
        if (!root.TryGetProperty("mouse", out var mouseElement))
            return MouseMotionProfile.Default;
        if (mouseElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("mouse must be an object.");

        var defaults = MouseMotionProfile.Default;
        var engine = ReadOptionalString(mouseElement, "engine", defaults.Engine, "mouse");
        var updateMs = ReadOptionalInt32(mouseElement, "updateMs", defaults.UpdateIntervalMs, "mouse");
        var socd = ReadOptionalString(mouseElement, "socd", defaults.Socd, "mouse");
        var tapNudgePixels = ReadOptionalInt32(mouseElement, "tapNudgePixels", defaults.TapNudgePixels, "mouse");
        var maxCatchupMs = ReadOptionalInt32(mouseElement, "maxCatchupMs", defaults.MaxCatchupMs, "mouse");

        var pressMs = defaults.PressMs;
        var releaseMs = defaults.ReleaseMs;
        var curve = defaults.Curve;
        if (mouseElement.TryGetProperty("response", out var response))
        {
            if (response.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("mouse.response must be an object.");
            pressMs = ReadOptionalInt32(response, "pressMs", pressMs, "mouse.response");
            releaseMs = ReadOptionalInt32(response, "releaseMs", releaseMs, "mouse.response");
            curve = ReadOptionalString(response, "curve", curve, "mouse.response");
        }

        var normal = defaults.NormalSpeed;
        var precision = defaults.PrecisionSpeed;
        var fine = defaults.FineSpeed;
        var fast = defaults.FastSpeed;
        if (mouseElement.TryGetProperty("speed", out var speed))
        {
            if (speed.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("mouse.speed must be an object.");
            normal = ReadOptionalDouble(speed, "normal", normal, "mouse.speed");
            precision = ReadOptionalDouble(speed, "precision", precision, "mouse.speed");
            fine = ReadOptionalDouble(speed, "fine", fine, "mouse.speed");
            fast = ReadOptionalDouble(speed, "fast", fast, "mouse.speed");
        }

        try
        {
            return new MouseMotionProfile(
                engine,
                updateMs,
                pressMs,
                releaseMs,
                curve,
                normal,
                precision,
                fine,
                fast,
                socd,
                tapNudgePixels,
                maxCatchupMs);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Invalid mouse settings: {exception.Message}", exception);
        }
    }

    private static int ReadOptionalInt32(JsonElement element, string name, int fallback, string location)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"{location}.{name} must be an integer.");
        return result;
    }

    private static double ReadOptionalDouble(JsonElement element, string name, double fallback, string location)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new InvalidDataException($"{location}.{name} must be a finite number.");
        return result;
    }

    private static string ReadOptionalString(JsonElement element, string name, string fallback, string location)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{location}.{name} must be a string.");
        return value.GetString() ?? string.Empty;
    }
}
