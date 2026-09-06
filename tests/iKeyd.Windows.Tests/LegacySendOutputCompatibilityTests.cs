using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendOutputCompatibilityTests
{
    [Fact]
    public void Combined_modifiers_preserve_down_press_reverse_up_order()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("^+{TAB}");

        Assert.Equal(
        [
            Event(WindowsKeyMap.Control, KeyEventKind.Down),
            Event(WindowsKeyMap.Shift, KeyEventKind.Down),
            Event(WindowsKeyMap.Tab, KeyEventKind.Down),
            Event(WindowsKeyMap.Tab, KeyEventKind.Up),
            Event(WindowsKeyMap.Shift, KeyEventKind.Up),
            Event(WindowsKeyMap.Control, KeyEventKind.Up)
        ],
        keyboard.Events);
    }

    [Fact]
    public void Named_ctrl_vk_sc_and_media_tokens_used_by_hotkeyskg_are_supported()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("{CTRL}{vk1Csc079}{vk1Dsc07B}{vkF3sc029}{VOLUME_UP}{VOLUME_MUTE}{VOLUME_DOWN}{MEDIA_NEXT}{MEDIA_PLAY_PAUSE}{MEDIA_PREV}");

        Assert.Equal(20, keyboard.Events.Count);
        Assert.Equal(Event(WindowsKeyMap.Control, KeyEventKind.Down), keyboard.Events[0]);
        Assert.Equal(Event(WindowsKeyMap.Control, KeyEventKind.Up), keyboard.Events[1]);

        Assert.Equal(new KeyboardKey(0x1C, 0x79), keyboard.Events[2].Key);
        Assert.Equal(new KeyboardKey(0x1C, 0x79), keyboard.Events[3].Key);
        Assert.Equal(new KeyboardKey(0x1D, 0x7B), keyboard.Events[4].Key);
        Assert.Equal(new KeyboardKey(0x1D, 0x7B), keyboard.Events[5].Key);
        Assert.Equal(new KeyboardKey(0xF3, 0x29), keyboard.Events[6].Key);
        Assert.Equal(new KeyboardKey(0xF3, 0x29), keyboard.Events[7].Key);

        Assert.Equal(Event(WindowsKeyMap.VolumeUp, KeyEventKind.Down), keyboard.Events[8]);
        Assert.Equal(Event(WindowsKeyMap.VolumeMute, KeyEventKind.Down), keyboard.Events[10]);
        Assert.Equal(Event(WindowsKeyMap.VolumeDown, KeyEventKind.Down), keyboard.Events[12]);
        Assert.Equal(Event(WindowsKeyMap.MediaNext, KeyEventKind.Down), keyboard.Events[14]);
        Assert.Equal(Event(WindowsKeyMap.MediaPlayPause, KeyEventKind.Down), keyboard.Events[16]);
        Assert.Equal(Event(WindowsKeyMap.MediaPrevious, KeyEventKind.Down), keyboard.Events[18]);
        Assert.Empty(keyboard.Text);
    }

    [Fact]
    public void Click_wheel_tokens_used_by_hotkeyskg_preserve_control_modifier_semantics()
    {
        var keyboard = new RecordingKeyboardOutput();
        var desktop = new RecordingDesktopBackend();
        var output = new LegacySendOutput(keyboard, desktop);

        output.Send("^{Click,WU}^{Click,WD}");

        Assert.Empty(keyboard.Events);
        Assert.Empty(keyboard.Text);
        Assert.Equal([(120, true), (-120, true)], desktop.Scrolls);
    }

    [Fact]
    public void Mixed_menu_sequence_releases_alt_before_following_plain_text()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("!{Space}ep");

        Assert.Equal(
        [
            Event(WindowsKeyMap.Alt, KeyEventKind.Down),
            Event(WindowsKeyMap.Space, KeyEventKind.Down),
            Event(WindowsKeyMap.Space, KeyEventKind.Up),
            Event(WindowsKeyMap.Alt, KeyEventKind.Up)
        ],
        keyboard.Events);
        Assert.Equal(["ep"], keyboard.Text);
    }

    [Fact]
    public void Unsupported_brace_tokens_fail_with_a_diagnostic_instead_of_becoming_literal_text()
    {
        var output = new LegacySendOutput(new RecordingKeyboardOutput());

        var error = Assert.Throws<InvalidDataException>(() => output.Send("{Left 3}"));

        Assert.Contains("Unsupported hotkeySKG legacy Send syntax", error.Message);
        Assert.Contains("Left 3", error.Message);
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

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        public List<(int Delta, bool Control)> Scrolls { get; } = [];

        public WindowHandle GetActiveWindow() => throw new NotSupportedException();
        public DesktopWindowState GetWindowState(WindowHandle window) => throw new NotSupportedException();
        public DesktopRect GetWindowBounds(WindowHandle window) => throw new NotSupportedException();
        public DesktopRect GetPrimaryWorkArea() => throw new NotSupportedException();
        public string? GetWindowClass(WindowHandle window) => throw new NotSupportedException();
        public bool IsWindow(WindowHandle window) => throw new NotSupportedException();
        public void Minimize(WindowHandle window) => throw new NotSupportedException();
        public void Maximize(WindowHandle window) => throw new NotSupportedException();
        public void Restore(WindowHandle window) => throw new NotSupportedException();
        public void MoveResize(WindowHandle window, DesktopRect bounds) => throw new NotSupportedException();
        public void Activate(WindowHandle window) => throw new NotSupportedException();
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => throw new NotSupportedException();
        public bool IsTopMost(WindowHandle window) => throw new NotSupportedException();
        public void SetTopMost(WindowHandle window, bool enabled) => throw new NotSupportedException();
        public byte? GetOpacity(WindowHandle window) => throw new NotSupportedException();
        public void SetOpacity(WindowHandle window, byte? opacity) => throw new NotSupportedException();
        public bool HasCaption(WindowHandle window) => throw new NotSupportedException();
        public void SetCaption(WindowHandle window, bool enabled) => throw new NotSupportedException();
        public DesktopPoint GetPointerPosition() => throw new NotSupportedException();
        public void MovePointer(DesktopPoint position) => throw new NotSupportedException();
        public void MovePointerBy(int deltaX, int deltaY) => throw new NotSupportedException();
        public bool IsMouseButtonDown(DesktopMouseButton button) => throw new NotSupportedException();
        public void SetMouseButton(DesktopMouseButton button, bool down) => throw new NotSupportedException();
        public void Click(DesktopMouseButton button) => throw new NotSupportedException();
        public void ScrollVertical(int wheelDelta, bool controlModifier = false)
            => Scrolls.Add((wheelDelta, controlModifier));
        public void SendMediaCommand(DesktopMediaCommand command) => throw new NotSupportedException();
    }

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);
}
