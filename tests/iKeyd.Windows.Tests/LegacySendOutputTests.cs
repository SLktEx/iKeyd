using iKeyd.App;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendOutputTests
{
    [Fact]
    public void RepeatBraceToken_RepeatsNamedKey()
    {
        var keyboard = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(keyboard);

        send.Send("/*  */{LEFT 3}");

        Assert.Equal("/*  */", Assert.Single(keyboard.Text));
        Assert.Equal(
            new[]
            {
                Press(WindowsKeyMap.Left),
                Press(WindowsKeyMap.Left),
                Press(WindowsKeyMap.Left)
            },
            keyboard.Events);
    }

    [Fact]
    public void DownUpBraceTokens_PreserveExplicitModifierLifetime()
    {
        var keyboard = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(keyboard);

        send.Send("{END}{SHIFT DOWN}{HOME}{LEFT}{SHIFT UP}");

        Assert.Equal(
            new[]
            {
                Press(WindowsKeyMap.End),
                Down(WindowsKeyMap.Shift),
                Press(WindowsKeyMap.Home),
                Press(WindowsKeyMap.Left),
                Up(WindowsKeyMap.Shift)
            },
            keyboard.Events);
    }

    [Fact]
    public void LiteralBraceTokens_AreEmittedAsText()
    {
        var keyboard = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(keyboard);

        send.Send("{{}{ENTER}{ENTER}{}}{UP}{END}");

        Assert.Equal(new[] { "{", "}" }, keyboard.Text);
        Assert.Equal(
            new[]
            {
                Press(WindowsKeyMap.Enter),
                Press(WindowsKeyMap.Enter),
                Press(WindowsKeyMap.Up),
                Press(WindowsKeyMap.End)
            },
            keyboard.Events);
    }

    [Fact]
    public void VirtualKeyScanCodeToken_PreservesBothFields()
    {
        var keyboard = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(keyboard);

        send.Send("{vkF3sc029}");

        var entry = Assert.Single(keyboard.RawEvents);
        Assert.Equal("press", entry.Kind);
        Assert.Equal((ushort)0xF3, entry.Key.VirtualKey);
        Assert.Equal((ushort)0x29, entry.Key.ScanCode);
    }

    [Fact]
    public void PrefixModifiers_ArePressedAndReleasedAroundNamedKey()
    {
        var keyboard = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(keyboard);

        send.Send("^+{TAB}");

        Assert.Equal(
            new[]
            {
                Down(WindowsKeyMap.Control),
                Down(WindowsKeyMap.Shift),
                Press(WindowsKeyMap.Tab),
                Up(WindowsKeyMap.Shift),
                Up(WindowsKeyMap.Control)
            },
            keyboard.Events);
    }

    [Fact]
    public void UnknownBraceToken_RemainsLiteral()
    {
        var keyboard = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(keyboard);

        send.Send("{BOGUS}");

        Assert.Equal("{BOGUS}", Assert.Single(keyboard.Text));
        Assert.Empty(keyboard.Events);
    }

    private static string Press(ushort key) => $"press:{key:X2}";
    private static string Down(ushort key) => $"down:{key:X2}";
    private static string Up(ushort key) => $"up:{key:X2}";

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<string> Events { get; } = [];
        public List<(string Kind, KeyboardKey Key)> RawEvents { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
        {
            var label = kind == KeyEventKind.Down ? "down" : "up";
            Events.Add($"{label}:{key.VirtualKey:X2}");
            RawEvents.Add((label, key));
        }

        public void SendKeyPress(KeyboardKey key)
        {
            Events.Add($"press:{key.VirtualKey:X2}");
            RawEvents.Add(("press", key));
        }

        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }
}
