using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardHookDeadlineTests
{
    [Fact]
    public void Behavior_deadline_callback_runs_from_hook_message_loop_without_input()
    {
        using var hook = new WindowsKeyboardHook();
        using var fired = new ManualResetEventSlim(false);
        var callbackThreadId = 0;
        var callbackTimestamp = 0L;

        hook.Start(new PassThroughHandler());
        var deadline = Environment.TickCount64 + 25;

        hook.ScheduleBehaviorDeadline(
            deadline,
            timestampMs =>
            {
                callbackThreadId = Environment.CurrentManagedThreadId;
                callbackTimestamp = timestampMs;
                fired.Set();
            });

        Assert.True(fired.Wait(TimeSpan.FromSeconds(2)), "Behavior deadline callback did not fire.");
        Assert.True(callbackTimestamp >= deadline);
        Assert.NotEqual(0, callbackThreadId);
    }

    [Fact]
    public void Replacing_deadline_does_not_allow_stale_timer_to_fire_new_callback_early()
    {
        using var hook = new WindowsKeyboardHook();
        using var fired = new ManualResetEventSlim(false);
        var callbackTimestamp = 0L;

        hook.Start(new PassThroughHandler());
        hook.ScheduleBehaviorDeadline(Environment.TickCount64 + 10, _ => { });

        var replacementDeadline = Environment.TickCount64 + 75;
        hook.ScheduleBehaviorDeadline(
            replacementDeadline,
            timestampMs =>
            {
                callbackTimestamp = timestampMs;
                fired.Set();
            });

        Assert.True(fired.Wait(TimeSpan.FromSeconds(2)), "Replacement Behavior deadline callback did not fire.");
        Assert.True(
            callbackTimestamp >= replacementDeadline,
            $"Replacement deadline fired early: {callbackTimestamp} < {replacementDeadline}.");
    }

    private sealed class PassThroughHandler : IKeyboardEventHandler
    {
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
            => KeyboardDisposition.PassThrough;
    }
}
