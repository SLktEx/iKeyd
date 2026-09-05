using iKeyd.Core.Clipboard;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void Records_newest_first_and_keeps_only_capacity()
    {
        var history = new ClipboardHistory(3);

        history.Record("one");
        history.Record("two");
        history.Record("three");
        history.Record("four");

        Assert.Equal(["four", "three", "two"], history.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_clipboard_values_are_ignored(string? value)
    {
        var history = new ClipboardHistory();

        Assert.False(history.Record(value));
        Assert.Empty(history.Items);
    }

    [Fact]
    public void Recopying_existing_text_moves_it_to_front_without_duplicate()
    {
        var history = new ClipboardHistory();
        history.Record("one");
        history.Record("two");
        history.Record("three");

        Assert.True(history.Record("one"));

        Assert.Equal(["one", "three", "two"], history.Items);
    }

    [Fact]
    public void Recording_current_front_item_is_a_noop()
    {
        var history = new ClipboardHistory();
        history.Record("one");

        Assert.False(history.Record("one"));
        Assert.Equal(["one"], history.Items);
    }

    [Fact]
    public void Picker_selection_can_promote_exact_index()
    {
        var history = new ClipboardHistory();
        history.Record("one");
        history.Record("two");
        history.Record("three");

        var selected = history.Promote(2);

        Assert.Equal("one", selected);
        Assert.Equal(["one", "three", "two"], history.Items);
    }

    [Fact]
    public void Clear_removes_all_history()
    {
        var history = new ClipboardHistory();
        history.Record("one");
        history.Record("two");

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Empty(history.Items);
    }
}
