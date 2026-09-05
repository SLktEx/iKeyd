using System.Globalization;
using System.Text;

namespace iKeyd.Core.Macros;

public sealed class MacroIncrementer
{
    public MacroIteration PrepareIteration(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var parts = template.Split('`', StringSplitOptions.None);
        var rendered = string.Concat(parts);

        for (var index = 1; index < parts.Length; index += 2)
            parts[index] = Increment(parts[index]);

        return new MacroIteration(rendered, string.Join('`', parts));
    }

    private static string Increment(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return checked(number + 1).ToString(CultureInfo.InvariantCulture);
        if (value.Length == 0)
            return value;
        if (value[0] == char.MaxValue)
            throw new OverflowException("Macro increment reached the maximum UTF-16 character value.");

        // This intentionally matches the legacy script: a non-numeric marked segment is
        // replaced by only its incremented first character rather than preserving its suffix.
        return ((char)(value[0] + 1)).ToString();
    }
}

public sealed class MacroExecutor
{
    private readonly MacroParser _parser;
    private readonly MacroExpressionEvaluator _calculator;
    private readonly MacroIncrementer _incrementer;
    private readonly IMacroOutput _output;
    private readonly IMacroActionDispatcher _actions;
    private readonly IMacroDelay _delay;

    public MacroExecutor(
        IMacroOutput output,
        IMacroActionDispatcher actions,
        IMacroDelay? delay = null,
        MacroParser? parser = null,
        MacroExpressionEvaluator? calculator = null,
        MacroIncrementer? incrementer = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _delay = delay ?? new SystemMacroDelay();
        _parser = parser ?? new MacroParser();
        _calculator = calculator ?? new MacroExpressionEvaluator();
        _incrementer = incrementer ?? new MacroIncrementer();
    }

    public async ValueTask<MacroExecutionResult> ExecuteAsync(
        string template,
        MacroRepeat repeat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (repeat.Count < 0)
            throw new ArgumentOutOfRangeException(nameof(repeat));

        var currentTemplate = template;
        var completed = 0;
        var cancelled = false;

        try
        {
            for (var iteration = 0; iteration < repeat.Count; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // hotkeySKG mutates increment-marked fields before executing each rendered iteration.
                var prepared = _incrementer.PrepareIteration(currentTemplate);
                currentTemplate = prepared.NextTemplate;
                var program = _parser.Parse(prepared.RenderedTemplate);
                await ExecuteProgramAsync(program, cancellationToken).ConfigureAwait(false);
                completed++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        var nextRepeat = repeat.Persist ? repeat : MacroRepeat.Once;
        return new MacroExecutionResult(currentTemplate, nextRepeat, completed, cancelled);
    }

    private async ValueTask ExecuteProgramAsync(MacroProgram program, CancellationToken cancellationToken)
    {
        var pendingSend = new StringBuilder();

        foreach (var node in program.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case MacroText text:
                    pendingSend.Append(text.Text);
                    break;

                case MacroCalc calc:
                    pendingSend.Append(_calculator.Evaluate(calc.Expression).ToString(CultureInfo.InvariantCulture));
                    break;

                case MacroWait wait:
                    await FlushAsync(pendingSend, cancellationToken).ConfigureAwait(false);
                    await _delay.DelayAsync(wait.Duration, cancellationToken).ConfigureAwait(false);
                    break;

                case MacroHotkey hotkey:
                    await FlushAsync(pendingSend, cancellationToken).ConfigureAwait(false);
                    await _actions.DispatchAsync(hotkey, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported macro node: {node.GetType().Name}");
            }
        }

        await FlushAsync(pendingSend, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FlushAsync(StringBuilder pendingSend, CancellationToken cancellationToken)
    {
        if (pendingSend.Length == 0)
            return;

        var text = pendingSend.ToString();
        pendingSend.Clear();
        await _output.SendAsync(text, cancellationToken).ConfigureAwait(false);
    }
}
