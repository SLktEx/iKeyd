using System.Text.Json;
using System.Text.RegularExpressions;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

internal static class IKeydDslParser
{
    private const string IdentPattern = "[A-Za-z0-9_]+";
    private const string KeyRefPattern = IdentPattern + @"(?:\[\s*\d+\s*,\s*\d+\s*\])?";
    private const int MaxBehaviorStatementDepth = 32;

    public static AutomationProfile Parse(string text, string sourcePath = "<memory>")
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("iKeyd DSL source must not be empty.");

        var path = string.IsNullOrWhiteSpace(sourcePath) ? "<memory>" : sourcePath;
        var extracted = ExtractUserBehaviors(text, path);
        text = extracted.SourceWithoutDefinitions;

        var layouts = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
        var keymaps = new List<KeymapBuilder>();
        var keymapsByName = new Dictionary<string, KeymapBuilder>(StringComparer.OrdinalIgnoreCase);
        ClipboardBuilder? clipboard = null;

        BlockKind block = BlockKind.None;
        string? blockName = null;
        PendingBehaviorBuilder? pendingBehavior = null;
        var sawProfile = false;
        var sawClipboard = false;
        int? chordWindowMs = null;
        var startupMode = "S";

        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index].TrimEnd('\r')).Trim();
            if (line.Length == 0)
                continue;

            if (line == "}")
            {
                if (block == BlockKind.None)
                    throw Error(path, lineNumber, "unexpected '}'");

                if (block == BlockKind.BehaviorOptions)
                {
                    if (pendingBehavior is null)
                        throw Error(path, lineNumber, "internal behavior-options state is missing");
                    pendingBehavior.Keymap.Behaviors.Add(new BehaviorMappingProfile(
                        pendingBehavior.Key,
                        new BehaviorInvocationProfile(
                            pendingBehavior.Name,
                            pendingBehavior.Arguments,
                            pendingBehavior.Options)));
                    block = BlockKind.Keymap;
                    blockName = pendingBehavior.Keymap.Name;
                    pendingBehavior = null;
                }
                else
                {
                    block = BlockKind.None;
                    blockName = null;
                }
                continue;
            }

            if (block == BlockKind.None)
            {
                var profileMatch = Regex.Match(line, $@"^profile\s+({IdentPattern})\s*\{{$", RegexOptions.CultureInvariant);
                if (profileMatch.Success)
                {
                    if (sawProfile)
                        throw Error(path, lineNumber, "only one profile block is allowed");
                    sawProfile = true;
                    block = BlockKind.Profile;
                    blockName = profileMatch.Groups[1].Value;
                    continue;
                }

                if (Regex.IsMatch(line, @"^clipboard\s*\{$", RegexOptions.CultureInvariant))
                {
                    if (sawClipboard)
                        throw Error(path, lineNumber, "only one clipboard block is allowed");
                    sawClipboard = true;
                    clipboard = new ClipboardBuilder();
                    block = BlockKind.Clipboard;
                    continue;
                }

                var layoutMatch = Regex.Match(line, $@"^layout\s+({IdentPattern})\s*\{{$", RegexOptions.CultureInvariant);
                if (layoutMatch.Success)
                {
                    var name = layoutMatch.Groups[1].Value;
                    if (!layouts.TryAdd(name, []))
                        throw Error(path, lineNumber, $"duplicate layout '{name}'");
                    block = BlockKind.Layout;
                    blockName = name;
                    continue;
                }

                var keymapMatch = Regex.Match(line, $@"^keymap\s+({IdentPattern})\s*\{{$", RegexOptions.CultureInvariant);
                if (keymapMatch.Success)
                {
                    var name = keymapMatch.Groups[1].Value;
                    var builder = new KeymapBuilder(name);
                    if (!keymapsByName.TryAdd(name, builder))
                        throw Error(path, lineNumber, $"duplicate keymap '{name}'");
                    keymaps.Add(builder);
                    block = BlockKind.Keymap;
                    blockName = name;
                    continue;
                }

                if (Regex.IsMatch(line, @"^quirks\s*\{$", RegexOptions.CultureInvariant))
                {
                    block = BlockKind.Quirks;
                    continue;
                }

                throw Error(path, lineNumber, $"unexpected top-level statement: {line}");
            }

            switch (block)
            {
                case BlockKind.Profile:
                {
                    var runtime = Regex.Match(line, @"^runtime\s*=\s*(.+)$", RegexOptions.CultureInvariant);
                    if (runtime.Success)
                    {
                        _ = ParseJsonString(path, lineNumber, runtime.Groups[1].Value);
                        continue;
                    }

                    var executableLines = Regex.Match(line, @"^executable_lines\s*=\s*(\d+)\s*;?$", RegexOptions.CultureInvariant);
                    if (executableLines.Success)
                        continue;

                    var chordWindow = Regex.Match(line, @"^chord_window\s*=\s*(\d+)\s*ms\s*;?$", RegexOptions.CultureInvariant);
                    if (chordWindow.Success)
                    {
                        chordWindowMs = int.Parse(chordWindow.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        continue;
                    }

                    var startup = Regex.Match(line, $@"^startup_mode\s*=\s*({IdentPattern})\s*;?$", RegexOptions.CultureInvariant);
                    if (startup.Success)
                    {
                        startupMode = startup.Groups[1].Value;
                        continue;
                    }

                    throw Error(path, lineNumber, $"unknown profile setting: {line}");
                }

                case BlockKind.Clipboard:
                    if (clipboard is null)
                        throw Error(path, lineNumber, "internal clipboard state is missing");
                    ParseClipboardSetting(clipboard, path, lineNumber, line);
                    continue;

                case BlockKind.Layout:
                {
                    if (blockName is null)
                        throw Error(path, lineNumber, "internal layout state is missing");
                    var rowMatch = Regex.Match(line, @"^row\s+(.+)$", RegexOptions.CultureInvariant);
                    if (!rowMatch.Success)
                        throw Error(path, lineNumber, $"unknown layout statement: {line}");

                    var row = ParseLayoutRow(path, lineNumber, rowMatch.Groups[1].Value);
                    var layout = layouts[blockName];
                    var existing = new HashSet<string>(layout.SelectMany(item => item), StringComparer.OrdinalIgnoreCase);
                    foreach (var key in row)
                    {
                        if (!existing.Add(key))
                            throw Error(path, lineNumber, $"duplicate key '{key}' in layout '{blockName}'");
                    }
                    layout.Add(row);
                    continue;
                }

                case BlockKind.Keymap:
                {
                    if (blockName is null || !keymapsByName.TryGetValue(blockName, out var keymap))
                        throw Error(path, lineNumber, "internal keymap state is missing");

                    var combo = Regex.Match(
                        line,
                        $@"^combo\s+({KeyRefPattern})\s*\+\s*({KeyRefPattern})\s*=\s*(.+)$",
                        RegexOptions.CultureInvariant);
                    if (combo.Success)
                    {
                        var first = ResolveKeyRef(path, lineNumber, combo.Groups[1].Value, layouts);
                        var second = ResolveKeyRef(path, lineNumber, combo.Groups[2].Value, layouts);
                        var output = ParseJsonString(path, lineNumber, combo.Groups[3].Value);
                        keymap.Chords.Add(new ChordMapping<string>(first, second, output));
                        continue;
                    }

                    var optionBlock = Regex.Match(
                        line,
                        $@"^({KeyRefPattern})\s*=\s*(.+?)\s*\{{$",
                        RegexOptions.CultureInvariant);
                    if (optionBlock.Success)
                    {
                        var key = ResolveKeyRef(path, lineNumber, optionBlock.Groups[1].Value, layouts);
                        keymap.ReserveKey(path, lineNumber, key);
                        var invocation = ParseBehaviorInvocation(path, lineNumber, optionBlock.Groups[2].Value)
                            ?? throw Error(path, lineNumber, "option blocks are only valid for behavior invocations");
                        pendingBehavior = new PendingBehaviorBuilder(
                            keymap,
                            key,
                            invocation.Name,
                            invocation.Arguments);
                        block = BlockKind.BehaviorOptions;
                        continue;
                    }

                    var mapping = Regex.Match(
                        line,
                        $@"^({KeyRefPattern})\s*=\s*(.+)$",
                        RegexOptions.CultureInvariant);
                    if (!mapping.Success)
                        throw Error(path, lineNumber, $"unknown keymap statement: {line}");

                    var resolvedKey = ResolveKeyRef(path, lineNumber, mapping.Groups[1].Value, layouts);
                    keymap.ReserveKey(path, lineNumber, resolvedKey);
                    var behavior = ParseBehaviorInvocation(path, lineNumber, mapping.Groups[2].Value);
                    if (behavior is not null)
                    {
                        keymap.Behaviors.Add(new BehaviorMappingProfile(
                            resolvedKey,
                            new BehaviorInvocationProfile(behavior.Value.Name, behavior.Value.Arguments)));
                    }
                    else
                    {
                        keymap.Singles.Add(new SingleMapping<string>(
                            resolvedKey,
                            ParseJsonString(path, lineNumber, mapping.Groups[2].Value)));
                    }
                    continue;
                }

                case BlockKind.BehaviorOptions:
                {
                    if (pendingBehavior is null)
                        throw Error(path, lineNumber, "internal behavior-options state is missing");
                    var option = Regex.Match(line, $@"^({IdentPattern})\s*=\s*(.+)$", RegexOptions.CultureInvariant);
                    if (!option.Success)
                        throw Error(path, lineNumber, $"unknown behavior option statement: {line}");
                    var name = option.Groups[1].Value;
                    if (pendingBehavior.Options.Any(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase)))
                        throw Error(path, lineNumber, $"duplicate behavior option '{name}'");
                    pendingBehavior.Options.Add(new KeyValuePair<string, string>(
                        name,
                        ParseBehaviorOptionValue(path, lineNumber, option.Groups[2].Value)));
                    continue;
                }

                case BlockKind.Quirks:
                {
                    var duplicateFlag = Regex.Match(
                        line,
                        $@"^duplicate_flag\s+({IdentPattern})\s*=\s*(.+)$",
                        RegexOptions.CultureInvariant);
                    if (!duplicateFlag.Success)
                        throw Error(path, lineNumber, $"unknown quirks statement: {line}");
                    _ = ParseStringList(path, lineNumber, duplicateFlag.Groups[2].Value);
                    continue;
                }

                default:
                    throw Error(path, lineNumber, "invalid parser block state");
            }
        }

        if (block != BlockKind.None)
            throw Error(path, lines.Length, $"unclosed {block.ToString().ToLowerInvariant()} block");
        if (!sawProfile)
            throw Error(path, 1, "profile block is required");
        if (chordWindowMs is null)
            throw Error(path, 1, "profile.chord_window is required");
        if (keymaps.Count == 0)
            throw Error(path, 1, "at least one keymap is required");

        return new AutomationProfile(
            chordWindowMs.Value,
            keymaps.Select(item => item.Build()),
            startupMode,
            behaviorDefinitions: extracted.Definitions,
            clipboard: clipboard?.Build());
    }

    private static void ParseClipboardSetting(
        ClipboardBuilder clipboard,
        string path,
        int lineNumber,
        string line)
    {
        var match = Regex.Match(line, $@"^({IdentPattern})\s*=\s*(.+)$", RegexOptions.CultureInvariant);
        if (!match.Success)
            throw Error(path, lineNumber, $"unknown clipboard setting: {line}");

        var setting = match.Groups[1].Value;
        var raw = match.Groups[2].Value;
        if (!clipboard.Seen.Add(setting))
            throw Error(path, lineNumber, $"duplicate clipboard setting '{setting}'");

        switch (setting)
        {
            case "history":
                clipboard.History = ParseBool(path, lineNumber, raw, setting);
                return;
            case "max_items":
            {
                var token = TrimTerminator(raw);
                if (!int.TryParse(token, out var value) || value <= 0)
                    throw Error(path, lineNumber, "clipboard.max_items must be a positive integer");
                clipboard.MaxItems = value;
                return;
            }
            case "persist":
                clipboard.Persist = ParseBool(path, lineNumber, raw, setting);
                return;
            case "images":
                clipboard.Images = ParseBool(path, lineNumber, raw, setting);
                return;
            case "encryption":
            {
                var value = ParseClipboardToken(path, lineNumber, raw, setting).ToLowerInvariant();
                if (value != "user")
                    throw Error(path, lineNumber, "clipboard.encryption currently supports only 'user'");
                clipboard.Encryption = value;
                return;
            }
            case "cipher":
            {
                var value = ParseClipboardToken(path, lineNumber, raw, setting)
                    .ToLowerInvariant()
                    .Replace('_', '-');
                if (value is not ("auto" or "chacha20-poly1305"))
                    throw Error(path, lineNumber, "clipboard.cipher currently supports 'auto' or 'chacha20_poly1305'");
                clipboard.Cipher = value;
                return;
            }
            case "directory":
            {
                var value = ParseJsonString(path, lineNumber, raw);
                if (string.IsNullOrWhiteSpace(value))
                    throw Error(path, lineNumber, "clipboard.directory must not be empty");
                clipboard.Directory = value;
                return;
            }
            default:
                throw Error(path, lineNumber, $"unknown clipboard setting '{setting}'");
        }
    }

    private static ExtractedBehaviors ExtractUserBehaviors(string text, string path)
    {
        var lines = text.Split('\n');
        var output = lines.ToArray();
        var definitions = new List<UserBehaviorDefinitionProfile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < lines.Length;)
        {
            var line = StripComment(lines[index].TrimEnd('\r')).Trim();
            var header = Regex.Match(
                line,
                $@"^behavior\s+({IdentPattern})\s*\(([^)]*)\)\s*\{{$",
                RegexOptions.CultureInvariant);
            if (!header.Success)
            {
                index++;
                continue;
            }

            var name = header.Groups[1].Value;
            if (!names.Add(name))
                throw Error(path, index + 1, $"duplicate behavior definition '{name}'");
            var parameters = ParseIdentifierList(path, index + 1, header.Groups[2].Value, "behavior parameter");

            var depth = BraceDelta(line);
            var body = new List<SourceLine>();
            output[index] = string.Empty;
            var cursor = index + 1;
            while (cursor < lines.Length && depth > 0)
            {
                var current = StripComment(lines[cursor].TrimEnd('\r'));
                depth += BraceDelta(current);
                output[cursor] = string.Empty;
                if (depth > 0)
                    body.Add(new SourceLine(cursor + 1, current));
                cursor++;
            }
            if (depth != 0)
                throw Error(path, index + 1, $"unclosed behavior '{name}'");

            definitions.Add(ParseUserBehaviorDefinition(path, name, parameters, body));
            index = cursor;
        }

        return new ExtractedBehaviors(string.Join('\n', output), definitions);
    }

    private static UserBehaviorDefinitionProfile ParseUserBehaviorDefinition(
        string path,
        string name,
        IReadOnlyList<string> parameters,
        IReadOnlyList<SourceLine> rawLines)
    {
        var tokens = NormalizeBehaviorTokens(rawLines);
        var locals = new List<UserBehaviorLocalProfile>();
        var localNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handlers = new List<UserBehaviorHandlerProfile>();
        var handlerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];
            var line = TrimTerminator(token.Text);
            var local = Regex.Match(
                line,
                $@"^var\s+({IdentPattern})\s*:\s*bool\s*=\s*(true|false)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (local.Success)
            {
                var localName = local.Groups[1].Value;
                if (!localNames.Add(localName) || parameters.Contains(localName, StringComparer.OrdinalIgnoreCase))
                    throw Error(path, token.Line, $"duplicate/conflicting behavior local '{localName}'");
                locals.Add(new UserBehaviorLocalProfile(
                    localName,
                    local.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase)));
                index++;
                continue;
            }

            var eventName = string.Empty;
            IReadOnlyList<string> handlerParameters = [];
            var handler = Regex.Match(line, @"^on_(press|hold|tap|release)\s*\{$", RegexOptions.CultureInvariant);
            if (handler.Success)
            {
                eventName = handler.Groups[1].Value;
            }
            else
            {
                var interrupt = Regex.Match(
                    line,
                    $@"^on_interrupt\s*\(\s*({IdentPattern})\s*\)\s*\{{$",
                    RegexOptions.CultureInvariant);
                if (interrupt.Success)
                {
                    eventName = "interrupt";
                    handlerParameters = [interrupt.Groups[1].Value];
                }
            }

            if (eventName.Length != 0)
            {
                if (!handlerNames.Add(eventName))
                    throw Error(path, token.Line, $"duplicate behavior handler 'on_{eventName}'");
                var parsed = ParseBehaviorStatements(path, tokens, index + 1, 0);
                handlers.Add(new UserBehaviorHandlerProfile(eventName, handlerParameters, parsed.Statements));
                index = parsed.NextIndex;
                continue;
            }

            throw Error(path, token.Line, $"unsupported behavior declaration: {line}");
        }

        return new UserBehaviorDefinitionProfile(name, parameters, locals, handlers);
    }

    private static StatementParseResult ParseBehaviorStatements(
        string path,
        IReadOnlyList<SourceLine> tokens,
        int index,
        int depth)
    {
        if (depth > MaxBehaviorStatementDepth)
        {
            var line = index < tokens.Count ? tokens[index].Line : 1;
            throw Error(path, line, "behavior statement nesting is too deep");
        }

        var statements = new List<UserBehaviorStatementProfile>();
        while (index < tokens.Count)
        {
            var token = tokens[index];
            var line = TrimTerminator(token.Text);
            if (line == "}")
                return new StatementParseResult(statements, index + 1);

            var conditional = Regex.Match(
                line,
                $@"^if\s+({IdentPattern})\s*\{{$",
                RegexOptions.CultureInvariant);
            if (conditional.Success)
            {
                var thenResult = ParseBehaviorStatements(path, tokens, index + 1, depth + 1);
                var elseStatements = Array.Empty<UserBehaviorStatementProfile>();
                index = thenResult.NextIndex;
                if (index < tokens.Count && TrimTerminator(tokens[index].Text) == "else {")
                {
                    var elseResult = ParseBehaviorStatements(path, tokens, index + 1, depth + 1);
                    elseStatements = elseResult.Statements.ToArray();
                    index = elseResult.NextIndex;
                }
                statements.Add(new UserBehaviorStatementProfile(
                    "if_bool",
                    condition: conditional.Groups[1].Value,
                    thenStatements: thenResult.Statements,
                    elseStatements: elseStatements));
                continue;
            }

            var assignment = Regex.Match(
                line,
                $@"^({IdentPattern})\s*=\s*(true|false)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (assignment.Success)
            {
                statements.Add(new UserBehaviorStatementProfile(
                    "set_bool",
                    target: assignment.Groups[1].Value,
                    value: assignment.Groups[2].Value.ToLowerInvariant()));
                index++;
                continue;
            }

            var send = Regex.Match(line, $@"^send\s+({IdentPattern})$", RegexOptions.CultureInvariant);
            if (send.Success)
            {
                statements.Add(new UserBehaviorStatementProfile("send", value: send.Groups[1].Value));
                index++;
                continue;
            }

            var action = Regex.Match(
                line,
                $@"^(layer\.on|layer\.off|modifier\.down|modifier\.up)\s*\(\s*({IdentPattern})\s*\)$",
                RegexOptions.CultureInvariant);
            if (action.Success)
            {
                var op = action.Groups[1].Value switch
                {
                    "layer.on" => "layer_on",
                    "layer.off" => "layer_off",
                    "modifier.down" => "modifier_down",
                    "modifier.up" => "modifier_up",
                    _ => throw new UnreachableException(),
                };
                statements.Add(new UserBehaviorStatementProfile(op, value: action.Groups[2].Value));
                index++;
                continue;
            }

            throw Error(path, token.Line, $"unsupported behavior statement: {line}");
        }

        throw Error(path, tokens.Count == 0 ? 1 : tokens[^1].Line, "unclosed behavior statement block");
    }

    private static List<SourceLine> NormalizeBehaviorTokens(IReadOnlyList<SourceLine> rawLines)
    {
        var result = new List<SourceLine>();
        foreach (var raw in rawLines)
        {
            var line = StripComment(raw.Text).Trim();
            if (line.Length == 0)
                continue;
            if (Regex.IsMatch(line, @"^}\s*else\s*\{$", RegexOptions.CultureInvariant))
            {
                result.Add(new SourceLine(raw.Line, "}"));
                result.Add(new SourceLine(raw.Line, "else {"));
            }
            else
            {
                result.Add(new SourceLine(raw.Line, line));
            }
        }
        return result;
    }

    private static (string Name, IReadOnlyList<string> Arguments)? ParseBehaviorInvocation(
        string path,
        int lineNumber,
        string raw)
    {
        var value = TrimTerminator(raw);
        var match = Regex.Match(value, $@"^({IdentPattern})\s*\((.*)\)$", RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        var arguments = ParseIdentifierList(path, lineNumber, match.Groups[2].Value, "behavior argument");
        return (match.Groups[1].Value, arguments);
    }

    private static List<string> ParseIdentifierList(
        string path,
        int lineNumber,
        string raw,
        string kind)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var item in raw.Split(','))
        {
            var token = item.Trim();
            if (!Regex.IsMatch(token, $@"^{IdentPattern}$", RegexOptions.CultureInvariant))
                throw Error(path, lineNumber, $"{kind}s must be identifiers in the current syntax: '{token}'");
            if (result.Contains(token, StringComparer.OrdinalIgnoreCase))
                throw Error(path, lineNumber, $"duplicate {kind} '{token}'");
            result.Add(token);
        }
        return result;
    }

    private static string ResolveKeyRef(
        string path,
        int lineNumber,
        string raw,
        IReadOnlyDictionary<string, List<List<string>>> layouts)
    {
        var value = raw.Trim();
        if (Regex.IsMatch(value, $@"^{IdentPattern}$", RegexOptions.CultureInvariant))
            return value;

        var coordinate = Regex.Match(
            value,
            $@"^({IdentPattern})\[\s*(\d+)\s*,\s*(\d+)\s*\]$",
            RegexOptions.CultureInvariant);
        if (!coordinate.Success)
            throw Error(path, lineNumber, $"invalid key reference '{value}'");

        var layoutName = coordinate.Groups[1].Value;
        var row = int.Parse(coordinate.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var column = int.Parse(coordinate.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (row < 1 || column < 1)
            throw Error(path, lineNumber, $"key positions are 1-based: '{value}'");

        var resolvedLayoutName = layoutName;
        if (layoutName.Equals("POS", StringComparison.OrdinalIgnoreCase) &&
            !layouts.ContainsKey("POS") && layouts.ContainsKey("BASE"))
        {
            resolvedLayoutName = "BASE";
        }

        if (!layouts.TryGetValue(resolvedLayoutName, out var layout))
            throw Error(path, lineNumber, $"unknown layout '{layoutName}' in key reference '{value}'");
        if (row > layout.Count)
            throw Error(path, lineNumber, $"row {row} is out of range for layout '{layoutName}'");
        if (column > layout[row - 1].Count)
            throw Error(path, lineNumber, $"column {column} is out of range for layout '{layoutName}' row {row}");
        return layout[row - 1][column - 1];
    }

    private static List<string> ParseLayoutRow(string path, int lineNumber, string raw)
    {
        var value = TrimTerminator(raw);
        var keys = Regex.Split(value, @"[\s,]+")
            .Where(item => item.Length != 0)
            .ToList();
        if (keys.Count == 0 || keys.Any(key => !Regex.IsMatch(key, $@"^{IdentPattern}$", RegexOptions.CultureInvariant)))
            throw Error(path, lineNumber, "expected one or more key identifiers after 'row'");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
                throw Error(path, lineNumber, $"duplicate key '{key}' in layout row");
        }
        return keys;
    }

    private static string ParseBehaviorOptionValue(string path, int lineNumber, string raw)
    {
        var value = TrimTerminator(raw);
        if (value.Length == 0)
            throw Error(path, lineNumber, "behavior option value must not be empty");
        if (value.StartsWith('"'))
            return ParseJsonString(path, lineNumber, value);
        if (!Regex.IsMatch(value, @"^[-+A-Za-z0-9_.]+$", RegexOptions.CultureInvariant))
            throw Error(path, lineNumber, $"invalid behavior option value '{value}'");
        return value;
    }

    private static string ParseClipboardToken(string path, int lineNumber, string raw, string setting)
    {
        var value = TrimTerminator(raw);
        if (value.StartsWith('"'))
            return ParseJsonString(path, lineNumber, value);
        if (!Regex.IsMatch(value, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant))
            throw Error(path, lineNumber, $"invalid clipboard.{setting} value '{value}'");
        return value;
    }

    private static bool ParseBool(string path, int lineNumber, string raw, string setting)
    {
        return TrimTerminator(raw).ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => throw Error(path, lineNumber, $"clipboard.{setting} must be true or false"),
        };
    }

    private static List<string> ParseStringList(string path, int lineNumber, string raw)
    {
        try
        {
            using var document = JsonDocument.Parse("[" + TrimTerminator(raw) + "]");
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                throw Error(path, lineNumber, "expected one or more quoted strings");
            var result = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw Error(path, lineNumber, "expected one or more quoted strings");
                result.Add(element.GetString()!);
            }
            return result;
        }
        catch (JsonException exception)
        {
            throw Error(path, lineNumber, $"expected comma-separated quoted strings: {exception.Message}");
        }
    }

    private static string ParseJsonString(string path, int lineNumber, string raw)
    {
        var value = TrimTerminator(raw);
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.String)
                throw Error(path, lineNumber, "expected a quoted string");
            return document.RootElement.GetString() ?? string.Empty;
        }
        catch (JsonException exception)
        {
            throw Error(path, lineNumber, $"expected a quoted string: {exception.Message}");
        }
    }

    private static string StripComment(string line)
    {
        var inString = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == '"')
                    inString = false;
            }
            else
            {
                if (character == '"')
                    inString = true;
                else if (character == '/' && index + 1 < line.Length && line[index + 1] == '/')
                    return line[..index];
            }
        }
        return line;
    }

    private static int BraceDelta(string line)
        => line.Count(character => character == '{') - line.Count(character => character == '}');

    private static string TrimTerminator(string value)
        => value.Trim().TrimEnd(';').Trim();

    private static InvalidDataException Error(string path, int line, string message)
        => new($"{path}:{line}: {message}");

    private enum BlockKind
    {
        None,
        Profile,
        Clipboard,
        Layout,
        Keymap,
        Quirks,
        BehaviorOptions,
    }

    private sealed class KeymapBuilder(string name)
    {
        private readonly HashSet<string> _mappedKeys = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;
        public List<SingleMapping<string>> Singles { get; } = [];
        public List<ChordMapping<string>> Chords { get; } = [];
        public List<BehaviorMappingProfile> Behaviors { get; } = [];

        public void ReserveKey(string path, int lineNumber, string key)
        {
            if (!_mappedKeys.Add(key))
                throw Error(path, lineNumber, $"duplicate key mapping '{Name}.{key}'");
        }

        public AutomationKeymapProfile Build()
            => new(Name, Singles, Chords, Behaviors);
    }

    private sealed class PendingBehaviorBuilder(
        KeymapBuilder keymap,
        string key,
        string name,
        IReadOnlyList<string> arguments)
    {
        public KeymapBuilder Keymap { get; } = keymap;
        public string Key { get; } = key;
        public string Name { get; } = name;
        public IReadOnlyList<string> Arguments { get; } = arguments;
        public List<KeyValuePair<string, string>> Options { get; } = [];
    }

    private sealed class ClipboardBuilder
    {
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool History { get; set; } = true;
        public int MaxItems { get; set; } = 20;
        public bool Persist { get; set; } = true;
        public bool Images { get; set; } = true;
        public string Encryption { get; set; } = "user";
        public string Cipher { get; set; } = "auto";
        public string? Directory { get; set; }

        public ClipboardHistoryProfile Build()
            => new(History, MaxItems, Persist, Images, Encryption, Cipher, Directory);
    }

    private readonly record struct SourceLine(int Line, string Text);
    private readonly record struct ExtractedBehaviors(
        string SourceWithoutDefinitions,
        IReadOnlyList<UserBehaviorDefinitionProfile> Definitions);
    private readonly record struct StatementParseResult(
        IReadOnlyList<UserBehaviorStatementProfile> Statements,
        int NextIndex);
}
