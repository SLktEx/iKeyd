using System.Text.Json;
using System.Text.RegularExpressions;
using iKeyd.Core.State;

internal static class IKeydStateDslParser
{
    private const string Ident = "[A-Za-z0-9_]+";

    public static StateDslExtraction Extract(string text, string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(text);
        var path = string.IsNullOrWhiteSpace(sourcePath) ? "<memory>" : sourcePath;
        var lines = text.Split('\n');
        var foundStart = -1;
        var foundEnd = -1;
        var depth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index].TrimEnd('\r')).Trim();
            if (depth == 0 && Regex.IsMatch(line, @"^state\s*\{$", RegexOptions.CultureInvariant))
            {
                if (foundStart >= 0)
                    throw Error(path, index + 1, "only one state block is allowed");

                foundStart = index;
                var blockDepth = BraceDelta(line);
                var cursor = index + 1;
                while (cursor < lines.Length && blockDepth > 0)
                {
                    blockDepth += BraceDelta(StripComment(lines[cursor]));
                    cursor++;
                }
                if (blockDepth != 0)
                    throw Error(path, index + 1, "unclosed state block");

                foundEnd = cursor;
                index = cursor - 1;
                depth = 0;
                continue;
            }

            depth += BraceDelta(line);
            if (depth < 0)
                depth = 0;
        }

        if (foundStart < 0)
            return new StateDslExtraction(text, RuntimeStateProfile.Empty);

        var profile = ParseState(lines, foundStart, foundEnd, path);
        for (var index = foundStart; index < foundEnd; index++)
            lines[index] = string.Empty;
        return new StateDslExtraction(string.Join('\n', lines), profile);
    }

    private static RuntimeStateProfile ParseState(string[] lines, int start, int end, string path)
    {
        var fields = new List<RuntimeStateFieldProfile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = start + 1; index < end - 1; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index].TrimEnd('\r')).Trim();
            if (line.Length == 0)
                continue;
            if (line == "}")
                throw Error(path, lineNumber, "state fields may not contain nested blocks");

            var match = Regex.Match(
                line,
                $@"^({Ident})\s*:\s*(bool|string)\s*=\s*(.+?)\s*;?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                throw Error(path, lineNumber, $"invalid state declaration: {line}");

            var name = match.Groups[1].Value;
            var type = match.Groups[2].Value.ToLowerInvariant();
            var raw = match.Groups[3].Value.Trim();
            if (!seen.Add(name))
                throw Error(path, lineNumber, $"duplicate state field '{name}'");

            if (type == "bool")
            {
                if (!bool.TryParse(raw, out var value))
                    throw Error(path, lineNumber, $"state.{name} is bool and must default to true or false");
                fields.Add(RuntimeStateFieldProfile.Bool(name, value));
                continue;
            }

            fields.Add(RuntimeStateFieldProfile.String(name, ParseJsonString(path, lineNumber, raw)));
        }

        return new RuntimeStateProfile(fields);
    }

    private static string ParseJsonString(string path, int lineNumber, string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.String)
                throw Error(path, lineNumber, "string state default must be a quoted string");
            return document.RootElement.GetString() ?? string.Empty;
        }
        catch (JsonException exception)
        {
            throw Error(path, lineNumber, $"string state default must be a quoted string: {exception.Message}");
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
            else if (character == '"')
            {
                inString = true;
            }
            else if (character == '/' && index + 1 < line.Length && line[index + 1] == '/')
            {
                return line[..index];
            }
        }
        return line;
    }

    private static int BraceDelta(string line)
        => line.Count(character => character == '{') - line.Count(character => character == '}');

    private static InvalidDataException Error(string path, int line, string message)
        => new($"{path}:{line}: {message}");
}

internal readonly record struct StateDslExtraction(string SourceWithoutState, RuntimeStateProfile Profile);
