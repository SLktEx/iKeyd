using System.Globalization;
using System.Text.Json;
using iKeyd.Core.Chords;

namespace iKeyd.Core.Configuration;

internal static class KeyBehaviorProfileJson
{
    public static KeyBehaviorProfile Parse(JsonElement root)
    {
        var layers = new List<KeyBehaviorLayer>();
        if (root.TryGetProperty("layers", out var layersElement))
        {
            if (layersElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("layers must be an object.");

            foreach (var layerProperty in layersElement.EnumerateObject())
            {
                if (layerProperty.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"layers.{layerProperty.Name} must be an object.");

                var bindings = new List<KeyBehaviorLayerBinding>();
                foreach (var bindingProperty in layerProperty.Value.EnumerateObject())
                {
                    var key = ParseCompactKey(bindingProperty.Name, $"layers.{layerProperty.Name}");
                    var action = ParseAction(bindingProperty.Value, $"layers.{layerProperty.Name}.{bindingProperty.Name}");
                    if (action.IsHoldAction)
                        throw new InvalidDataException($"layers.{layerProperty.Name}.{bindingProperty.Name} must emit an output action, not layer/modifier.");
                    bindings.Add(new KeyBehaviorLayerBinding(key, action));
                }

                layers.Add(new KeyBehaviorLayer(layerProperty.Name, bindings));
            }
        }

        var behaviors = new List<KeyBehaviorBinding>();
        if (root.TryGetProperty("behaviors", out var behaviorsElement))
        {
            if (behaviorsElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("behaviors must be an object.");

            foreach (var behaviorProperty in behaviorsElement.EnumerateObject())
            {
                if (behaviorProperty.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"behaviors.{behaviorProperty.Name} must be an object.");

                var location = $"behaviors.{behaviorProperty.Name}";
                var trigger = ParseCompactKey(behaviorProperty.Name, "behaviors");
                KeyBehaviorAction? tap = null;
                if (behaviorProperty.Value.TryGetProperty("tap", out var tapElement))
                    tap = ParseAction(tapElement, $"{location}.tap");

                if (!behaviorProperty.Value.TryGetProperty("hold", out var holdElement))
                    throw new InvalidDataException($"{location}.hold is required.");
                var hold = ParseAction(holdElement, $"{location}.hold");

                var timeoutMs = behaviorProperty.Value.TryGetProperty("timeoutMs", out var timeoutElement)
                    ? timeoutElement.GetInt32()
                    : 180;
                if (timeoutMs <= 0)
                    throw new InvalidDataException($"{location}.timeoutMs must be positive.");

                var interrupt = TapHoldInterruptPolicy.Hold;
                if (behaviorProperty.Value.TryGetProperty("interrupt", out var interruptElement))
                {
                    var value = interruptElement.GetString()
                        ?? throw new InvalidDataException($"{location}.interrupt must be a string.");
                    interrupt = value.ToLowerInvariant() switch
                    {
                        "hold" => TapHoldInterruptPolicy.Hold,
                        "tap" => TapHoldInterruptPolicy.Tap,
                        _ => throw new InvalidDataException($"{location}.interrupt must be 'hold' or 'tap'.")
                    };
                }

                try
                {
                    behaviors.Add(new KeyBehaviorBinding(trigger, tap, hold, timeoutMs, interrupt));
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"Invalid {location}: {exception.Message}", exception);
                }
            }
        }

        try
        {
            return new KeyBehaviorProfile(behaviors, layers);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Invalid key behavior profile: {exception.Message}", exception);
        }
    }

    public static void Write(KeyBehaviorProfile profile, Utf8JsonWriter writer)
    {
        if (profile.Layers.Count > 0)
        {
            writer.WritePropertyName("layers");
            writer.WriteStartObject();
            foreach (var layer in profile.Layers.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(layer.Name);
                writer.WriteStartObject();
                foreach (var binding in layer.Bindings.OrderBy(pair => pair.Key))
                {
                    writer.WritePropertyName(binding.Key.Value);
                    WriteAction(binding.Value, writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        if (profile.Behaviors.Count > 0)
        {
            writer.WritePropertyName("behaviors");
            writer.WriteStartObject();
            foreach (var behavior in profile.Behaviors.Values.OrderBy(value => value.Trigger))
            {
                writer.WritePropertyName(behavior.Trigger.Value);
                writer.WriteStartObject();
                if (behavior.Tap is { } tap)
                {
                    writer.WritePropertyName("tap");
                    WriteAction(tap, writer);
                }
                writer.WritePropertyName("hold");
                WriteAction(behavior.Hold, writer);
                writer.WriteNumber("timeoutMs", behavior.TimeoutMs);
                writer.WriteString("interrupt", behavior.Interrupt == TapHoldInterruptPolicy.Hold ? "hold" : "tap");
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
    }

    private static KeyId ParseCompactKey(string value, string location)
    {
        if (!KeyId.TryParseCompact(value, out var code))
            throw new InvalidDataException($"{location} contains unsupported physical key '{value}'.");
        return new KeyId(code);
    }

    private static KeyBehaviorAction ParseAction(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{location} must be an action object.");

        var kindText = element.GetProperty("kind").GetString()
            ?? throw new InvalidDataException($"{location}.kind must be a string.");
        var value = element.GetProperty("value").GetString()
            ?? throw new InvalidDataException($"{location}.value must be a string.");

        return kindText.ToLowerInvariant() switch
        {
            "key" => KeyBehaviorAction.Key(value),
            "text" => KeyBehaviorAction.Text(value),
            "layer" => KeyBehaviorAction.Layer(value),
            "modifier" => KeyBehaviorAction.Modifier(ParseModifier(value, location)),
            "mouse_move" => ParseMouseMove(value, location),
            "mouse_click" => KeyBehaviorAction.MouseClick(ParseChoice(value, location, "Left", "Right", "Middle")),
            "scroll" => KeyBehaviorAction.Scroll(ParseChoice(value, location, "Up", "Down")),
            "media" => KeyBehaviorAction.Media(ParseChoice(value, location, "VolumeUp", "VolumeMute", "VolumeDown", "NextTrack", "PlayPause", "PreviousTrack")),
            "window" => KeyBehaviorAction.Window(ParseChoice(value, location,
                "Minimize", "ToggleMaximize", "LeftHalf", "RightHalf", "TopHalf", "BottomHalf",
                "ToggleTopMost", "OpacityUp", "OpacityDown", "ToggleCaption", "ActivateBottomSameClass")),
            _ => throw new InvalidDataException($"{location}.kind '{kindText}' is unsupported.")
        };
    }

    private static KeyBehaviorAction ParseMouseMove(string value, string location)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            throw new InvalidDataException($"{location} mouse_move value must be 'deltaX,deltaY'.");
        return KeyBehaviorAction.MouseMove(x, y);
    }

    private static string ParseChoice(string value, string location, params string[] allowed)
    {
        foreach (var choice in allowed)
            if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
                return choice;
        throw new InvalidDataException($"{location} contains unsupported value '{value}'. Allowed: {string.Join(", ", allowed)}.");
    }

    private static KeyBehaviorModifier ParseModifier(string value, string location)
        => value.ToLowerInvariant() switch
        {
            "ctrl" or "control" => KeyBehaviorModifier.Control,
            "shift" => KeyBehaviorModifier.Shift,
            "alt" => KeyBehaviorModifier.Alt,
            "gui" or "win" or "super" => KeyBehaviorModifier.Gui,
            _ => throw new InvalidDataException($"{location} contains unknown modifier '{value}'.")
        };

    private static void WriteAction(KeyBehaviorAction action, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", ActionKindName(action.Kind));
        writer.WriteString("value", action.Value);
        writer.WriteEndObject();
    }

    private static string ActionKindName(KeyBehaviorActionKind kind) => kind switch
    {
        KeyBehaviorActionKind.Key => "key",
        KeyBehaviorActionKind.Text => "text",
        KeyBehaviorActionKind.Layer => "layer",
        KeyBehaviorActionKind.Modifier => "modifier",
        KeyBehaviorActionKind.MouseMove => "mouse_move",
        KeyBehaviorActionKind.MouseClick => "mouse_click",
        KeyBehaviorActionKind.Scroll => "scroll",
        KeyBehaviorActionKind.Media => "media",
        KeyBehaviorActionKind.Window => "window",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
