using System.Text.Json;
using System.Text.RegularExpressions;
using iKeyd.Core.Configuration;
using iKeyd.Core.State;

internal sealed class IKeydSharedStateBehaviorSyntax
{
    private const string Ident = "[A-Za-z0-9_]+";
    private readonly IReadOnlyDictionary<string, RewriteOperation> _operations;

    private IKeydSharedStateBehaviorSyntax(string source, IReadOnlyDictionary<string, RewriteOperation> operations)
    {
        Source = source;
        _operations = operations;
    }

    public string Source { get; }

    public static IKeydSharedStateBehaviorSyntax Rewrite(
        string source,
        RuntimeStateProfile stateProfile,
        string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stateProfile);
        var path = string.IsNullOrWhiteSpace(sourcePath) ? "<memory>" : sourcePath;
        var lines = source.Split('\n');
        var operations = new Dictionary<string, RewriteOperation>(StringComparer.OrdinalIgnoreCase);
        var inBehavior = false;
        var depth = 0;
        var sequence = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index].TrimEnd('\r');
            var semantic = StripComment(rawLine).Trim();

            if (!inBehavior)
            {
                if (Regex.IsMatch(
                    semantic,
                    $@"^behavior\s+{Ident}\s*\([^)]*\)\s*\{{$",
                    RegexOptions.CultureInvariant))
                {
                    inBehavior = true;
                    depth = BraceDelta(semantic);
                }
                continue;
            }

            var lineNumber = index + 1;
            var indent = rawLine[..(rawLine.Length - rawLine.TrimStart().Length)];

            var set = Regex.Match(
                semantic,
                $@"^state\.set\(\s*(?:state\.)?({Ident})\s*,\s*(.+?)\s*\)\s*;?$",
                RegexOptions.CultureInvariant);
            if (set.Success)
            {
                var field = GetField(stateProfile, set.Groups[1].Value, path, lineNumber);
                var value = ParseTypedValue(field, set.Groups[2].Value, path, lineNumber);
                var token = Token(++sequence);
                operations.Add(token, new RewriteOperation(RewriteKind.Set, field.Name, value));
                lines[index] = indent + "send " + token;
            }
            else
            {
                var toggle = Regex.Match(
                    semantic,
                    $@"^state\.toggle\(\s*(?:state\.)?({Ident})\s*\)\s*;?$",
                    RegexOptions.CultureInvariant);
                if (toggle.Success)
                {
                    var field = GetField(stateProfile, toggle.Groups[1].Value, path, lineNumber);
                    if (field.Type != RuntimeStateType.Bool)
                        throw Error(path, lineNumber, $"state.{field.Name} is not bool and cannot be toggled");
                    var token = Token(++sequence);
                    operations.Add(token, new RewriteOperation(RewriteKind.Toggle, field.Name, null));
                    lines[index] = indent + "send " + token;
                }
                else
                {
                    var condition = Regex.Match(
                        semantic,
                        $@"^if\s+state\.({Ident})\s*(==|!=)\s*(.+?)\s*\{{$",
                        RegexOptions.CultureInvariant);
                    if (condition.Success)
                    {
                        var field = GetField(stateProfile, condition.Groups[1].Value, path, lineNumber);
                        var value = ParseTypedValue(field, condition.Groups[3].Value, path, lineNumber);
                        var token = Token(++sequence);
                        operations.Add(
                            token,
                            new RewriteOperation(
                                condition.Groups[2].Value == "==" ? RewriteKind.IfEquals : RewriteKind.IfNotEquals,
                                field.Name,
                                value));
                        lines[index] = indent + "if " + token + " {";
                    }
                }
            }

            depth += BraceDelta(semantic);
            if (depth <= 0)
            {
                inBehavior = false;
                depth = 0;
            }
        }

        return new IKeydSharedStateBehaviorSyntax(string.Join('\n', lines), operations);
    }

    public IReadOnlyList<UserBehaviorDefinitionProfile> Apply(
        IEnumerable<UserBehaviorDefinitionProfile> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return definitions.Select(RewriteDefinition).ToArray();
    }

    private UserBehaviorDefinitionProfile RewriteDefinition(UserBehaviorDefinitionProfile definition)
        => new(
            definition.Name,
            definition.Parameters,
            definition.Locals,
            definition.Handlers.Select(handler => new UserBehaviorHandlerProfile(
                handler.Event,
                handler.Parameters,
                RewriteStatements(handler.Statements))));

    private IReadOnlyList<UserBehaviorStatementProfile> RewriteStatements(
        IReadOnlyList<UserBehaviorStatementProfile> statements)
    {
        var result = new List<UserBehaviorStatementProfile>(statements.Count);
        foreach (var statement in statements)
        {
            if (statement.Op.Equals("send", StringComparison.OrdinalIgnoreCase) &&
                statement.Value is not null &&
                _operations.TryGetValue(statement.Value, out var action))
            {
                result.Add(action.Kind switch
                {
                    RewriteKind.Set => new UserBehaviorStatementProfile(
                        "state_set",
                        target: action.Field,
                        value: action.Value),
                    RewriteKind.Toggle => new UserBehaviorStatementProfile(
                        "state_toggle",
                        target: action.Field),
                    _ => throw new InvalidDataException($"State condition token '{statement.Value}' was used as an action.")
                });
                continue;
            }

            if (statement.Op.Equals("if_bool", StringComparison.OrdinalIgnoreCase) &&
                statement.Condition is not null &&
                _operations.TryGetValue(statement.Condition, out var condition))
            {
                if (condition.Kind is not (RewriteKind.IfEquals or RewriteKind.IfNotEquals))
                    throw new InvalidDataException($"State action token '{statement.Condition}' was used as a condition.");
                result.Add(new UserBehaviorStatementProfile(
                    condition.Kind == RewriteKind.IfEquals ? "if_state_equals" : "if_state_not_equals",
                    target: condition.Field,
                    value: condition.Value,
                    thenStatements: RewriteStatements(statement.Then),
                    elseStatements: RewriteStatements(statement.Else)));
                continue;
            }

            result.Add(new UserBehaviorStatementProfile(
                statement.Op,
                statement.Target,
                statement.Value,
                statement.Condition,
                RewriteStatements(statement.Then),
                RewriteStatements(statement.Else)));
        }
        return result;
    }

    private static RuntimeStateFieldProfile GetField(
        RuntimeStateProfile profile,
        string name,
        string path,
        int line)
    {
        if (profile.TryGetField(name, out var field))
            return field;
        throw Error(path, line, $"unknown runtime state field 'state.{name}'");
    }

    private static string ParseTypedValue(
        RuntimeStateFieldProfile field,
        string raw,
        string path,
        int line)
    {
        var value = raw.Trim().TrimEnd(';').Trim();
        if (field.Type == RuntimeStateType.Bool)
        {
            if (!bool.TryParse(value, out var parsed))
                throw Error(path, line, $"state.{field.Name} is bool and requires true or false");
            return parsed ? "true" : "false";
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.String)
                throw Error(path, line, $"state.{field.Name} is string and requires a quoted string");
            return document.RootElement.GetString() ?? string.Empty;
        }
        catch (JsonException exception)
        {
            throw Error(path, line, $"state.{field.Name} is string and requires a quoted string: {exception.Message}");
        }
    }

    private static string Token(int sequence) => $"IKEYDSTATE{sequence:D4}";

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

    private enum RewriteKind
    {
        Set,
        Toggle,
        IfEquals,
        IfNotEquals
    }

    private sealed record RewriteOperation(RewriteKind Kind, string Field, string? Value);
}
