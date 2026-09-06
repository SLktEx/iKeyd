using iKeyd.App;
using iKeyd.Core.Macros;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyMacroSlotControllerTests
{
    [Fact]
    public async Task H_and_Y_templates_are_independent_and_repeat_is_shared()
    {
        var output = new RecordingOutput();
        var editor = new RecordingEditor();
        using var controller = new LegacyMacroSlotController(
            new MacroExecutor(output, new NoopActions(), new ImmediateDelay()),
            editor);

        editor.NextResult = new MacroEditResult("H-template", new MacroRepeat(99, true));
        await controller.EditTemplateAsync('H');

        Assert.Equal("H-template", controller.GetTemplate('H'));
        Assert.Equal(string.Empty, controller.GetTemplate('Y'));
        Assert.Equal(MacroRepeat.Once, controller.Repeat);
        Assert.Equal(MacroEditScope.Template, editor.Requests[^1].Scope);

        editor.NextResult = new MacroEditResult("ignored", new MacroRepeat(3, true));
        await controller.EditRepeatAsync();

        Assert.Equal("H-template", controller.GetTemplate('H'));
        Assert.Equal(string.Empty, controller.GetTemplate('Y'));
        Assert.Equal(new MacroRepeat(3, true), controller.Repeat);
        Assert.Equal(MacroEditScope.Repeat, editor.Requests[^1].Scope);
    }

    [Fact]
    public async Task Run_updates_incremented_slot_template_and_resets_nonpersistent_repeat()
    {
        var output = new RecordingOutput();
        var editor = new RecordingEditor();
        using var controller = new LegacyMacroSlotController(
            new MacroExecutor(output, new NoopActions(), new ImmediateDelay()),
            editor);

        editor.NextResult = new MacroEditResult("x`1`", MacroRepeat.Once);
        await controller.EditTemplateAsync('Y');
        editor.NextResult = new MacroEditResult(string.Empty, new MacroRepeat(2, false));
        await controller.EditRepeatAsync();

        await controller.RunAsync('Y');

        Assert.Equal(["x1", "x2"], output.Sends);
        Assert.Equal("x`3`", controller.GetTemplate('Y'));
        Assert.Equal(MacroRepeat.Once, controller.Repeat);
    }

    [Fact]
    public async Task Persistent_repeat_survives_slot_execution()
    {
        var output = new RecordingOutput();
        var editor = new RecordingEditor();
        using var controller = new LegacyMacroSlotController(
            new MacroExecutor(output, new NoopActions(), new ImmediateDelay()),
            editor);

        editor.NextResult = new MacroEditResult("a", MacroRepeat.Once);
        await controller.EditTemplateAsync('H');
        editor.NextResult = new MacroEditResult(string.Empty, new MacroRepeat(2, true));
        await controller.EditRepeatAsync();

        await controller.RunAsync('H');

        Assert.Equal(["a", "a"], output.Sends);
        Assert.Equal(new MacroRepeat(2, true), controller.Repeat);
    }

    [Fact]
    public async Task Cancel_stops_an_active_waiting_macro()
    {
        var editor = new RecordingEditor();
        var delay = new CancellableDelay();
        using var controller = new LegacyMacroSlotController(
            new MacroExecutor(new RecordingOutput(), new NoopActions(), delay),
            editor);

        editor.NextResult = new MacroEditResult("{Wait 5000}", MacroRepeat.Once);
        await controller.EditTemplateAsync('Y');

        var run = controller.RunAsync('Y').AsTask();
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        controller.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(delay.Cancelled);
    }

    private sealed class RecordingEditor : IMacroEditor
    {
        public List<MacroEditRequest> Requests { get; } = [];
        public MacroEditResult? NextResult { get; set; }

        public MacroEditResult? Edit(MacroEditRequest request)
        {
            Requests.Add(request);
            return NextResult;
        }
    }

    private sealed class RecordingOutput : IMacroOutput
    {
        public List<string> Sends { get; } = [];

        public ValueTask SendAsync(string legacySendText, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sends.Add(legacySendText);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopActions : IMacroActionDispatcher
    {
        public ValueTask DispatchAsync(MacroHotkey hotkey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ImmediateDelay : IMacroDelay
    {
        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellableDelay : IMacroDelay
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }

        public async ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}
