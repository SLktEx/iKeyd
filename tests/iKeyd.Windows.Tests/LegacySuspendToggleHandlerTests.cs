using iKeyd.App;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySuspendToggleHandlerTests
{
    [Theory]
    [InlineData(0x11)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    public void Ctrl_Escape_toggles_suspend_and_remains_available_while_suspended(ushort controlKey)
    {
        var state = new KeyboardState();
        var inner = new RecordingHandler();
        var handler = new LegacySuspendToggleHandler(state, inner);

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(state, handler, controlKey, KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Down));
        Assert.True(handler.IsSuspended);
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Up));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(state, handler, controlKey, KeyEventKind.Up));

        var innerCountWhileSuspended = inner.Events.Count;
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(state, handler, 'Q', KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(state, handler, 'Q', KeyEventKind.Up));
        Assert.Equal(innerCountWhileSuspended, inner.Events.Count);

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(state, handler, controlKey, KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Down));
        Assert.False(handler.IsSuspended);
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Up));
        Dispatch(state, handler, controlKey, KeyEventKind.Up);

        var beforeResumedKey = inner.Events.Count;
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(state, handler, 'Q', KeyEventKind.Down));
        Assert.Equal(beforeResumedKey + 1, inner.Events.Count);
    }

    [Fact]
    public void Plain_Escape_is_delegated_when_not_suspended()
    {
        var state = new KeyboardState();
        var inner = new RecordingHandler { Disposition = KeyboardDisposition.Suppress };
        var handler = new LegacySuspendToggleHandler(state, inner);

        var disposition = Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Down);

        Assert.Equal(KeyboardDisposition.Suppress, disposition);
        Assert.False(handler.IsSuspended);
        Assert.Single(inner.Events);
    }

    [Fact]
    public void Suspend_hotkey_swallows_Escape_down_and_up_but_not_the_Control_key()
    {
        var state = new KeyboardState();
        var inner = new RecordingHandler();
        var handler = new LegacySuspendToggleHandler(state, inner);

        Dispatch(state, handler, WindowsKeyMap.Control, KeyEventKind.Down);
        Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Down);
        Dispatch(state, handler, WindowsKeyMap.Escape, KeyEventKind.Up);
        Dispatch(state, handler, WindowsKeyMap.Control, KeyEventKind.Up);

        Assert.DoesNotContain(inner.Events, item => item.Key.VirtualKey == WindowsKeyMap.Escape);
        Assert.Contains(inner.Events, item => item.Key.VirtualKey == WindowsKeyMap.Control && item.Kind == KeyEventKind.Down);
    }

    private static KeyboardDisposition Dispatch(
        KeyboardState state,
        LegacySuspendToggleHandler handler,
        ushort virtualKey,
        KeyEventKind kind)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            kind,
            KeyEventOrigin.Physical,
            0);
        state.Apply(keyboardEvent);
        return handler.OnKeyboardEvent(keyboardEvent);
    }

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        public List<KeyboardEvent> Events { get; } = [];
        public KeyboardDisposition Disposition { get; set; } = KeyboardDisposition.PassThrough;

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            Events.Add(keyboardEvent);
            return Disposition;
        }
    }
}
