using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class KeyboardStateTests
{
    [Fact]
    public void Key_down_and_up_update_pressed_state()
    {
        var state = new KeyboardState();
        var key = new KeyboardKey(0x41, 0x1E);

        state.Apply(new KeyboardEvent(key, KeyEventKind.Down, KeyEventOrigin.Physical, 1));
        Assert.True(state.IsPressed(key));
        Assert.True(state.IsVirtualKeyPressed(0x41));

        state.Apply(new KeyboardEvent(key, KeyEventKind.Up, KeyEventOrigin.Physical, 2));
        Assert.False(state.IsPressed(key));
        Assert.False(state.IsVirtualKeyPressed(0x41));
    }

    [Fact]
    public void Extended_and_non_extended_keys_are_tracked_separately()
    {
        var state = new KeyboardState();
        var regular = new KeyboardKey(0x0D, 0x1C, false);
        var extended = new KeyboardKey(0x0D, 0x1C, true);

        state.Apply(new KeyboardEvent(regular, KeyEventKind.Down, KeyEventOrigin.Physical, 1));
        state.Apply(new KeyboardEvent(extended, KeyEventKind.Down, KeyEventOrigin.Physical, 2));

        Assert.Equal(2, state.Snapshot().Count);
        Assert.True(state.IsVirtualKeyPressed(0x0D));
    }
}
