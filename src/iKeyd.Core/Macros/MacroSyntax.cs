using System.Globalization;

namespace iKeyd.Core.Macros;

public enum MacroTokenKind
{
    Text,
    Wait,
    Calc,
    Hotkey
}

public readonly record struct MacroToken(MacroTokenKind Kind, string Value, int Position);

public abstract record MacroNode;

public sealed record MacroText(string Text) : MacroNode;

public sealed record MacroWait(TimeSpan Duration) : MacroNode;

public sealed record MacroCalc(string Expression) : MacroNode;

public sealed record MacroHotkey(string State, char Key) : MacroNode;

public sealed record MacroProgram(IReadOnlyList<MacroNode> Nodes);

public readonly record struct MacroRepeat(int Count, bool Persist)
{
    public static MacroRepeat Once { get; } = new(1, false);

    public static MacroRepeat Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim();
        var persist = text.StartsWith('+');
        if (persist)
            text = text[1..];

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw new FormatException("Macro repeat must be a non-negative integer, optionally prefixed with '+'.");

        return new MacroRepeat(count, persist);
    }

    public override string ToString()
        => string.Concat(Persist ? "+" : string.Empty, Count.ToString(CultureInfo.InvariantCulture));
}

public readonly record struct MacroIteration(string RenderedTemplate, string NextTemplate);

public sealed record MacroExecutionResult(
    string UpdatedTemplate,
    MacroRepeat NextRepeat,
    int CompletedIterations,
    bool Cancelled);

public sealed record MacroEditRequest(string Name, string Template, MacroRepeat Repeat);

public sealed record MacroEditResult(string Template, MacroRepeat Repeat);

public interface IMacroOutput
{
    ValueTask SendAsync(string legacySendText, CancellationToken cancellationToken);
}

public interface IMacroActionDispatcher
{
    ValueTask DispatchAsync(MacroHotkey hotkey, CancellationToken cancellationToken);
}

public interface IMacroDelay
{
    ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public interface IMacroEditor
{
    MacroEditResult? Edit(MacroEditRequest request);
}

public sealed class SystemMacroDelay : IMacroDelay
{
    public async ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        => await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
}

public sealed class MacroParseException : FormatException
{
    public MacroParseException(string message, int position)
        : base($"{message} (position {position})")
        => Position = position;

    public int Position { get; }
}
