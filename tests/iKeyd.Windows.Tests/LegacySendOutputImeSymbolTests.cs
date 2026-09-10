using iKeyd.App;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendOutputImeSymbolTests
{
    private const ushort LeftShift = 0xA0;

    [Fact]
    public void Plain_tilde_is_emitted_as_jis_shift_caret_instead_of_unicode_text()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("~");

        Assert.Equal(
        [
            Event(LeftShift, KeyEventKind.Down),
            Event(WindowsKeyMap.OemCaret, KeyEventKind.Down),
            Event(WindowsKeyMap.OemCaret, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Up)
        ],
        keyboard.Events);
        Assert.Empty(keyboard.Text);
    }

    [Fact]
    public void Plain_function_symbols_are_lowered_to_jis_key_events_before_sendtext_fallback()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("-=%~");

        Assert.Equal(
        [
            Event(WindowsKeyMap.OemMinus, KeyEventKind.Down),
            Event(WindowsKeyMap.OemMinus, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Down),
            Event(WindowsKeyMap.OemMinus, KeyEventKind.Down),
            Event(WindowsKeyMap.OemMinus, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Down),
            Event((ushort)'5', KeyEventKind.Down),
            Event((ushort)'5', KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Down),
            Event(WindowsKeyMap.OemCaret, KeyEventKind.Down),
            Event(WindowsKeyMap.OemCaret, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Up)
        ],
        keyboard.Events);
        Assert.Empty(keyboard.Text);
    }

    [Fact]
    public void Non_jis_unicode_text_keeps_unicode_sendtext_fallback()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("日本語");

        Assert.Empty(keyboard.Events);
        Assert.Equal(["日本語"], keyboard.Text);
    }

    private static RecordedKeyboardEvent Event(ushort virtualKey, KeyEventKind kind)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind);

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<RecordedKeyboardEvent> Events { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new RecordedKeyboardEvent(key, kind));

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);
}
