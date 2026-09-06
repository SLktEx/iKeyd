using System.Text.Json;
using iKeyd.Core.Chords;

namespace iKeyd.Core.Configuration;

public static class AutomationProfileJson
{
    private const int MaxUserBehaviorStatementDepth = 32;

    public static AutomationProfile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Configuration path must not be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Automation profile was not found.", path);

        return Parse(File.ReadAllText(path));
    }

    public static AutomationProfile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Profile JSON must not be empty.", nameof(json));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var chordWindowMs = ChordEngine<string>.DefaultChordWindowMs;
        if (root.TryGetProperty("source", out var source) &&
            source.TryGetProperty("chordWindowMs", out var chordWindow))
        {
            chordWindowMs = chordWindow.GetInt32();
        }
        if (chordWindowMs < 0)
            throw new InvalidDataException("source.chordWindowMs must be non-negative.");

        var startupMode = "S";
        if (root.TryGetProperty("startupMode", out var startupModeElement))
            startupMode = startupModeElement.GetString() ?? throw new InvalidDataException("startupMode must be a string.");
        if (string.IsNullOrWhiteSpace(startupMode))
            throw new InvalidDataException("startupMode must not be empty.");

        if (!root.TryGetProperty("singleStroke", out var singleRoot) || singleRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("singleStroke must be an object.");
        if (!root.TryGetProperty("chords", out var chordRoot) || chordRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("chords must be an object.");

        var hasBehaviors = root.TryGetProperty("behaviors", out var behaviorRoot);
        if (hasBehaviors && behaviorRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("behaviors must be an object.");

        var modeNames = singleRoot.EnumerateObject().Select(property => property.Name)
            .Concat(chordRoot.EnumerateObject().Select(property => property.Name))
            .Concat(hasBehaviors ? behaviorRoot.EnumerateObject().Select(property => property.Name) : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var keymaps = new List<AutomationKeymapProfile>();
        foreach (var mode in modeNames)
        {
            if (!TryGetPropertyIgnoreCase(singleRoot, mode, out var singlesElement))
                throw new InvalidDataException($"singleStroke.{mode} is missing from the profile.");
            if (!TryGetPropertyIgnoreCase(chordRoot, mode, out var chordsElement))
                throw new InvalidDataException($"chords.{mode} is missing from the profile.");
            if (singlesElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"singleStroke.{mode} must be an object.");
            if (chordsElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"chords.{mode} must be an array.");

            var singles = singlesElement.EnumerateObject()
                .Select(item => new SingleMapping<string>(item.Name, item.Value.GetString() ?? string.Empty))
                .ToArray();

            var chords = new List<ChordMapping<string>>();
            foreach (var item in chordsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 3)
                    throw new InvalidDataException($"A chords.{mode} entry must contain [first, second, output].");

                chords.Add(new ChordMapping<string>(
                    item[0].GetString() ?? throw new InvalidDataException("Chord first key is missing."),
                    item[1].GetString() ?? throw new InvalidDataException("Chord second key is missing."),
                    item[2].GetString() ?? string.Empty));
            }

            var behaviors = new List<BehaviorMappingProfile>();
            if (hasBehaviors && TryGetPropertyIgnoreCase(behaviorRoot, mode, out var behaviorsElement))
            {
                if (behaviorsElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"behaviors.{mode} must be an object.");

                foreach (var item in behaviorsElement.EnumerateObject())
                {
                    if (item.Value.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException($"behaviors.{mode}.{item.Name} must be an object.");

                    var invocation = item.Value;
                    var behaviorName = invocation.GetProperty("name").GetString()
                        ?? throw new InvalidDataException($"behaviors.{mode}.{item.Name}.name must be a string.");
                    if (!invocation.TryGetProperty("arguments", out var argumentsElement) ||
                        argumentsElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException($"behaviors.{mode}.{item.Name}.arguments must be an array.");
                    }

                    var arguments = ReadStringArray(argumentsElement, $"behaviors.{mode}.{item.Name}.arguments");
                    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (invocation.TryGetProperty("options", out var optionsElement))
                    {
                        if (optionsElement.ValueKind != JsonValueKind.Object)
                            throw new InvalidDataException($"behaviors.{mode}.{item.Name}.options must be an object.");

                        foreach (var option in optionsElement.EnumerateObject())
                        {
                            options.Add(
                                option.Name,
                                ReadBehaviorOptionValue(option.Value, $"behaviors.{mode}.{item.Name}.options.{option.Name}"));
                        }
                    }

                    behaviors.Add(new BehaviorMappingProfile(
                        new KeyId(item.Name),
                        new BehaviorInvocationProfile(behaviorName, arguments, options)));
                }
            }

            keymaps.Add(new AutomationKeymapProfile(mode, singles, chords, behaviors));
        }

        var hotkeys = new List<HotkeyBinding>();
        if (root.TryGetProperty("hotkeys", out var hotkeysElement))
        {
            if (hotkeysElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("hotkeys must be an array.");

            foreach (var item in hotkeysElement.EnumerateArray())
            {
                var trigger = item.GetProperty("trigger").GetString()
                    ?? throw new InvalidDataException("A hotkey trigger must be a string.");
                var action = item.GetProperty("action").GetString()
                    ?? throw new InvalidDataException("A hotkey action must be a string.");
                hotkeys.Add(new HotkeyBinding(trigger, action));
            }
        }

        var behaviorDefinitions = ParseUserBehaviorDefinitions(root);
        return new AutomationProfile(
            chordWindowMs,
            keymaps,
            startupMode,
            hotkeys,
            behaviorDefinitions);
    }

    public static void Save(AutomationProfile profile, string path)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Output path must not be empty.", nameof(path));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        Write(profile, writer);
    }

    public static string Serialize(AutomationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            Write(profile, writer);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(AutomationProfile profile, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteNumber("chordWindowMs", profile.ChordWindowMs);
        writer.WriteEndObject();
        writer.WriteString("startupMode", profile.StartupMode);

        writer.WritePropertyName("singleStroke");
        writer.WriteStartObject();
        foreach (var keymap in profile.Keymaps.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(keymap.Name);
            writer.WriteStartObject();
            var effectiveSingles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in keymap.SingleMappings)
                effectiveSingles[mapping.Key.Value] = mapping.Output;
            foreach (var mapping in effectiveSingles.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                writer.WriteString(mapping.Key, mapping.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("chords");
        writer.WriteStartObject();
        foreach (var keymap in profile.Keymaps.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(keymap.Name);
            writer.WriteStartArray();
            foreach (var mapping in keymap.ChordMappings)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(mapping.First.Value);
                writer.WriteStringValue(mapping.Second.Value);
                writer.WriteStringValue(mapping.Output);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();

        if (profile.Keymaps.Values.Any(keymap => keymap.BehaviorMappings.Count != 0))
        {
            writer.WritePropertyName("behaviors");
            writer.WriteStartObject();
            foreach (var keymap in profile.Keymaps.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(keymap.Name);
                writer.WriteStartObject();
                foreach (var mapping in keymap.BehaviorMappings.OrderBy(value => value.Key.Value, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WritePropertyName(mapping.Key.Value);
                    writer.WriteStartObject();
                    writer.WriteString("name", mapping.Invocation.Name);
                    writer.WritePropertyName("arguments");
                    writer.WriteStartArray();
                    foreach (var argument in mapping.Invocation.Arguments)
                        writer.WriteStringValue(argument);
                    writer.WriteEndArray();
                    if (mapping.Invocation.Options.Count != 0)
                    {
                        writer.WritePropertyName("options");
                        writer.WriteStartObject();
                        foreach (var option in mapping.Invocation.Options.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                            writer.WriteString(option.Key, option.Value);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        if (profile.BehaviorDefinitions.Count != 0)
            WriteUserBehaviorDefinitions(profile.BehaviorDefinitions.Values, writer);

        if (profile.Hotkeys.Count > 0)
        {
            writer.WritePropertyName("hotkeys");
            writer.WriteStartArray();
            foreach (var hotkey in profile.Hotkeys)
            {
                writer.WriteStartObject();
                writer.WriteString("trigger", hotkey.Trigger);
                writer.WriteString("action", hotkey.Action);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    private static IReadOnlyList<UserBehaviorDefinitionProfile> ParseUserBehaviorDefinitions(JsonElement root)
    {
        if (!root.TryGetProperty("behaviorDefinitions", out var definitionsElement))
            return [];
        if (definitionsElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("behaviorDefinitions must be an object.");

        var result = new List<UserBehaviorDefinitionProfile>();
        foreach (var item in definitionsElement.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"behaviorDefinitions.{item.Name} must be an object.");

            var definition = item.Value;
            if (!definition.TryGetProperty("parameters", out var parametersElement) ||
                parametersElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"behaviorDefinitions.{item.Name}.parameters must be an array.");
            }
            var parameters = ReadStringArray(parametersElement, $"behaviorDefinitions.{item.Name}.parameters");

            var locals = new List<UserBehaviorLocalProfile>();
            if (definition.TryGetProperty("locals", out var localsElement))
            {
                if (localsElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"behaviorDefinitions.{item.Name}.locals must be an object.");
                foreach (var local in localsElement.EnumerateObject())
                {
                    if (local.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException($"behaviorDefinitions.{item.Name}.locals.{local.Name} must be boolean.");
                    locals.Add(new UserBehaviorLocalProfile(local.Name, local.Value.GetBoolean()));
                }
            }

            var handlers = new List<UserBehaviorHandlerProfile>();
            if (definition.TryGetProperty("handlers", out var handlersElement))
            {
                if (handlersElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"behaviorDefinitions.{item.Name}.handlers must be an object.");
                foreach (var handler in handlersElement.EnumerateObject())
                {
                    if (handler.Value.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException($"behaviorDefinitions.{item.Name}.handlers.{handler.Name} must be an object.");
                    var handlerParameters = handler.Value.TryGetProperty("parameters", out var handlerParametersElement)
                        ? ReadStringArray(handlerParametersElement, $"behaviorDefinitions.{item.Name}.handlers.{handler.Name}.parameters")
                        : [];
                    if (!handler.Value.TryGetProperty("statements", out var statementsElement) ||
                        statementsElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException($"behaviorDefinitions.{item.Name}.handlers.{handler.Name}.statements must be an array.");
                    }
                    handlers.Add(new UserBehaviorHandlerProfile(
                        handler.Name,
                        handlerParameters,
                        ParseStatements(statementsElement, 0)));
                }
            }

            result.Add(new UserBehaviorDefinitionProfile(item.Name, parameters, locals, handlers));
        }
        return result;
    }

    private static IReadOnlyList<UserBehaviorStatementProfile> ParseStatements(JsonElement array, int depth)
    {
        if (depth > MaxUserBehaviorStatementDepth)
            throw new InvalidDataException("User behavior statement nesting exceeds the supported limit.");
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("User behavior statements must be an array.");

        var result = new List<UserBehaviorStatementProfile>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("A user behavior statement must be an object.");
            var op = element.GetProperty("op").GetString()
                ?? throw new InvalidDataException("User behavior statement op must be a string.");
            var target = element.TryGetProperty("target", out var targetElement) ? targetElement.GetString() : null;
            var value = element.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
            var condition = element.TryGetProperty("condition", out var conditionElement) ? conditionElement.GetString() : null;
            var thenStatements = element.TryGetProperty("then", out var thenElement)
                ? ParseStatements(thenElement, depth + 1)
                : [];
            var elseStatements = element.TryGetProperty("else", out var elseElement)
                ? ParseStatements(elseElement, depth + 1)
                : [];
            result.Add(new UserBehaviorStatementProfile(op, target, value, condition, thenStatements, elseStatements));
        }
        return result;
    }

    private static void WriteUserBehaviorDefinitions(
        IEnumerable<UserBehaviorDefinitionProfile> definitions,
        Utf8JsonWriter writer)
    {
        writer.WritePropertyName("behaviorDefinitions");
        writer.WriteStartObject();
        foreach (var definition in definitions.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(definition.Name);
            writer.WriteStartObject();
            writer.WritePropertyName("parameters");
            writer.WriteStartArray();
            foreach (var parameter in definition.Parameters)
                writer.WriteStringValue(parameter);
            writer.WriteEndArray();

            writer.WritePropertyName("locals");
            writer.WriteStartObject();
            foreach (var local in definition.Locals.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
                writer.WriteBoolean(local.Name, local.InitialValue);
            writer.WriteEndObject();

            writer.WritePropertyName("handlers");
            writer.WriteStartObject();
            foreach (var handler in definition.Handlers.OrderBy(value => value.Event, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(handler.Event);
                writer.WriteStartObject();
                writer.WritePropertyName("parameters");
                writer.WriteStartArray();
                foreach (var parameter in handler.Parameters)
                    writer.WriteStringValue(parameter);
                writer.WriteEndArray();
                writer.WritePropertyName("statements");
                WriteStatements(handler.Statements, writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteStatements(
        IReadOnlyList<UserBehaviorStatementProfile> statements,
        Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach (var statement in statements)
        {
            writer.WriteStartObject();
            writer.WriteString("op", statement.Op);
            if (statement.Target is not null)
                writer.WriteString("target", statement.Target);
            if (statement.Value is not null)
                writer.WriteString("value", statement.Value);
            if (statement.Condition is not null)
                writer.WriteString("condition", statement.Condition);
            if (statement.Then.Count != 0)
            {
                writer.WritePropertyName("then");
                WriteStatements(statement.Then, writer);
            }
            if (statement.Else.Count != 0)
            {
                writer.WritePropertyName("else");
                WriteStatements(statement.Else, writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{location} must be an array.");
        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{location} must contain strings.");
            result.Add(item.GetString()!);
        }
        return result;
    }

    private static string ReadBehaviorOptionValue(JsonElement value, string location)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new InvalidDataException($"{location} must be a string, number, or boolean.")
        };

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
