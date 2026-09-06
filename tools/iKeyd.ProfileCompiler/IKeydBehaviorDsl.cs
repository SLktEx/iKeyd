using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using iKeyd.Core.Chords;

internal sealed record IKeydBehaviorDslExtension(string CleanSource, JsonObject Layers, JsonObject Behaviors);

internal static class IKeydBehaviorDsl
{
    private const string Ident = "[A-Za-z0-9_]+";
    private const string KeyRef = Ident + @"(?:\[\s*\d+\s*,\s*\d+\s*\]|\." + Ident + @")?";

    private static readonly string[][] Jis109Layout =
    [
        ["Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PrintScreen", "ScrollLock", "Pause"],
        ["ZenkakuHankaku", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Minus", "Caret", "Yen", "Backspace"],
        ["Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "AT", "LeftBracket"],
        ["CapsLock", "A", "S", "D", "F", "G", "H", "J", "K", "L", "SColon", "Colon", "RightBracket", "Enter"],
        ["LeftShift", "Z", "X", "C", "V", "B", "N", "M", "Comma", "Dot", "Slash", "Ro", "RightShift"],
        ["LeftControl", "LeftGui", "LeftAlt", "Muhenkan", "Space", "Henkan", "KatakanaHiragana", "RightAlt", "RightGui", "Menu", "RightControl"],
        ["Insert", "Home", "PageUp"],
        ["Delete", "End", "PageDown"],
        ["Left", "Up", "Down", "Right"],
        ["NumLock", "NumpadSlash", "NumpadAsterisk", "NumpadMinus"],
        ["Numpad7", "Numpad8", "Numpad9", "NumpadPlus"],
        ["Numpad4", "Numpad5", "Numpad6"],
        ["Numpad1", "Numpad2", "Numpad3", "NumpadEnter"],
        ["Numpad0", "NumpadDot"]
    ];

    public static IKeydBehaviorDslExtension Extract(string source, string path)
    {
        ArgumentNullException.ThrowIfNull(source);
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var layouts = ScanLayouts(lines, path);
        var clean = lines.ToArray();
        var layers = new JsonObject();
        var behaviors = new JsonObject();

        string? extractedKind = null;
        string? extractedName = null;
        string? behaviorTrigger = null;
        JsonObject? behaviorDraft = null;
        var ordinaryDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
                continue;

            if (extractedKind is not null)
            {
                clean[index] = string.Empty;
                if (line == "}")
                {
                    if (extractedKind == "behavior")
                    {
                        ValidateBehaviorDraft(path, lineNumber, behaviorTrigger!, behaviorDraft!, layers);
                        behaviors.Add(behaviorTrigger!, behaviorDraft);
                    }

                    extractedKind = null;
                    extractedName = null;
                    behaviorTrigger = null;
                    behaviorDraft = null;
                    continue;
                }

                if (extractedKind == "layer")
                {
                    var match = Regex.Match(line, $@"^({KeyRef})\s*=\s*(.+)$", RegexOptions.CultureInvariant);
                    if (!match.Success)
                        throw Error(path, lineNumber, $"unknown layer statement: {line}");
                    var key = ResolveAndValidateKey(path, lineNumber, match.Groups[1].Value, layouts);
                    var action = ParseAction(path, lineNumber, match.Groups[2].Value, allowHoldActions: false);
                    var layer = (JsonObject)layers[extractedName!]!;
                    if (ContainsPropertyIgnoreCase(layer, key))
                        throw Error(path, lineNumber, $"duplicate key '{key}' in behavior layer '{extractedName}'");
                    layer.Add(key, action);
                    continue;
                }

                ParseBehaviorSetting(path, lineNumber, line, behaviorDraft!);
                continue;
            }

            if (ordinaryDepth > 0)
            {
                if (line.EndsWith("{", StringComparison.Ordinal))
                    ordinaryDepth++;
                if (line == "}")
                    ordinaryDepth--;
                continue;
            }

            if (IsOrdinaryTopLevelBlock(line))
            {
                ordinaryDepth = 1;
                continue;
            }

            var layerStart = Regex.Match(line, $@"^layer\s+({Ident})\s*\{{$", RegexOptions.CultureInvariant);
            if (layerStart.Success)
            {
                var name = layerStart.Groups[1].Value;
                if (ContainsPropertyIgnoreCase(layers, name))
                    throw Error(path, lineNumber, $"duplicate behavior layer '{name}'");
                layers.Add(name, new JsonObject());
                extractedKind = "layer";
                extractedName = name;
                clean[index] = string.Empty;
                continue;
            }

            var behaviorBlock = Regex.Match(line, $@"^behavior\s+({KeyRef})\s*\{{$", RegexOptions.CultureInvariant);
            if (behaviorBlock.Success)
            {
                var trigger = ResolveAndValidateKey(path, lineNumber, behaviorBlock.Groups[1].Value, layouts);
                if (ContainsPropertyIgnoreCase(behaviors, trigger))
                    throw Error(path, lineNumber, $"duplicate behavior trigger '{trigger}'");
                extractedKind = "behavior";
                behaviorTrigger = trigger;
                behaviorDraft = NewBehaviorDraft();
                clean[index] = string.Empty;
                continue;
            }

            var behaviorShort = Regex.Match(line, $@"^behavior\s+({KeyRef})\s*=\s*(.+)$", RegexOptions.CultureInvariant);
            if (behaviorShort.Success)
            {
                var trigger = ResolveAndValidateKey(path, lineNumber, behaviorShort.Groups[1].Value, layouts);
                if (ContainsPropertyIgnoreCase(behaviors, trigger))
                    throw Error(path, lineNumber, $"duplicate behavior trigger '{trigger}'");
                var behavior = ParseShorthand(path, lineNumber, behaviorShort.Groups[2].Value);
                ValidateBehaviorDraft(path, lineNumber, trigger, behavior, layers, validateLayerReference: false);
                behaviors.Add(trigger, behavior);
                clean[index] = string.Empty;
                continue;
            }
        }

        if (extractedKind is not null)
            throw Error(path, lines.Length, $"unclosed {extractedKind} block");

        foreach (var property in behaviors)
            ValidateBehaviorDraft(path, 1, property.Key, (JsonObject)property.Value!, layers);

        return new IKeydBehaviorDslExtension(string.Join('\n', clean), layers, behaviors);
    }

    public static string Merge(string baseJson, IKeydBehaviorDslExtension extension)
    {
        var root = JsonNode.Parse(baseJson) as JsonObject
            ?? throw new InvalidDataException("DSL compiler produced a non-object profile.");
        if (extension.Layers.Count > 0)
            root["layers"] = extension.Layers.DeepClone();
        if (extension.Behaviors.Count > 0)
            root["behaviors"] = extension.Behaviors.DeepClone();
        return root.ToJsonString();
    }

    private static Dictionary<string, List<List<string>>> ScanLayouts(string[] lines, string path)
    {
        var layouts = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
        string? current = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
                continue;

            if (current is not null)
            {
                if (line == "}")
                {
                    current = null;
                    continue;
                }
                var row = Regex.Match(line, @"^row\s+(.+)$", RegexOptions.CultureInvariant);
                if (row.Success)
                    layouts[current].Add(ParseLayoutRow(path, lineNumber, row.Groups[1].Value));
                continue;
            }

            var keyboard = Regex.Match(line, $@"^keyboard\s+({Ident})\s*;?$", RegexOptions.CultureInvariant);
            if (keyboard.Success && string.Equals(keyboard.Groups[1].Value, "JIS109", StringComparison.OrdinalIgnoreCase))
            {
                layouts["JIS109"] = Jis109Layout.Select(row => row.ToList()).ToList();
                continue;
            }

            var layout = Regex.Match(line, $@"^layout\s+({Ident})\s*\{{$", RegexOptions.CultureInvariant);
            if (layout.Success)
            {
                current = layout.Groups[1].Value;
                layouts.TryAdd(current, []);
            }
        }

        return layouts;
    }

    private static List<string> ParseLayoutRow(string path, int lineNumber, string value)
    {
        var keys = Regex.Split(value.Trim().TrimEnd(';'), @"[\s,]+")
            .Where(item => item.Length > 0)
            .ToList();
        if (keys.Count == 0 || keys.Any(key => !Regex.IsMatch(key, $@"^{Ident}$", RegexOptions.CultureInvariant)))
            throw Error(path, lineNumber, "expected one or more key identifiers after 'row'");
        return keys;
    }

    private static JsonObject ParseShorthand(string path, int lineNumber, string expression)
    {
        expression = expression.Trim().TrimEnd(';').Trim();
        var layerTap = Regex.Match(expression, $@"^layer_tap\(\s*({Ident})\s*,\s*({Ident})\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (layerTap.Success)
        {
            ValidateOutputKey(path, lineNumber, layerTap.Groups[2].Value);
            var result = NewBehaviorDraft();
            result["tap"] = Action("key", CanonicalKey(layerTap.Groups[2].Value));
            result["hold"] = Action("layer", layerTap.Groups[1].Value);
            return result;
        }

        var modTap = Regex.Match(expression, $@"^mod_tap\(\s*({Ident})\s*,\s*({Ident})\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (modTap.Success)
        {
            ValidateOutputKey(path, lineNumber, modTap.Groups[2].Value);
            var result = NewBehaviorDraft();
            result["tap"] = Action("key", CanonicalKey(modTap.Groups[2].Value));
            result["hold"] = Action("modifier", NormalizeModifier(path, lineNumber, modTap.Groups[1].Value));
            return result;
        }

        var hold = ParseAction(path, lineNumber, expression, allowHoldActions: true);
        var kind = hold["kind"]!.GetValue<string>();
        if (kind is not ("layer" or "modifier"))
            throw Error(path, lineNumber, "behavior shorthand must be layer_tap, mod_tap, layer(...) or modifier(...)");
        var behavior = NewBehaviorDraft();
        behavior["hold"] = hold;
        return behavior;
    }

    private static JsonObject NewBehaviorDraft() => new()
    {
        ["timeoutMs"] = 180,
        ["interrupt"] = "hold"
    };

    private static void ParseBehaviorSetting(string path, int lineNumber, string line, JsonObject draft)
    {
        var assignment = Regex.Match(line, $@"^({Ident})\s*=\s*(.+)$", RegexOptions.CultureInvariant);
        if (!assignment.Success)
            throw Error(path, lineNumber, $"unknown behavior setting: {line}");

        var name = assignment.Groups[1].Value.ToLowerInvariant();
        var value = assignment.Groups[2].Value.Trim().TrimEnd(';').Trim();
        switch (name)
        {
            case "tap":
                if (draft.ContainsKey("tap"))
                    throw Error(path, lineNumber, "duplicate behavior tap setting");
                draft["tap"] = ParseAction(path, lineNumber, value, allowHoldActions: false);
                break;
            case "hold":
                if (draft.ContainsKey("hold"))
                    throw Error(path, lineNumber, "duplicate behavior hold setting");
                var hold = ParseAction(path, lineNumber, value, allowHoldActions: true);
                if (hold["kind"]!.GetValue<string>() is not ("layer" or "modifier"))
                    throw Error(path, lineNumber, "hold must be layer(...) or modifier(...)");
                draft["hold"] = hold;
                break;
            case "timeout":
                var timeout = Regex.Match(value, @"^(\d+)\s*ms$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!timeout.Success || !int.TryParse(timeout.Groups[1].Value, out var timeoutMs) || timeoutMs <= 0)
                    throw Error(path, lineNumber, "timeout must be a positive duration such as 180ms");
                draft["timeoutMs"] = timeoutMs;
                break;
            case "interrupt":
                var policy = value.ToLowerInvariant();
                if (policy is not ("hold" or "tap"))
                    throw Error(path, lineNumber, "interrupt must be 'hold' or 'tap'");
                draft["interrupt"] = policy;
                break;
            default:
                throw Error(path, lineNumber, $"unknown behavior setting '{assignment.Groups[1].Value}'");
        }
    }

    private static JsonObject ParseAction(string path, int lineNumber, string expression, bool allowHoldActions)
    {
        expression = expression.Trim().TrimEnd(';').Trim();
        var call = Regex.Match(expression, $@"^({Ident})\((.*)\)$", RegexOptions.CultureInvariant);
        if (!call.Success)
            throw Error(path, lineNumber, $"invalid behavior action '{expression}'");

        var kind = call.Groups[1].Value.ToLowerInvariant();
        var argument = call.Groups[2].Value.Trim();
        switch (kind)
        {
            case "key":
                if (!Regex.IsMatch(argument, $@"^{Ident}$", RegexOptions.CultureInvariant))
                    throw Error(path, lineNumber, "key(...) expects one key name");
                ValidateOutputKey(path, lineNumber, argument);
                return Action("key", CanonicalKey(argument));
            case "text":
                try
                {
                    var text = JsonSerializer.Deserialize<string>(argument)
                        ?? throw Error(path, lineNumber, "text(...) requires a quoted string");
                    return Action("text", text);
                }
                catch (JsonException exception)
                {
                    throw Error(path, lineNumber, $"text(...) requires a quoted string: {exception.Message}");
                }
            case "layer" when allowHoldActions:
                if (!Regex.IsMatch(argument, $@"^{Ident}$", RegexOptions.CultureInvariant))
                    throw Error(path, lineNumber, "layer(...) expects one layer name");
                return Action("layer", argument);
            case "modifier" when allowHoldActions:
                return Action("modifier", NormalizeModifier(path, lineNumber, argument));
            default:
                throw Error(path, lineNumber, allowHoldActions
                    ? "action must be key(...), text(...), layer(...) or modifier(...)"
                    : "action must be key(...) or text(...)");
        }
    }

    private static JsonObject Action(string kind, string value) => new()
    {
        ["kind"] = kind,
        ["value"] = value
    };

    private static void ValidateBehaviorDraft(
        string path,
        int lineNumber,
        string trigger,
        JsonObject draft,
        JsonObject layers,
        bool validateLayerReference = true)
    {
        if (draft["hold"] is not JsonObject hold)
            throw Error(path, lineNumber, $"behavior '{trigger}' requires hold = layer(...) or modifier(...)");
        if (draft["tap"] is JsonObject tap && tap["kind"]!.GetValue<string>() is "layer" or "modifier")
            throw Error(path, lineNumber, $"behavior '{trigger}' tap action must be key or text");

        if (validateLayerReference && hold["kind"]!.GetValue<string>() == "layer")
        {
            var layerName = hold["value"]!.GetValue<string>();
            if (!ContainsPropertyIgnoreCase(layers, layerName))
                throw Error(path, lineNumber, $"behavior '{trigger}' references unknown layer '{layerName}'");
        }
    }

    private static string ResolveAndValidateKey(
        string path,
        int lineNumber,
        string keyRef,
        Dictionary<string, List<List<string>>> layouts)
    {
        var key = ResolveKeyRef(path, lineNumber, keyRef, layouts);
        if (!KeyId.TryParseCompact(key, out var code))
            throw Error(path, lineNumber, $"behavior key '{key}' is not a supported physical key");
        return new KeyId(code).Value;
    }

    private static string ResolveKeyRef(
        string path,
        int lineNumber,
        string value,
        Dictionary<string, List<List<string>>> layouts)
    {
        value = value.Trim();
        if (Regex.IsMatch(value, $@"^{Ident}$", RegexOptions.CultureInvariant))
            return value;

        var named = Regex.Match(value, $@"^({Ident})\.({Ident})$", RegexOptions.CultureInvariant);
        if (named.Success)
        {
            var layoutName = ResolveLayoutName(named.Groups[1].Value, layouts);
            if (layoutName is null || !layouts.TryGetValue(layoutName, out var layout))
                throw Error(path, lineNumber, $"unknown layout '{named.Groups[1].Value}' in key reference '{value}'");
            var requested = named.Groups[2].Value;
            foreach (var key in layout.SelectMany(row => row))
                if (string.Equals(key, requested, StringComparison.OrdinalIgnoreCase))
                    return key;
            throw Error(path, lineNumber, $"layout '{named.Groups[1].Value}' has no key named '{requested}'");
        }

        var coordinate = Regex.Match(value, $@"^({Ident})\[\s*(\d+)\s*,\s*(\d+)\s*\]$", RegexOptions.CultureInvariant);
        if (!coordinate.Success)
            throw Error(path, lineNumber, $"invalid key reference '{value}'");
        var resolvedName = ResolveLayoutName(coordinate.Groups[1].Value, layouts);
        if (resolvedName is null || !layouts.TryGetValue(resolvedName, out var resolved))
            throw Error(path, lineNumber, $"unknown layout '{coordinate.Groups[1].Value}' in key reference '{value}'");
        var row = int.Parse(coordinate.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var column = int.Parse(coordinate.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (row < 1 || row > resolved.Count)
            throw Error(path, lineNumber, $"row {row} is out of range for layout '{coordinate.Groups[1].Value}'");
        if (column < 1 || column > resolved[row - 1].Count)
            throw Error(path, lineNumber, $"column {column} is out of range for layout '{coordinate.Groups[1].Value}' row {row}");
        return resolved[row - 1][column - 1];
    }

    private static string? ResolveLayoutName(string requested, Dictionary<string, List<List<string>>> layouts)
    {
        if (!string.Equals(requested, "POS", StringComparison.OrdinalIgnoreCase))
            return layouts.Keys.FirstOrDefault(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));
        return layouts.ContainsKey("POS") ? "POS"
            : layouts.ContainsKey("JIS109") ? "JIS109"
            : layouts.ContainsKey("BASE") ? "BASE"
            : null;
    }

    private static void ValidateOutputKey(string path, int lineNumber, string key)
    {
        if (!KeyId.TryParseCompact(key, out _))
            throw Error(path, lineNumber, $"unknown output key '{key}'");
    }

    private static string CanonicalKey(string key)
    {
        KeyId.TryParseCompact(key, out var code);
        return new KeyId(code).Value;
    }

    private static string NormalizeModifier(string path, int lineNumber, string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => "Control",
            "shift" => "Shift",
            "alt" => "Alt",
            "gui" or "win" or "super" => "Gui",
            _ => throw Error(path, lineNumber, $"unknown modifier '{value.Trim()}'")
        };

    private static bool IsOrdinaryTopLevelBlock(string line)
        => Regex.IsMatch(line, $@"^(profile\s+{Ident}|layout\s+{Ident}|keymap\s+{Ident}(?:\s+using\s+{Ident})?|quirks)\s*\{{$", RegexOptions.CultureInvariant);

    private static bool ContainsPropertyIgnoreCase(JsonObject obj, string name)
        => obj.Any(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase));

    private static string StripComment(string line)
    {
        var inString = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }
            if (character == '"') inString = true;
            else if (character == '/' && index + 1 < line.Length && line[index + 1] == '/') return line[..index];
        }
        return line;
    }

    private static Exception Error(string path, int lineNumber, string message)
        => new IKeydDslException(path, lineNumber, message);
}
