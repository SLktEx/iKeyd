using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeMacroSlotTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Theory]
    [InlineData('Y')]
    [InlineData('H')]
    public void Physical_M_slot_runs_the_matching_legacy_macro(char slot)
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        var disposition = Dispatch(fixture, slot, KeyEventKind.Down, 10);
        Dispatch(fixture, slot, KeyEventKind.Up, 11);

        Assert.Equal(KeyboardDisposition.Suppress, disposition);
        Assert.Equal([slot], fixture.MacroSlots.RunSlots);
    }

    [Fact]
    public void Physical_MH_Y_opens_the_Y_template_editor()
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Down, 5);
        Dispatch(fixture, 'Y', KeyEventKind.Down, 10);

        Assert.Equal(['Y'], fixture.MacroSlots.EditTemplateSlots);
        Assert.Equal(0, fixture.MacroSlots.EditRepeatCalls);
    }

    [Fact]
    public void Physical_HM_H_opens_the_shared_repeat_editor()
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Down, 0);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 5);
        Dispatch(fixture, 'H', KeyEventKind.Down, 10);

        Assert.Empty(fixture.MacroSlots.EditTemplateSlots);
        Assert.Equal(1, fixture.MacroSlots.EditRepeatCalls);
    }

    [Fact]
    public void Physical_MS_Y_is_an_intentional_noop_not_pass_through()
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture, WindowsKeyMap.Space, KeyEventKind.Down, 5);
        var disposition = Dispatch(fixture, 'Y', KeyEventKind.Down, 10);

        Assert.Equal(KeyboardDisposition.Suppress, disposition);
        Assert.Empty(fixture.MacroSlots.RunSlots);
        Assert.Empty(fixture.MacroSlots.EditTemplateSlots);
        Assert.Equal(0, fixture.MacroSlots.EditRepeatCalls);
    }

    [Fact]
    public void Escape_cancels_running_legacy_slots_without_swallowing_Escape()
    {
        using var fixture = CreateRuntime();

        var disposition = Dispatch(fixture, WindowsKeyMap.Escape, KeyEventKind.Down, 0);

        Assert.Equal(1, fixture.MacroSlots.CancelCalls);
        Assert.Equal(KeyboardDisposition.PassThrough, disposition);
    }

    [Fact]
    public async Task Nested_macro_hotkey_waits_for_slot_editor_completion()
    {
        using var fixture = CreateRuntime();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.MacroSlots.EditTemplateCompletion = completion.Task;

        var action = fixture.Runtime.DispatchAsync(new MacroHotkey("MH", 'y'), CancellationToken.None);

        Assert.False(action.IsCompleted);
        Assert.Equal(['Y'], fixture.MacroSlots.EditTemplateSlots);
        completion.SetResult();
        await action;
    }

    private static RuntimeFixture CreateRuntime()
    {
        var keyboardState = new KeyboardState();
        var macroSlots = new RecordingMacroSlotActions();
        var runtime = new IKeydRuntimeHandler(
            IKeydConfiguration.Load(ProfilePath),
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(new NullKeyboardOutput()),
            new NullDesktopBackend());
        runtime.AttachMacroSlotActions(macroSlots);
        return new RuntimeFixture(runtime, keyboardState, macroSlots);
    }

    private static KeyboardDisposition Dispatch(
        RuntimeFixture fixture,
        ushort virtualKey,
        KeyEventKind kind,
        long timestampMs)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            kind,
            KeyEventOrigin.Physical,
            timestampMs);
        fixture.KeyboardState.Apply(keyboardEvent);
        return fixture.Runtime.OnKeyboardEvent(keyboardEvent);
    }

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        KeyboardState KeyboardState,
        RecordingMacroSlotActions MacroSlots) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class RecordingMacroSlotActions : ILegacyMacroSlotActions
    {
        public List<char> RunSlots { get; } = [];
        public List<char> EditTemplateSlots { get; } = [];
        public int EditRepeatCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public Task? EditTemplateCompletion { get; set; }

        public ValueTask RunAsync(char slot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunSlots.Add(slot);
            return ValueTask.CompletedTask;
        }

        public ValueTask EditTemplateAsync(char slot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EditTemplateSlots.Add(slot);
            return EditTemplateCompletion is null
                ? ValueTask.CompletedTask
                : new ValueTask(EditTemplateCompletion);
        }

        public ValueTask EditRepeatAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EditRepeatCalls++;
            return ValueTask.CompletedTask;
        }

        public void Cancel() => CancelCalls++;
    }

    private sealed class InactiveInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    private sealed class NullKeyboardOutput : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "MacroSlotTest";
        public bool IsWindow(WindowHandle window) => window == _window;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [_window];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
