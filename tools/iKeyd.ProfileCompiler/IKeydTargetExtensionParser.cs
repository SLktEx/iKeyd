using System.Text.Json;
using System.Text.RegularExpressions;

internal static class IKeydTargetExtensionParser
{
    private const string TargetNamePattern = "[A-Za-z0-9_-]+";
    private const string OptionNamePattern = "[A-Za-z0-9_.-]+";
    private const string NativeKindPattern = "[A-Za-z0-9_.-]+";

    public static TargetExtensionExtraction Extract(string text, string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(text);
        var path = string.IsNullOrWhiteSpace(sourcePath) ? "<memory>" : sourcePath;
        var lines = text.Split('\n');
        var output = lines.ToArray();
        var extensions = new List<TargetExtensionIr>();
        var topLevelDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var stripped = StripComment(lines[index].TrimEnd('\r')).Trim();
            if (topLevelDepth == 0)
            {
                var header = Regex.Match(
                    stripped,
                    $@"^target\s+({TargetNamePattern})\s*\{{$",
                    RegexOptions.CultureInvariant);
                if (header.Success)
                {
                    var selector = ParseSelector(path, index + 1, header.Groups[1].Value);
                    var start = index;
                    var depth = BraceDeltaOutsideString(stripped);
                    var body = new List<SourceLine>();
                    var cursor = index + 1;
                    while (cursor < lines.Length && depth > 0)
                    {
                        var current = StripComment(lines[cursor].TrimEnd('\r'));
                        depth += BraceDeltaOutsideString(current);
                        if (depth > 0)
                            body.Add(new SourceLine(cursor + 1, current));
                        cursor++;
                    }
                    if (depth != 0)
                        throw Error(path, index + 1, $"unclosed target block '{header.Groups[1].Value}'");

                    for (var remove = start; remove < cursor; remove++)
                        output[remove] = string.Empty;

                    extensions.Add(ParseBlock(path, selector, index + 1, body));
                    index = cursor - 1;
                    continue;
                }
            }

            topLevelDepth += BraceDeltaOutsideString(stripped);
            if (topLevelDepth < 0)
                topLevelDepth = 0;
        }

        return new TargetExtensionExtraction(string.Join('\n', output), extensions);
    }

    private static TargetExtensionIr ParseBlock(
        string path,
        TargetSelector selector,
        int headerLine,
        IReadOnlyList<SourceLine> body)
    {
        var requirements = new List<TargetCapabilityRequirementIr>();
        var requirementNames = new HashSet<BehaviorCapability>();
        var options = new List<TargetOptionIr>();
        var optionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nativeFragments = new List<NativeTargetFragmentIr>();

        foreach (var sourceLine in body)
        {
            var line = StripComment(sourceLine.Text).Trim();
            if (line.Length == 0)
                continue;

            var require = Regex.Match(
                line,
                $@"^require\s+({OptionNamePattern})\s*;?$",
                RegexOptions.CultureInvariant);
            if (require.Success)
            {
                var capability = ParseCapability(path, sourceLine.Line, require.Groups[1].Value);
                if (!requirementNames.Add(capability))
                    throw Error(path, sourceLine.Line, $"duplicate target capability requirement '{require.Groups[1].Value}'");
                requirements.Add(new TargetCapabilityRequirementIr(
                    capability,
                    new SourceLocation(path, sourceLine.Line, ColumnOf(sourceLine.Text, "require"))));
                continue;
            }

            var option = Regex.Match(
                line,
                $@"^option\s+({OptionNamePattern})\s*=\s*(.+)$",
                RegexOptions.CultureInvariant);
            if (option.Success)
            {
                var name = option.Groups[1].Value;
                if (!optionNames.Add(name))
                    throw Error(path, sourceLine.Line, $"duplicate target option '{name}' in the same block");
                options.Add(new TargetOptionIr(
                    name,
                    ParseScalar(path, sourceLine.Line, option.Groups[2].Value),
                    new SourceLocation(path, sourceLine.Line, ColumnOf(sourceLine.Text, "option"))));
                continue;
            }

            var native = Regex.Match(
                line,
                $@"^native\s+({NativeKindPattern})\s*=\s*(.+)$",
                RegexOptions.CultureInvariant);
            if (native.Success)
            {
                nativeFragments.Add(new NativeTargetFragmentIr(
                    native.Groups[1].Value,
                    ParseQuotedString(path, sourceLine.Line, native.Groups[2].Value),
                    new SourceLocation(path, sourceLine.Line, ColumnOf(sourceLine.Text, "native"))));
                continue;
            }

            throw Error(
                path,
                sourceLine.Line,
                "target blocks may contain only 'require', 'option', and 'native' declarations; portable bindings cannot be overridden here");
        }

        return new TargetExtensionIr(
            selector,
            requirements,
            options,
            nativeFragments,
            new SourceLocation(path, headerLine, 1));
    }

    private static TargetSelector ParseSelector(string path, int line, string raw)
        => raw.ToLowerInvariant() switch
        {
            "ikeyd" => TargetSelector.IKeyd,
            "ikeyd-csharp" => TargetSelector.IKeydCSharp,
            "ikeyd-rust" => TargetSelector.IKeydRust,
            "qmk" => TargetSelector.Qmk,
            "zmk" => TargetSelector.Zmk,
            "windows" => throw Error(
                path,
                line,
                "'windows' is a host platform, not a compiler backend target; use target ikeyd plus host/platform conditions instead"),
            _ => throw Error(path, line, $"unknown target '{raw}'"),
        };

    private static BehaviorCapability ParseCapability(string path, int line, string raw)
        => raw.ToLowerInvariant() switch
        {
            "key-output" => BehaviorCapability.KeyOutput,
            "layer" => BehaviorCapability.Layer,
            "combo" => BehaviorCapability.Combo,
            "hold-tap" => BehaviorCapability.HoldTap,
            "mod-tap" => BehaviorCapability.ModTap,
            "layer-tap" => BehaviorCapability.LayerTap,
            "macro" => BehaviorCapability.Macro,
            "unicode" => BehaviorCapability.Unicode,
            "pointer" => BehaviorCapability.Pointer,
            "host-command" => BehaviorCapability.HostCommand,
            "clipboard" => BehaviorCapability.Clipboard,
            "app-context" => BehaviorCapability.AppContext,
            _ => throw Error(path, line, $"unknown behavior capability '{raw}'"),
        };

    private static string ParseScalar(string path, int line, string raw)
    {
        var value = TrimTerminator(raw);
        if (value.Length == 0)
            throw Error(path, line, "target option value must not be empty");
        return value.StartsWith('"')
            ? ParseQuotedString(path, line, value)
            : value;
    }

    private static string ParseQuotedString(string path, int line, string raw)
    {
        var value = TrimTerminator(raw);
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.String)
                throw Error(path, line, "native target fragment must be a quoted string");
            return document.RootElement.GetString() ?? string.Empty;
        }
        catch (JsonException exception)
        {
            throw Error(path, line, $"expected a quoted string: {exception.Message}");
        }
    }

    private static int ColumnOf(string text, string token)
    {
        var index = text.IndexOf(token, StringComparison.Ordinal);
        return index < 0 ? 1 : index + 1;
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

    private static int BraceDeltaOutsideString(string line)
    {
        var delta = 0;
        var inString = false;
        var escaped = false;
        foreach (var character in line)
        {
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == '"')
                    inString = false;
                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }
            if (character == '{')
                delta++;
            else if (character == '}')
                delta--;
        }
        return delta;
    }

    private static string TrimTerminator(string value)
        => value.Trim().TrimEnd(';').Trim();

    private static InvalidDataException Error(string path, int line, string message)
        => new($"{path}:{line}: {message}");

    private readonly record struct SourceLine(int Line, string Text);
}

internal readonly record struct TargetExtensionExtraction(
    string SourceWithoutTargetBlocks,
    IReadOnlyList<TargetExtensionIr> Extensions);
