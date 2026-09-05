using iKeyd.Core.Macros;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class MacroExecutorTests
{
    [Fact]
    public async Task Executor_expands_calc_flushes_around_actions_and_waits_in_order()
    {
        var output = new RecordingOutput();
        var actions = new RecordingActions();
        var delay = new RecordingDelay();
        var executor = new MacroExecutor(output, actions, delay);

        var result = await executor.ExecuteAsync(
            "A{calc 1+2}{hk MHr}B{wait 10}C",
            MacroRepeat.Once);

        Assert.Equal(["A3", "B", "C"], output.Sends);
        Assert.Equal([new MacroHotkey("MH", 'r')], actions.Hotkeys);
        Assert.Equal([TimeSpan.FromMilliseconds(10)], delay.Durations);
        Assert.Equal(1, result.CompletedIterations);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task Repeat_executes_incremented_template_and_returns_updated_definition()
    {
        var output = new RecordingOutput();
        var executor = new MacroExecutor(output, new RecordingActions(), new RecordingDelay());

        var result = await executor.ExecuteAsync("item`1`", new MacroRepeat(3, false));

        Assert.Equal(["item1", "item2", "item3"], output.Sends);
        Assert.Equal("item`4`", result.UpdatedTemplate);
        Assert.Equal(MacroRepeat.Once, result.NextRepeat);
        Assert.Equal(3, result.CompletedIterations);
    }

    [Fact]
    public async Task Plus_repeat_keeps_repeat_setting_for_next_invocation()
    {
        var executor = new MacroExecutor(new RecordingOutput(), new RecordingActions(), new RecordingDelay());
        var repeat = new MacroRepeat(2, true);

        var result = await executor.ExecuteAsync("x", repeat);

        Assert.Equal(repeat, result.NextRepeat);
    }

    [Fact]
    public async Task Cancellation_during_wait_stops_remaining_macro_without_throwing()
    {
        using var cancellation = new CancellationTokenSource();
        var output = new RecordingOutput();
        var delay = new CancellingDelay(cancellation);
        var executor = new MacroExecutor(output, new RecordingActions(), delay);

        var result = await executor.ExecuteAsync("before{wait 1000}after", MacroRepeat.Once, cancellation.Token);

        Assert.Equal(["before"], output.Sends);
        Assert.True(result.Cancelled);
        Assert.Equal(0, result.CompletedIterations);
    }

    [Fact]
    public async Task Zero_repeats_send_nothing_and_reset_non_persistent_repeat_to_once()
    {
        var output = new RecordingOutput();
        var executor = new MacroExecutor(output, new RecordingActions(), new RecordingDelay());

        var result = await executor.ExecuteAsync("never", new MacroRepeat(0, false));

        Assert.Empty(output.Sends);
        Assert.Equal(MacroRepeat.Once, result.NextRepeat);
        Assert.Equal(0, result.CompletedIterations);
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

    private sealed class RecordingActions : IMacroActionDispatcher
    {
        public List<MacroHotkey> Hotkeys { get; } = [];

        public ValueTask DispatchAsync(MacroHotkey hotkey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Hotkeys.Add(hotkey);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDelay : IMacroDelay
    {
        public List<TimeSpan> Durations { get; } = [];

        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Durations.Add(duration);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingDelay(CancellationTokenSource cancellation) : IMacroDelay
    {
        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
