using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal sealed class IKeydDslException : Exception
{
    public IKeydDslException(string path, int line, string message)
        : base($"{path}:{line}: {message}")
    {
    }
}

internal static class IKeydDslCompiler
{
    private const string Ident = "[A-Za-z0-9_]+";
    private const string KeyRef = Ident + @"(?:\[\s*\d+\s*,\s*\d+\s*\]|\." + Ident + @")?";

    private sealed record ChordEntry(string First, string Second, string Output);

    public static string CompileToJson(string text, string path)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new IKeydDslException(path, 1, "profile must not be empty");

        var source = new JsonObject();
        var layouts = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
        var singles = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        var chords = new Dictionary<string, List<ChordEntry>>(StringComparer.Ordinal);
        var keymapLayouts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var modeOrder = new List<string>();
        var duplicateFlags = new JsonArray();

        string? blockKind = null;
        string? blockName = null;
        string? sectionKind = null;
        string? sectionArg = null;
        string? keyboardPreset = null;
        var mapRowIndex = 0;
        var sawProfile = false;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
                continue;

            if (line == "}")
            {
                if (sectionKind is not null)
                {
                    if (sectionKind == "map")
                    {
                        var mode = blockName!;
                        var layoutName = keymapLayouts[mode]!;
                        var expectedRows = layouts[layoutName].Count;
                        if (mapRowIndex != expectedRows)
                        {
                            throw Error(path, lineNumber,
                                $"map for keymap '{mode}' has {mapRowIndex} rows; layout '{layoutName}' has {expectedRows}");
                        }
                    }

                    sectionKind = null;
                    sectionArg = null;
                    mapRowIndex = 0;
                    continue;
                }

                if (blockKind is null)
                    throw Error(path, lineNumber, "unexpected '}'");

                blockKind = null;
                blockName = null;
                continue;
            }

            if (blockKind is null)
            {
                var profile = Match(line, $@"^profile\s+({Ident})\s*\{{$" );
                if (profile.Success)
                {
                    if (sawProfile)
                        throw Error(path, lineNumber, "only one profile block is allowed");
                    sawProfile = true;
                    blockKind = "profile";
                    blockName = profile.Groups[1].Value;
                    continue;
                }

                var keyboard = Match(line, $@"^keyboard\s+({Ident})\s*;?$");
                if (keyboard.Success)
                {
                    var requested = keyboard.Groups[1].Value;
                    if (keyboardPreset is not null)
                        throw Error(path, lineNumber, $"keyboard preset already declared as '{keyboardPreset}'");
                    if (!string.Equals(requested, "JIS109", StringComparison.OrdinalIgnoreCase))
                        throw Error(path, lineNumber, $"unknown keyboard preset '{requested}'");
                    if (layouts.ContainsKey("JIS109"))
                        throw Error(path, lineNumber, "layout 'JIS109' is already defined");

                    layouts.Add("JIS109", CreateJis109Layout());
                    keyboardPreset = "JIS109";
                    continue;
                }

                var layout = Match(line, $@"^layout\s+({Ident})\s*\{{$" );
                if (layout.Success)
                {
                    var layoutName = layout.Groups[1].Value;
                    if (layouts.ContainsKey(layoutName))
                        throw Error(path, lineNumber, $"duplicate layout '{layoutName}'");
                    layouts.Add(layoutName, []);
                    blockKind = "layout";
                    blockName = layoutName;
                    continue;
                }

                var keymap = Match(line, $@"^keymap\s+({Ident})(?:\s+using\s+({Ident}))?\s*\{{$" );
                if (keymap.Success)
                {
                    var mode = keymap.Groups[1].Value;
                    var layoutName = keymap.Groups[2].Success ? keymap.Groups[2].Value : null;
                    if (modeOrder.Any(item => string.Equals(item, mode, StringComparison.OrdinalIgnoreCase)))
                        throw Error(path, lineNumber, $"duplicate keymap '{mode}'");
                    if (layoutName is not null && !layouts.ContainsKey(layoutName))
                        throw Error(path, lineNumber, $"unknown layout '{layoutName}'");

                    modeOrder.Add(mode);
                    singles.Add(mode, []);
                    chords.Add(mode, []);
                    keymapLayouts.Add(mode, layoutName);
                    blockKind = "keymap";
                    blockName = mode;
                    continue;
                }

                if (Regex.IsMatch(line, @"^quirks\s*\{$", RegexOptions.CultureInvariant))
                {
                    blockKind = "quirks";
                    continue;
                }

                throw Error(path, lineNumber, $"unexpected top-level statement: {line}");
            }

            switch (blockKind)
            {
                case "profile":
                    ParseProfileSetting(source, path, lineNumber, line);
                    break;
                case "layout":
                    ParseLayoutStatement(layouts[blockName!], blockName!, path, lineNumber, line);
                    break;
                case "keymap":
                    ParseKeymapStatement(
                        blockName!, path, lineNumber, line, layouts, singles, chords, keymapLayouts,
                        ref sectionKind, ref sectionArg, ref mapRowIndex);
                    break;
                case "quirks":
                    ParseQuirk(duplicateFlags, path, lineNumber, line);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown DSL block '{blockKind}'.");
            }
        }

        if (sectionKind is not null)
            throw Error(path, lines.Length, $"unclosed {sectionKind} section");
        if (blockKind is not null)
            throw Error(path, lines.Length, $"unclosed {blockKind} block");
        if (!sawProfile)
            throw Error(path, 1, "profile block is required");
        if (!source.ContainsKey("chordWindowMs"))
            throw Error(path, 1, "profile.chord_window is required");
        if (modeOrder.Count == 0)
            throw Error(path, 1, "at least one keymap is required");

        var singleRoot = new JsonObject();
        var chordRoot = new JsonObject();
        foreach (var mode in modeOrder)
        {
            var modeSingles = new JsonObject();
            foreach (var (key, output) in singles[mode])
                modeSingles.Add(key, output);
            singleRoot.Add(mode, modeSingles);

            var modeChords = new JsonArray();
            foreach (var chord in chords[mode])
                modeChords.Add(new JsonArray(chord.First, chord.Second, chord.Output));
            chordRoot.Add(mode, modeChords);
        }

        var duplicateChordPatterns = new JsonObject();
        foreach (var mode in modeOrder)
            duplicateChordPatterns.Add(mode, BuildDuplicateChordMetadata(chords[mode]));

        var root = new JsonObject
        {
            ["source"] = source,
            ["singleStroke"] = singleRoot,
            ["chords"] = chordRoot,
            ["knownQuirks"] = new JsonObject
            {
                ["duplicateChordPatterns"] = duplicateChordPatterns,
                ["duplicateFlagDefinitions"] = duplicateFlags,
            },
        };

        return root.ToJsonString();
    }

    private static List<List<string>> CreateJis109Layout() =>
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
        ["Numpad0", "NumpadDot"],
    ];

    private static void ParseProfileSetting(JsonObject source, string path, int lineNumber, string line)
    {
        var runtime = Match(line, @"^runtime\s*=\s*(.+)$");
        if (runtime.Success)
        {
            source["runtime"] = ParseQuotedString(path, lineNumber, runtime.Groups[1].Value);
            return;
        }

        var executableLines = Match(line, @"^executable_lines\s*=\s*(\d+)\s*;?$");
        if (executableLines.Success)
        {
            source["executableLines"] = int.Parse(executableLines.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        var chordWindow = Match(line, @"^chord_window\s*=\s*(\d+)\s*ms\s*;?$");
        if (chordWindow.Success)
        {
            source["chordWindowMs"] = int.Parse(chordWindow.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        throw Error(path, lineNumber, $"unknown profile setting: {line}");
    }

    private static void ParseLayoutStatement(
        List<List<string>> rows,
        string layoutName,
        string path,
        int lineNumber,
        string line)
    {
        var match = Match(line, @"^row\s+(.+)$");
        if (!match.Success)
            throw Error(path, lineNumber, $"unknown layout statement: {line}");

        var row = Regex.Split(match.Groups[1].Value.Trim().TrimEnd(';'), @"[\s,]+")
            .Where(value => value.Length > 0)
            .ToList();
        if (row.Count == 0 || row.Any(key => !Regex.IsMatch(key, $"^{Ident}$", RegexOptions.CultureInvariant)))
            throw Error(path, lineNumber, "expected one or more key identifiers after 'row'");

        var seen = new HashSet<string>(rows.SelectMany(existing => existing), StringComparer.OrdinalIgnoreCase);
        foreach (var key in row)
        {
            if (!seen.Add(key))
                throw Error(path, lineNumber, $"duplicate key '{key}' in layout '{layoutName}'");
        }
        rows.Add(row);
    }

    private static void ParseKeymapStatement(
        string mode,
        string path,
        int lineNumber,
        string line,
        Dictionary<string, List<List<string>>> layouts,
        Dictionary<string, List<KeyValuePair<string, string>>> singles,
        Dictionary<string, List<ChordEntry>> chords,
        Dictionary<string, string?> keymapLayouts,
        ref string? sectionKind,
        ref string? sectionArg,
        ref int mapRowIndex)
    {
        if (sectionKind == "map")
        {
            var rowMatch = Match(line, @"^row\s+(.+)$");
            if (!rowMatch.Success)
                throw Error(path, lineNumber, $"unknown map statement: {line}");

            var layoutName = keymapLayouts[mode]!;
            var layout = layouts[layoutName];
            if (mapRowIndex >= layout.Count)
                throw Error(path, lineNumber, $"too many rows in map for keymap '{mode}'");

            var outputs = ParseOutputRow(path, lineNumber, rowMatch.Groups[1].Value);
            var keys = layout[mapRowIndex];
            if (outputs.Count != keys.Count)
            {
                throw Error(path, lineNumber,
                    $"map row {mapRowIndex + 1} has {outputs.Count} outputs; layout '{layoutName}' row has {keys.Count} keys");
            }

            for (var i = 0; i < keys.Count; i++)
                AddSingle(singles[mode], mode, keys[i], outputs[i], path, lineNumber);
            mapRowIndex++;
            return;
        }

        if (sectionKind == "combos")
        {
            var item = Match(line, $@"^({KeyRef})\s*=\s*(.+)$");
            if (!item.Success)
                throw Error(path, lineNumber, $"unknown combos statement: {line}");
            var second = ResolveKeyRef(path, lineNumber, item.Groups[1].Value, layouts);
            AddChord(chords[mode], sectionArg!, second, ParseOutput(path, lineNumber, item.Groups[2].Value), path, lineNumber);
            return;
        }

        if (Regex.IsMatch(line, @"^map\s*\{$", RegexOptions.CultureInvariant))
        {
            var layoutName = keymapLayouts[mode];
            if (layoutName is null)
                throw Error(path, lineNumber, $"keymap '{mode}' must declare 'using <layout>' before map {{");
            sectionKind = "map";
            sectionArg = layoutName;
            mapRowIndex = 0;
            return;
        }

        var comboGroup = Match(line, $@"^combos\s+({KeyRef})\s*\{{$" );
        if (comboGroup.Success)
        {
            sectionKind = "combos";
            sectionArg = ResolveKeyRef(path, lineNumber, comboGroup.Groups[1].Value, layouts);
            return;
        }

        var combo = Match(line, $@"^combo\s+({KeyRef})\s*\+\s*({KeyRef})\s*=\s*(.+)$");
        if (combo.Success)
        {
            var first = ResolveKeyRef(path, lineNumber, combo.Groups[1].Value, layouts);
            var second = ResolveKeyRef(path, lineNumber, combo.Groups[2].Value, layouts);
            AddChord(chords[mode], first, second, ParseOutput(path, lineNumber, combo.Groups[3].Value), path, lineNumber);
            return;
        }

        var single = Match(line, $@"^({KeyRef})\s*=\s*(.+)$");
        if (single.Success)
        {
            var key = ResolveKeyRef(path, lineNumber, single.Groups[1].Value, layouts);
            AddSingle(singles[mode], mode, key, ParseOutput(path, lineNumber, single.Groups[2].Value), path, lineNumber);
            return;
        }

        throw Error(path, lineNumber, $"unknown keymap statement: {line}");
    }

    private static void ParseQuirk(JsonArray duplicateFlags, string path, int lineNumber, string line)
    {
        var match = Match(line, $@"^duplicate_flag\s+({Ident})\s*=\s*(.+)$");
        if (!match.Success)
            throw Error(path, lineNumber, $"unknown quirks statement: {line}");

        var values = ParseQuotedStringList(path, lineNumber, match.Groups[2].Value);
        var expressions = new JsonArray();
        foreach (var value in values)
            expressions.Add(value);
        duplicateFlags.Add(new JsonObject
        {
            ["key"] = match.Groups[1].Value,
            ["expressions"] = expressions,
        });
    }

    private static void AddSingle(
        List<KeyValuePair<string, string>> mappings,
        string mode,
        string key,
        string output,
        string path,
        int lineNumber)
    {
        if (mappings.Any(mapping => string.Equals(mapping.Key, key, StringComparison.OrdinalIgnoreCase)))
            throw Error(path, lineNumber, $"duplicate single-stroke mapping '{mode}.{key}'");
        mappings.Add(new KeyValuePair<string, string>(key, output));
    }

    private static void AddChord(
        List<ChordEntry> mappings,
        string first,
        string second,
        string output,
        string path,
        int lineNumber)
    {
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            throw Error(path, lineNumber, $"combo cannot use the same key twice: '{first}'");
        mappings.Add(new ChordEntry(first, second, output));
    }

    private static string ResolveKeyRef(
        string path,
        int lineNumber,
        string value,
        Dictionary<string, List<List<string>>> layouts)
    {
        value = value.Trim();
        if (Regex.IsMatch(value, $"^{Ident}$", RegexOptions.CultureInvariant))
            return value;

        var named = Match(value, $@"^({Ident})\.({Ident})$");
        if (named.Success)
        {
            var layoutName = named.Groups[1].Value;
            var requestedKey = named.Groups[2].Value;
            var resolvedLayoutName = layoutName;
            if (string.Equals(layoutName, "POS", StringComparison.OrdinalIgnoreCase) && !layouts.ContainsKey("POS") && layouts.ContainsKey("BASE"))
                resolvedLayoutName = "BASE";
            if (!layouts.TryGetValue(resolvedLayoutName, out var namedLayout))
                throw Error(path, lineNumber, $"unknown layout '{layoutName}' in key reference '{value}'");

            foreach (var key in namedLayout.SelectMany(row => row))
            {
                if (string.Equals(key, requestedKey, StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            throw Error(path, lineNumber, $"layout '{layoutName}' has no key named '{requestedKey}'");
        }

        var coordinate = Match(value, $@"^({Ident})\[\s*(\d+)\s*,\s*(\d+)\s*\]$");
        if (!coordinate.Success)
            throw Error(path, lineNumber, $"invalid key reference '{value}'");

        var coordinateLayoutName = coordinate.Groups[1].Value;
        var row = int.Parse(coordinate.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var column = int.Parse(coordinate.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (row < 1 || column < 1)
            throw Error(path, lineNumber, $"key positions are 1-based: '{value}'");

        var resolvedCoordinateLayoutName = coordinateLayoutName;
        if (string.Equals(coordinateLayoutName, "POS", StringComparison.OrdinalIgnoreCase) && !layouts.ContainsKey("POS") && layouts.ContainsKey("BASE"))
            resolvedCoordinateLayoutName = "BASE";
        if (!layouts.TryGetValue(resolvedCoordinateLayoutName, out var layout))
            throw Error(path, lineNumber, $"unknown layout '{coordinateLayoutName}' in key reference '{value}'");
        if (row > layout.Count)
            throw Error(path, lineNumber, $"row {row} is out of range for layout '{coordinateLayoutName}'");
        if (column > layout[row - 1].Count)
            throw Error(path, lineNumber, $"column {column} is out of range for layout '{coordinateLayoutName}' row {row}");
        return layout[row - 1][column - 1];
    }

    private static JsonArray BuildDuplicateChordMetadata(List<ChordEntry> entries)
    {
        var groups = new List<(string First, string Second, List<string> Outputs)>();
        foreach (var entry in entries)
        {
            var pair = CanonicalPair(entry.First, entry.Second);
            var index = groups.FindIndex(group => group.First == pair.First && group.Second == pair.Second);
            if (index < 0)
            {
                groups.Add((pair.First, pair.Second, [entry.Output]));
            }
            else
            {
                groups[index].Outputs.Add(entry.Output);
            }
        }

        var result = new JsonArray();
        foreach (var group in groups.Where(group => group.Outputs.Count > 1))
        {
            var outputs = new JsonArray();
            foreach (var output in group.Outputs)
                outputs.Add(output);
            result.Add(new JsonObject
            {
                ["keys"] = new JsonArray(group.First, group.Second),
                ["outputs"] = outputs,
                ["effectiveOutput"] = group.Outputs[0],
            });
        }
        return result;
    }

    private static (string First, string Second) CanonicalPair(string first, string second)
    {
        first = first.ToLowerInvariant();
        second = second.ToLowerInvariant();
        return string.CompareOrdinal(first, second) <= 0 ? (first, second) : (second, first);
    }

    private static string ParseOutput(string path, int lineNumber, string value)
    {
        value = value.Trim().TrimEnd(';').Trim();
        if (value.Length == 0)
            throw Error(path, lineNumber, "expected an output value");
        if (value[0] == '"')
            return ParseQuotedString(path, lineNumber, value);
        if (value.Any(char.IsWhiteSpace))
            throw Error(path, lineNumber, "outputs containing whitespace must be quoted");
        return value;
    }

    private static List<string> ParseOutputRow(string path, int lineNumber, string value)
    {
        value = value.Trim().TrimEnd(';').Trim();
        var outputs = new List<string>();
        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && (char.IsWhiteSpace(value[index]) || value[index] == ','))
                index++;
            if (index >= value.Length)
                break;

            if (value[index] == '"')
            {
                var start = index++;
                var escaped = false;
                var closed = false;
                while (index < value.Length)
                {
                    var ch = value[index++];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (ch == '"')
                    {
                        closed = true;
                        break;
                    }
                }
                if (!closed)
                    throw Error(path, lineNumber, "unterminated quoted output");
                outputs.Add(ParseQuotedString(path, lineNumber, value[start..index]));
                continue;
            }

            var tokenStart = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]))
                index++;
            var token = value[tokenStart..index];
            if (token.Length > 0)
                outputs.Add(token);
        }

        if (outputs.Count == 0)
            throw Error(path, lineNumber, "expected one or more output values after 'row'");
        return outputs;
    }

    private static string ParseQuotedString(string path, int lineNumber, string value)
    {
        value = value.Trim().TrimEnd(';').Trim();
        try
        {
            return JsonSerializer.Deserialize<string>(value)
                ?? throw Error(path, lineNumber, "expected a quoted string");
        }
        catch (JsonException exception)
        {
            throw Error(path, lineNumber, $"expected a quoted string: {exception.Message}");
        }
    }

    private static string[] ParseQuotedStringList(string path, int lineNumber, string value)
    {
        value = value.Trim().TrimEnd(';').Trim();
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>($"[{value}]");
            if (parsed is null || parsed.Length == 0)
                throw Error(path, lineNumber, "expected one or more quoted strings");
            return parsed;
        }
        catch (JsonException exception)
        {
            throw Error(path, lineNumber, $"expected comma-separated quoted strings: {exception.Message}");
        }
    }

    private static string StripComment(string line)
    {
        var inString = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (ch == '\\')
                    escaped = true;
                else if (ch == '"')
                    inString = false;
            }
            else if (ch == '"')
            {
                inString = true;
            }
            else if (ch == '/' && index + 1 < line.Length && line[index + 1] == '/')
            {
                return line[..index];
            }
        }
        return line;
    }

    private static Match Match(string input, string pattern) =>
        Regex.Match(input, pattern, RegexOptions.CultureInvariant);

    private static IKeydDslException Error(string path, int lineNumber, string message) =>
        new(path, lineNumber, message);
}