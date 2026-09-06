namespace iKeyd.Core.Automation;

public enum CommandRequestKind
{
    Exec,
    Shell
}

public sealed record CommandRequest
{
    private CommandRequest(CommandRequestKind kind, string command, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command must not be empty.", nameof(command));

        Kind = kind;
        Command = command.Trim();
        Arguments = arguments;
    }

    public CommandRequestKind Kind { get; }
    public string Command { get; }
    public IReadOnlyList<string> Arguments { get; }

    public static CommandRequest Exec(string executable, IEnumerable<string>? arguments = null)
    {
        var argv = arguments?.Select(argument => argument ?? throw new ArgumentException("Command arguments must not contain null.", nameof(arguments))).ToArray()
            ?? [];
        return new CommandRequest(CommandRequestKind.Exec, executable, Array.AsReadOnly(argv));
    }

    public static CommandRequest Shell(string command)
        => new(CommandRequestKind.Shell, command, Array.Empty<string>());
}

public sealed record CommandResult(
    CommandRequest Request,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? Error)
{
    public bool Started => Error is null;
    public bool Succeeded => Error is null && ExitCode == 0;
}

public interface ICommandActionQueue : IDisposable
{
    bool TryEnqueue(CommandRequest request);
    CommandResult? LastResult { get; }
    event Action<CommandResult>? Completed;
}

public static class SystemQueryKeys
{
    public const string Os = "system.os";
    public const string Architecture = "system.architecture";
    public const string Hostname = "system.hostname";
    public const string Username = "system.username";
    public const string ForegroundProcess = "foreground.process";
    public const string ForegroundPid = "foreground.pid";
    public const string ForegroundTitle = "foreground.title";
    public const string ImeKanaActive = "ime.kana_active";
    public const string KeyboardCapsLock = "keyboard.capslock";
    public const string KeyboardNumLock = "keyboard.numlock";
    public const string KeyboardScrollLock = "keyboard.scrolllock";

    private static readonly string[] SupportedKeys =
    [
        Os,
        Architecture,
        Hostname,
        Username,
        ForegroundProcess,
        ForegroundPid,
        ForegroundTitle,
        ImeKanaActive,
        KeyboardCapsLock,
        KeyboardNumLock,
        KeyboardScrollLock
    ];

    public static IReadOnlyList<string> All => SupportedKeys;

    public static bool TryNormalize(string key, out string normalized)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            foreach (var candidate in SupportedKeys)
            {
                if (string.Equals(candidate, key.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    normalized = candidate;
                    return true;
                }
            }
        }

        normalized = string.Empty;
        return false;
    }

    public static string Normalize(string key)
        => TryNormalize(key, out var normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported system query '{key}'.", nameof(key));
}

public interface ISystemQueryProvider
{
    string GetValue(string key);
}
