using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

if (args.Length < 2)
{
    PrintUsage();
    return 2;
}

try
{
    var inputPath = Path.GetFullPath(args[0]);
    var outputPath = Path.GetFullPath(args[1]);
    string? emittedJsonPath = null;
    string? checkAgainstPath = null;

    for (var index = 2; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--emit-json" when index + 1 < args.Length:
                emittedJsonPath = Path.GetFullPath(args[++index]);
                break;
            case "--check-against" when index + 1 < args.Length:
                checkAgainstPath = Path.GetFullPath(args[++index]);
                break;
            default:
                PrintUsage();
                return 2;
        }
    }

    if (!File.Exists(inputPath))
        throw new FileNotFoundException("Automation profile was not found.", inputPath);

    var input = File.ReadAllText(inputPath);
    string json;
    if (string.Equals(Path.GetExtension(inputPath), ".ikeyd", StringComparison.OrdinalIgnoreCase))
    {
        var behaviorExtension = IKeydBehaviorDsl.Extract(input, inputPath);
        json = IKeydDslCompiler.CompileToJson(behaviorExtension.CleanSource, inputPath);
        json = IKeydBehaviorDsl.Merge(json, behaviorExtension);
    }
    else
    {
        json = input;
    }

    if (checkAgainstPath is not null)
    {
        if (!File.Exists(checkAgainstPath))
            throw new FileNotFoundException("Canonical profile was not found.", checkAgainstPath);

        var actualNode = JsonNode.Parse(json)
            ?? throw new InvalidDataException("Generated profile JSON is empty.");
        var expectedNode = JsonNode.Parse(File.ReadAllText(checkAgainstPath))
            ?? throw new InvalidDataException("Canonical profile JSON is empty.");
        if (!JsonNode.DeepEquals(actualNode, expectedNode))
            throw new InvalidDataException($"Generated profile differs from canonical profile '{checkAgainstPath}'.");
    }

    if (emittedJsonPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(emittedJsonPath)!);
        File.WriteAllText(emittedJsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    var generatedSource = ProfileCompiler.Compile(json);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, generatedSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"iKeyd profile compilation failed: {exception.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine(
        "Usage: iKeyd.ProfileCompiler <profile.json|profile.ikeyd> <GeneratedProfile.g.cs> " +
        "[--emit-json <generated.json>] [--check-against <canonical.json>]");
}

internal static class ProfileCompiler
{
    private const int DefaultChordWindowMs = 40;
    private const int CompactKeyCount = (int)KeyCode.NumpadComma;

    public static string Compile(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Profile JSON must not be empty.");

        var parsedProfile = AutomationProfileJson.Parse(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Profile root must be an object.");

        var chordWindowMs = DefaultChordWindowMs;
        if (root.TryGetProperty("source", out var source))
        {
            if (source.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("source must be an object.");
            if (source.TryGetProperty("chordWindowMs", out var chordWindow))
                chordWindowMs = chordWindow.GetInt32();
        }
        if (chordWindowMs < 0)
            throw new InvalidDataException("source.chordWindowMs must be non-negative.");

        var startupMode = root.TryGetProperty("startupMode", out var startupModeElement)
            ? startupModeElement.GetString() ?? throw new InvalidDataException("startupMode must be a string.")
            : "S";
        if (string.IsNullOrWhiteSpace(startupMode))
            throw new InvalidDataException("startupMode must not be empty.");
        var startupModeCode = startupMode.Trim().ToUpperInvariant();
        if (startupModeCode is not ("S" or "R" or "T" or "K"))
            throw new InvalidDataException($"Unsupported startupMode '{startupMode}' for the Windows app.");

        if (!root.TryGetProperty("singleStroke", out var singleRoot) || singleRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("singleStroke must be an object.");
        if (!root.TryGetProperty("chords", out var chordRoot) || chordRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("chords must be an object.");

        var modeNames = new List<string>();
        AddDistinctModeNames(modeNames, singleRoot);
        AddDistinctModeNames(modeNames, chordRoot);

        if (!modeNames.Any(name => string.Equals(name, "S", StringComparison.OrdinalIgnoreCase)) ||
            !modeNames.Any(name => string.Equals(name, "K", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The Windows app requires both S and K keymaps.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using iKeyd.Core.Chords;");
        builder.AppendLine("using iKeyd.Core.Configuration;");
        builder.AppendLine("using iKeyd.Core.Keymaps;");
        builder.AppendLine("using iKeyd.Profiles.HotkeySkg.Modes;");
        builder.AppendLine();
        builder.AppendLine("namespace iKeyd.App;");
        builder.AppendLine();
        builder.AppendLine("internal static class GeneratedProfile");
        builder.AppendLine("{");
        builder.AppendLine("    private static readonly Keymap<string> SKeymap = CreateSKeymap();");
        builder.AppendLine("    private static readonly Keymap<string> KKeymap = CreateKKeymap();");
        builder.AppendLine();
        builder.AppendLine("    public static IKeydConfiguration Create()");
        builder.AppendLine("    {");
        builder.AppendLine("        var profile = new AutomationProfile(");
        builder.AppendLine($"            chordWindowMs: {chordWindowMs},");
        builder.AppendLine("            keymaps: new AutomationKeymapProfile[]");
        builder.AppendLine("            {");

        foreach (var mode in modeNames)
        {
            var (singlesElement, chordsElement) = GetModeElements(singleRoot, chordRoot, mode);

            builder.AppendLine("                new AutomationKeymapProfile(");
            builder.AppendLine($"                    name: {Literal(mode)},");
            builder.AppendLine("                    singleMappings: new SingleMapping<string>[]");
            builder.AppendLine("                    {");
            foreach (var item in singlesElement.EnumerateObject())
            {
                var key = ParseKey(item.Name, $"singleStroke.{mode}");
                var output = item.Value.GetString() ?? string.Empty;
                builder.AppendLine($"                        new SingleMapping<string>({key.Expression}, {Literal(output)}),");
            }
            builder.AppendLine("                    },");
            builder.AppendLine("                    chordMappings: new ChordMapping<string>[]");
            builder.AppendLine("                    {");

            foreach (var item in chordsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 3)
                    throw new InvalidDataException($"A chords.{mode} entry must contain [first, second, output].");

                var firstName = item[0].GetString() ?? throw new InvalidDataException("Chord first key is missing.");
                var secondName = item[1].GetString() ?? throw new InvalidDataException("Chord second key is missing.");
                var output = item[2].GetString() ?? string.Empty;
                var first = ParseKey(firstName, $"chords.{mode}");
                var second = ParseKey(secondName, $"chords.{mode}");
                builder.AppendLine($"                        new ChordMapping<string>({first.Expression}, {second.Expression}, {Literal(output)}),");
            }

            builder.AppendLine("                    }),");
        }

        builder.AppendLine("            },");
        builder.AppendLine($"            startupMode: {Literal(startupMode)},");
        builder.AppendLine("            hotkeys: new HotkeyBinding[]");
        builder.AppendLine("            {");

        if (root.TryGetProperty("hotkeys", out var hotkeysElement))
        {
            if (hotkeysElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("hotkeys must be an array.");

            foreach (var item in hotkeysElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("A hotkey entry must be an object.");
                var trigger = item.GetProperty("trigger").GetString()
                    ?? throw new InvalidDataException("A hotkey trigger must be a string.");
                var action = item.GetProperty("action").GetString()
                    ?? throw new InvalidDataException("A hotkey action must be a string.");
                builder.AppendLine($"                new HotkeyBinding({Literal(trigger)}, {Literal(action)}),");
            }
        }

        builder.AppendLine("            },");
        builder.AppendLine("            keyBehaviors: CreateKeyBehaviors());");
        builder.AppendLine();
        builder.AppendLine($"        return new IKeydConfiguration(profile, InputMode.{startupModeCode}, SKeymap, KKeymap);");
        builder.AppendLine("    }");
        builder.AppendLine();

        EmitCompiledKeymapFactory(builder, "S", singleRoot, chordRoot);
        builder.AppendLine();
        EmitCompiledKeymapFactory(builder, "K", singleRoot, chordRoot);
        builder.AppendLine();
        EmitKeyBehaviorFactory(builder, parsedProfile.KeyBehaviors);

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void EmitKeyBehaviorFactory(StringBuilder builder, KeyBehaviorProfile profile)
    {
        builder.AppendLine("    private static KeyBehaviorProfile CreateKeyBehaviors()");
        builder.AppendLine("    {");
        if (profile.IsEmpty)
        {
            builder.AppendLine("        return KeyBehaviorProfile.Empty;");
            builder.AppendLine("    }");
            return;
        }

        builder.AppendLine("        return new KeyBehaviorProfile(");
        builder.AppendLine("            behaviors: new KeyBehaviorBinding[]");
        builder.AppendLine("            {");
        foreach (var behavior in profile.Behaviors.Values.OrderBy(value => value.Trigger))
        {
            var trigger = ParseKey(behavior.Trigger.Value, "behaviors");
            builder.AppendLine("                new KeyBehaviorBinding(");
            builder.AppendLine($"                    trigger: {trigger.Expression},");
            builder.AppendLine($"                    tap: {(behavior.Tap is { } tap ? BehaviorActionExpression(tap) : "null")},");
            builder.AppendLine($"                    hold: {BehaviorActionExpression(behavior.Hold)},");
            builder.AppendLine($"                    timeoutMs: {behavior.TimeoutMs},");
            builder.AppendLine($"                    interrupt: TapHoldInterruptPolicy.{behavior.Interrupt}),");
        }
        builder.AppendLine("            },");
        builder.AppendLine("            layers: new KeyBehaviorLayer[]");
        builder.AppendLine("            {");
        foreach (var layer in profile.Layers.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine("                new KeyBehaviorLayer(");
            builder.AppendLine($"                    name: {Literal(layer.Name)},");
            builder.AppendLine("                    bindings: new KeyBehaviorLayerBinding[]");
            builder.AppendLine("                    {");
            foreach (var binding in layer.Bindings.OrderBy(pair => pair.Key))
            {
                var key = ParseKey(binding.Key.Value, $"layers.{layer.Name}");
                builder.AppendLine($"                        new KeyBehaviorLayerBinding({key.Expression}, {BehaviorActionExpression(binding.Value)}),");
            }
            builder.AppendLine("                    }),");
        }
        builder.AppendLine("            });");
        builder.AppendLine("    }");
    }

    private static string BehaviorActionExpression(KeyBehaviorAction action)
        => action.Kind switch
        {
            KeyBehaviorActionKind.Key => $"KeyBehaviorAction.Key({Literal(action.Value)})",
            KeyBehaviorActionKind.Text => $"KeyBehaviorAction.Text({Literal(action.Value)})",
            KeyBehaviorActionKind.Layer => $"KeyBehaviorAction.Layer({Literal(action.Value)})",
            KeyBehaviorActionKind.Modifier => $"KeyBehaviorAction.Modifier(KeyBehaviorModifier.{action.GetModifier()})",
            _ => throw new InvalidDataException($"Unsupported behavior action kind '{action.Kind}'.")
        };

    private static void EmitCompiledKeymapFactory(
        StringBuilder builder,
        string mode,
        JsonElement singleRoot,
        JsonElement chordRoot)
    {
        var (singlesElement, chordsElement) = GetModeElements(singleRoot, chordRoot, mode);

        builder.AppendLine($"    private static Keymap<string> Create{mode}Keymap()");
        builder.AppendLine("    {");
        builder.AppendLine("        var single = new KeymapSlot<string>[Keymap<string>.CompactSingleSlotCount];");

        foreach (var item in singlesElement.EnumerateObject())
        {
            var key = ParseKey(item.Name, $"singleStroke.{mode}");
            var output = item.Value.GetString() ?? string.Empty;
            builder.AppendLine($"        single[{key.Code - 1}] = new KeymapSlot<string>({Literal(output)});");
        }

        builder.AppendLine("        var chords = new KeymapSlot<string>[Keymap<string>.CompactChordSlotCount];");
        var emittedChordIndices = new HashSet<int>();
        foreach (var item in chordsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 3)
                throw new InvalidDataException($"A chords.{mode} entry must contain [first, second, output].");

            var firstName = item[0].GetString() ?? throw new InvalidDataException("Chord first key is missing.");
            var secondName = item[1].GetString() ?? throw new InvalidDataException("Chord second key is missing.");
            var output = item[2].GetString() ?? string.Empty;
            var first = ParseKey(firstName, $"chords.{mode}");
            var second = ParseKey(secondName, $"chords.{mode}");
            var index = GetCompactChordIndex(first.Code, second.Code);

            // Legacy behavior is first declaration wins for duplicate unordered chords.
            if (emittedChordIndices.Add(index))
                builder.AppendLine($"        chords[{index}] = new KeymapSlot<string>({Literal(output)});");
        }

        builder.AppendLine("        return Keymap<string>.FromCompiledTables(single, chords);");
        builder.AppendLine("    }");
    }

    private static (JsonElement Singles, JsonElement Chords) GetModeElements(
        JsonElement singleRoot,
        JsonElement chordRoot,
        string mode)
    {
        if (!TryGetPropertyIgnoreCase(singleRoot, mode, out var singlesElement))
            throw new InvalidDataException($"singleStroke.{mode} is missing from the profile.");
        if (!TryGetPropertyIgnoreCase(chordRoot, mode, out var chordsElement))
            throw new InvalidDataException($"chords.{mode} is missing from the profile.");
        if (singlesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"singleStroke.{mode} must be an object.");
        if (chordsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"chords.{mode} must be an array.");
        return (singlesElement, chordsElement);
    }

    private static void AddDistinctModeNames(List<string> names, JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                names.Add(property.Name);
        }
    }

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

    private static KeyInfo ParseKey(string key, string location)
    {
        if (!KeyId.TryParseCompact(key, out var code))
            throw new InvalidDataException($"{location} contains unsupported key '{key}' for the compiled profile.");

        return new KeyInfo($"new KeyId(KeyCode.{code})", (int)code);
    }

    private static int GetCompactChordIndex(int firstCode, int secondCode)
    {
        var firstIndex = firstCode - 1;
        var secondIndex = secondCode - 1;
        if (firstIndex > secondIndex)
            (firstIndex, secondIndex) = (secondIndex, firstIndex);

        var rowOffset = firstIndex * CompactKeyCount - firstIndex * (firstIndex - 1) / 2;
        return rowOffset + secondIndex - firstIndex;
    }

    private static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                case '\0': builder.Append("\\0"); break;
                default:
                    if (char.IsControl(character))
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        builder.Append(character);
                    break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    private readonly record struct KeyInfo(string Expression, int Code);
}
