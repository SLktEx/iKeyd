using iKeyd.Core.Macros;

namespace iKeyd.App;

internal sealed class LegacyMacroSlotController : ILegacyMacroSlotActions, IDisposable
{
    private readonly object _gate = new();
    private readonly MacroExecutor _executor;
    private readonly IMacroEditor _editor;
    private readonly Action<string, Exception>? _errorHandler;
    private readonly Dictionary<char, string> _templates = new()
    {
        ['H'] = string.Empty,
        ['Y'] = string.Empty
    };
    private readonly HashSet<CancellationTokenSource> _activeRuns = [];

    private MacroRepeat _repeat = MacroRepeat.Once;
    private bool _disposed;

    public LegacyMacroSlotController(
        MacroExecutor executor,
        IMacroEditor editor,
        Action<string, Exception>? errorHandler = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _errorHandler = errorHandler;
    }

    public async ValueTask RunAsync(char slot, CancellationToken cancellationToken = default)
    {
        slot = NormalizeSlot(slot);

        string template;
        MacroRepeat repeat;
        CancellationTokenSource runCancellation;
        lock (_gate)
        {
            ThrowIfDisposed();
            template = _templates[slot];
            repeat = _repeat;
            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRuns.Add(runCancellation);
        }

        try
        {
            var result = await _executor.ExecuteAsync(template, repeat, runCancellation.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (!_disposed)
                {
                    _templates[slot] = result.UpdatedTemplate;
                    _repeat = result.NextRepeat;
                }
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            // MacroExecutor normally converts cancellation into its result, but keep
            // controller-level cancellation benign if a dependency throws directly.
        }
        catch (Exception exception)
        {
            _errorHandler?.Invoke($"Macro {slot} failed.", exception);
        }
        finally
        {
            lock (_gate)
                _activeRuns.Remove(runCancellation);
            runCancellation.Dispose();
        }
    }

    public async ValueTask EditTemplateAsync(char slot, CancellationToken cancellationToken = default)
    {
        slot = NormalizeSlot(slot);
        string template;
        MacroRepeat repeat;
        lock (_gate)
        {
            ThrowIfDisposed();
            template = _templates[slot];
            repeat = _repeat;
        }

        try
        {
            var result = await Task.Run(
                () => _editor.Edit(new MacroEditRequest(
                    slot.ToString(),
                    template,
                    repeat,
                    MacroEditScope.Template)),
                cancellationToken).ConfigureAwait(false);
            if (result is null)
                return;

            lock (_gate)
            {
                if (!_disposed)
                    _templates[slot] = result.Template;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _errorHandler?.Invoke($"Macro {slot} editor failed.", exception);
        }
    }

    public async ValueTask EditRepeatAsync(CancellationToken cancellationToken = default)
    {
        MacroRepeat repeat;
        lock (_gate)
        {
            ThrowIfDisposed();
            repeat = _repeat;
        }

        try
        {
            var result = await Task.Run(
                () => _editor.Edit(new MacroEditRequest(
                    "Loop",
                    string.Empty,
                    repeat,
                    MacroEditScope.Repeat)),
                cancellationToken).ConfigureAwait(false);
            if (result is null)
                return;

            lock (_gate)
            {
                if (!_disposed)
                    _repeat = result.Repeat;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _errorHandler?.Invoke("Macro loop editor failed.", exception);
        }
    }

    public void Cancel()
    {
        CancellationTokenSource[] active;
        lock (_gate)
            active = _activeRuns.ToArray();

        foreach (var cancellation in active)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        Cancel();
    }

    internal string GetTemplate(char slot)
    {
        slot = NormalizeSlot(slot);
        lock (_gate)
            return _templates[slot];
    }

    internal MacroRepeat Repeat
    {
        get
        {
            lock (_gate)
                return _repeat;
        }
    }

    private static char NormalizeSlot(char slot)
    {
        var normalized = char.ToUpperInvariant(slot);
        return normalized is 'H' or 'Y'
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(slot), "Legacy macro slot must be H or Y.");
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
