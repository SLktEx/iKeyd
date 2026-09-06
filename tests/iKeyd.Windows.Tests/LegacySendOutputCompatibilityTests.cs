using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendOutputCompatibilityTests
{
    private const ushort LeftShift = 0xA0;
    private const ushort LeftControl = 0xA2;
    private const ushort LeftAlt = 0xA4;

    [Fact]
    public void Combined_modifiers_match_compiled_legacy_left_vks_and_release_order()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("^+{TAB}");

        Assert.Equal(
        [
            Event(LeftControl, KeyEventKind.Down),
            Event(LeftShift, KeyEventKind.Down),
            Event(WindowsKeyMap.Tab, KeyEventKind.Down),
            Event(WindowsKeyMap.Tab, KeyEventKind.Up),
            Event(LeftControl, KeyEventKind.Up),
            Event(LeftShift, KeyEventKind.Up)
        ],
        keyboard.Events);
    }

    [Fact]
    public void AHK_backtick_escapes_reachable_default_key_punctuation()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("`;`,`.`[`]^`;");

        Assert.Equal([";,.[]"], keyboard.Text);
        Assert.Equal(
        [
            Event(LeftControl, KeyEventKind.Down),
            Event(WindowsKeyMap.OemSemicolon, KeyEventKind.Down),
            Event(WindowsKeyMap.OemSemicolon, KeyEventKind.Up),
            Event(LeftControl, KeyEventKind.Up)
        ],
        keyboard.Events);
    }

    [Fact]
    public void Trailing_AHK_backtick_is_diagnostic()
    {
        var output = new LegacySendOutput(new RecordingKeyboardOutput());
        var error = Assert.Throws<InvalidDataException>(() => output.Send("abc`"));
        Assert.Contains("trailing AHK escape", error.Message);
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
        Assert.Equal(new KeyboardKey(0x1C, 0x79, PreserveVirtualKeyWithScanCode: true), keyboard.Events[2].Key);
        Assert.Equal(new KeyboardKey(0x1D, 0x7B, PreserveVirtualKeyWithScanCode: true), keyboard.Events[4].Key);
        Assert.Equal(new KeyboardKey(0xF3, 0x29, PreserveVirtualKeyWithScanCode: true), keyboard.Events[6].Key);
        Assert.Equal(Event(WindowsKeyMap.VolumeUp, KeyEventKind.Down), keyboard.Events[8]);
        Assert.Equal(Event(WindowsKeyMap.VolumeMute, KeyEventKind.Down), keyboard.Events[10]);
        Assert.Equal(Event(WindowsKeyMap.VolumeDown, KeyEventKind.Down), keyboard.Events[12]);
        Assert.Equal(Event(WindowsKeyMap.MediaNext, KeyEventKind.Down), keyboard.Events[14]);
        Assert.Equal(Event(WindowsKeyMap.MediaPlayPause, KeyEventKind.Down), keyboard.Events[16]);
        Assert.Equal(Event(WindowsKeyMap.MediaPrevious, KeyEventKind.Down), keyboard.Events[18]);
        Assert.Empty(keyboard.Text);
    }

    [Fact]
    public void Repeat_key_state_and_escaped_literal_tokens_from_withFuncKey_are_supported()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("{LEFT 3}{ENTER 2}{SHIFT DOWN}{HOME}{SHIFT UP}{{}{}}{!}{#}{^}");

        Assert.Equal(14, keyboard.Events.Count);
        Assert.Equal(Event(WindowsKeyMap.Left, KeyEventKind.Down), keyboard.Events[0]);
        Assert.Equal(Event(WindowsKeyMap.Left, KeyEventKind.Up), keyboard.Events[1]);
        Assert.Equal(Event(WindowsKeyMap.Left, KeyEventKind.Down), keyboard.Events[4]);
        Assert.Equal(Event(WindowsKeyMap.Left, KeyEventKind.Up), keyboard.Events[5]);
        Assert.Equal(Event(WindowsKeyMap.Enter, KeyEventKind.Down), keyboard.Events[6]);
        Assert.Equal(Event(WindowsKeyMap.Enter, KeyEventKind.Up), keyboard.Events[9]);
        Assert.Equal(Event(WindowsKeyMap.Shift, KeyEventKind.Down), keyboard.Events[10]);
        Assert.Equal(Event(WindowsKeyMap.Home, KeyEventKind.Down), keyboard.Events[11]);
        Assert.Equal(Event(WindowsKeyMap.Home, KeyEventKind.Up), keyboard.Events[12]);
        Assert.Equal(Event(WindowsKeyMap.Shift, KeyEventKind.Up), keyboard.Events[13]);
        Assert.Equal(["{", "}", "!", "#", "^"], keyboard.Text);
    }

    [Fact]
    public void Complex_withFuncKey_sequence_preserves_text_navigation_repeat_and_literal_braces()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("{END}+{HOME}^x\\begin{{}^v{}}{ENTER 2}\\end{{}^v{}}{UP}");

        Assert.Contains("\\begin", keyboard.Text);
        Assert.Contains("{", keyboard.Text);
        Assert.Contains("}", keyboard.Text);
        Assert.Contains("\\end", keyboard.Text);
        Assert.Equal(2, keyboard.Events.Count(item => item.Key.VirtualKey == WindowsKeyMap.Enter && item.Kind == KeyEventKind.Down));
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)'X');
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)'V');
    }

    [Fact]
    public void Jis_punctuation_with_outer_modifiers_keeps_required_shift()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("^$!_^{!}");

        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == LeftControl && item.Kind == KeyEventKind.Down);
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == LeftShift && item.Kind == KeyEventKind.Down);
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == LeftAlt && item.Kind == KeyEventKind.Down);
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)'4');
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == 0xE2);
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)'1');
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
    public void Macro_click_coordinate_example_moves_and_holds_the_requested_button()
    {
        var desktop = new RecordingDesktopBackend();
        var output = new LegacySendOutput(new RecordingKeyboardOutput(), desktop);

        output.Send("{Click,123,456,Right,Down}");

        Assert.Equal(new DesktopPoint(123, 456), desktop.Pointer);
        Assert.Equal([(DesktopMouseButton.Right, true)], desktop.ButtonStates);
    }

    [Fact]
    public void Mixed_menu_sequence_releases_alt_before_following_plain_text()
    {
        var keyboard = new RecordingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        output.Send("!{Space}ep");

        Assert.Equal(
        [
            Event(LeftAlt, KeyEventKind.Down),
            Event(WindowsKeyMap.Space, KeyEventKind.Down),
            Event(WindowsKeyMap.Space, KeyEventKind.Up),
            Event(LeftAlt, KeyEventKind.Up)
        ],
        keyboard.Events);
        Assert.Equal(["ep"], keyboard.Text);
    }

    [Fact]
    public void Unsupported_brace_tokens_fail_with_a_diagnostic_instead_of_becoming_literal_text()
    {
        var output = new LegacySendOutput(new RecordingKeyboardOutput());

        var error = Assert.Throws<InvalidDataException>(() => output.Send("{BogusLegacyToken}"));

        Assert.Contains("Unsupported hotkeySKG legacy Send syntax", error.Message);
        Assert.Contains("BogusLegacyToken", error.Message);
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
        public List<(DesktopMouseButton Button, bool Down)> ButtonStates { get; } = [];
        public DesktopPoint Pointer { get; private set; }

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
        public DesktopPoint GetPointerPosition() => Pointer;
        public void MovePointer(DesktopPoint position) => Pointer = position;
        public void MovePointerBy(int deltaX, int deltaY) => throw new NotSupportedException();
        public bool IsMouseButtonDown(DesktopMouseButton button) => ButtonStates.LastOrDefault(item => item.Button == button).Down;
        public void SetMouseButton(DesktopMouseButton button, bool down) => ButtonStates.Add((button, down));
        public void Click(DesktopMouseButton button) => throw new NotSupportedException();
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) => Scrolls.Add((wheelDelta, controlModifier));
        public void SendMediaCommand(DesktopMediaCommand command) => throw new NotSupportedException();
    }

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);
}
