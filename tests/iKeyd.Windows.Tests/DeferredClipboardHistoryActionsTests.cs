using iKeyd.App;
using iKeyd.Core.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class DeferredClipboardHistoryActionsTests
{
    [Fact]
    public void ShowPickerAndPaste_posts_to_ui_context_instead_of_running_inline()
    {
        var inner = new RecordingClipboardActions();
        var context = new QueueSynchronizationContext();
        var pickerCalls = 0;
        var actions = new DeferredClipboardHistoryActions(inner, context, () => pickerCalls++);

        Assert.True(actions.ShowPickerAndPaste());

        Assert.Equal(1, context.PostCount);
        Assert.Equal(0, pickerCalls);
        Assert.Equal(0, inner.ShowPickerAndPasteCalls);

        context.RunNext();

        Assert.Equal(1, pickerCalls);
    }

    [Fact]
    public void ShowPickerAndPaste_coalesces_repeated_requests_until_picker_finishes()
    {
        var inner = new RecordingClipboardActions();
        var context = new QueueSynchronizationContext();
        var pickerCalls = 0;
        var actions = new DeferredClipboardHistoryActions(inner, context, () => pickerCalls++);

        Assert.True(actions.ShowPickerAndPaste());
        Assert.True(actions.ShowPickerAndPaste());
        Assert.Equal(1, context.PostCount);

        context.RunNext();
        Assert.Equal(1, pickerCalls);

        Assert.True(actions.ShowPickerAndPaste());
        Assert.Equal(2, context.PostCount);
        context.RunNext();
        Assert.Equal(2, pickerCalls);
    }

    [Fact]
    public void Picker_failure_is_contained_and_next_request_can_run()
    {
        var inner = new RecordingClipboardActions();
        var context = new QueueSynchronizationContext();
        var pickerCalls = 0;
        var actions = new DeferredClipboardHistoryActions(
            inner,
            context,
            () =>
            {
                pickerCalls++;
                throw new InvalidOperationException("picker failed");
            });

        Assert.True(actions.ShowPickerAndPaste());
        Assert.Null(Record.Exception(context.RunNext));
        Assert.Equal(1, pickerCalls);

        Assert.True(actions.ShowPickerAndPaste());
        Assert.Null(Record.Exception(context.RunNext));
        Assert.Equal(2, pickerCalls);
    }

    [Fact]
    public void Post_failure_is_contained_and_reported_as_not_dispatched()
    {
        var inner = new RecordingClipboardActions();
        var actions = new DeferredClipboardHistoryActions(
            inner,
            new ThrowingSynchronizationContext(),
            () => throw new Xunit.Sdk.XunitException("picker must not run"));

        Assert.False(actions.ShowPickerAndPaste());
    }

    [Fact]
    public void Capture_and_paste_remain_synchronous()
    {
        var inner = new RecordingClipboardActions();
        var actions = new DeferredClipboardHistoryActions(
            inner,
            new QueueSynchronizationContext(),
            () => { });

        Assert.True(actions.CaptureLatest());
        Assert.True(actions.PasteCaptured());

        Assert.Equal(1, inner.CaptureLatestCalls);
        Assert.Equal(1, inner.PasteCapturedCalls);
    }

    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            _queue.Enqueue((d, state));
        }

        public void RunNext()
        {
            var (callback, state) = _queue.Dequeue();
            callback(state);
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
            => throw new InvalidOperationException("post failed");
    }

    private sealed class RecordingClipboardActions : IClipboardHistoryActions
    {
        public int ShowPickerAndPasteCalls { get; private set; }
        public int CaptureLatestCalls { get; private set; }
        public int PasteCapturedCalls { get; private set; }

        public bool ShowPickerAndPaste()
        {
            ShowPickerAndPasteCalls++;
            return true;
        }

        public bool CaptureLatest()
        {
            CaptureLatestCalls++;
            return true;
        }

        public bool PasteCaptured()
        {
            PasteCapturedCalls++;
            return true;
        }
    }
}
