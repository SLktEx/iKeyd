using iKeyd.Core.Behaviors;
using iKeyd.Core.Clipboard;
using iKeyd.Core.Macros;
using iKeyd.Core.Modes;
using iKeyd.Windows.Clipboard;
using iKeyd.Windows.Desktop;
using iKeyd.Windows.Input;
using iKeyd.Windows.Macros;

namespace iKeyd.App;

internal sealed class IKeydApplicationContext : ApplicationContext
{
    private readonly WindowsKeyboardBackend _keyboard;
    private readonly IKeydRuntimeHandler _runtime;
    private readonly BehaviorWindowsInputRouter _keyboardHandler;
    private readonly LegacySuspendToggleHandler _suspendHandler;
    private readonly WindowsClipboardService _clipboardService;
    private readonly WindowsClipboardHistoryPersistence? _clipboardPersistence;
    private readonly WindowsClipboardController _clipboard;
    private readonly Control _behaviorHostDispatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Dictionary<InputMode, ToolStripMenuItem> _modeItems = [];
    private readonly ToolStripMenuItem _cancelMacroItem;
    private readonly WindowsMacroEditor _macroEditor = new();
    private readonly MacroExecutor _macroExecutor;
    private readonly LegacyMacroSlotController _legacyMacroSlots;

    private string _macroTemplate = string.Empty;
    private MacroRepeat _macroRepeat = MacroRepeat.Once;
    private CancellationTokenSource? _macroCancellation;
    private bool _stopping;

    public IKeydApplicationContext(IKeydConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _keyboard = new WindowsKeyboardBackend();
        var desktop = new WindowsDesktopBackend();
        var send = new LegacySendOutput(_keyboard, desktop);

        var clipboardSettings = configuration.Profile.Clipboard;
        _clipboardService = new WindowsClipboardService();
        _clipboardPersistence = clipboardSettings.History && clipboardSettings.Persist
            ? new WindowsClipboardHistoryPersistence(clipboardSettings)
            : null;
        var clipboardPicker = new WindowsClipboardPicker();
        var payloadHistory = clipboardSettings.History
            ? new ClipboardPayloadHistory(clipboardSettings.MaxItems, _clipboardPersistence)
            : null;
        _clipboard = new WindowsClipboardController(
            _clipboardService,
            new ClipboardHistory(clipboardSettings.MaxItems),
            clipboardPicker,
            _keyboard,
            payloadHistory,
            clipboardPicker,
            clipboardSettings.History,
            clipboardSettings.Images);

        var inputMethod = new WindowsInputMethod();
        _runtime = new IKeydRuntimeHandler(
            configuration,
            inputMethod,
            _keyboard.State,
            send,
            desktop,
            _clipboard);

        _macroExecutor = new MacroExecutor(send, _runtime);
        _legacyMacroSlots = new LegacyMacroSlotController(
            _macroExecutor,
            _macroEditor,
            ShowError);
        _runtime.AttachMacroSlotActions(_legacyMacroSlots);

        _behaviorHostDispatcher = new Control();
        _ = _behaviorHostDispatcher.Handle;
        var hostActions = new DelegateBehaviorHostActionSink(PostBehaviorHostAction);

        _keyboardHandler = new BehaviorWindowsInputRouter(
            configuration.Profile,
            () => _runtime.Mode.Route(inputMethod).Keymap?.ToString(),
            send,
            _keyboard,
            _runtime,
            desktop,
            hostActions);
        _suspendHandler = new LegacySuspendToggleHandler(_keyboard.State, _keyboardHandler);

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("iKeyd") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        var modeMenu = new ToolStripMenuItem("Mode");
        foreach (var mode in new[] { InputMode.S, InputMode.K, InputMode.T, InputMode.R })
        {
            var item = new ToolStripMenuItem(mode.ToString()) { CheckOnClick = false };
            item.Click += (_, _) => ChangeMode(mode);
            _modeItems.Add(mode, item);
            modeMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(modeMenu);

        _menu.Items.Add(new ToolStripMenuItem("Clipboard History...", null, (_, _) => ShowClipboardHistory())
        {
            Enabled = clipboardSettings.History
        });
        _menu.Items.Add(new ToolStripMenuItem("Macro...", null, async (_, _) => await EditAndRunMacroAsync()));
        _cancelMacroItem = new ToolStripMenuItem("Cancel Macro", null, (_, _) => CancelMacros())
        {
            Enabled = true
        };
        _menu.Items.Add(_cancelMacroItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "iKeyd",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowClipboardHistory();

        UpdateModeChecks();
        _keyboard.Start(_suspendHandler);
    }

    protected override void ExitThreadCore()
    {
        if (_stopping)
            return;
        _stopping = true;

        CancelMacros();
        _notifyIcon.Visible = false;

        try
        {
            _keyboard.Stop();
        }
        finally
        {
            _legacyMacroSlots.Dispose();
            _clipboard.Dispose();
            _clipboardPersistence?.Dispose();
            _clipboardService.Dispose();
            _keyboardHandler.Dispose();
            _runtime.Dispose();
            _keyboard.Dispose();
            _behaviorHostDispatcher.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _macroCancellation?.Dispose();
            _macroCancellation = null;
        }

        base.ExitThreadCore();
    }

    private void PostBehaviorHostAction(BehaviorAction action)
    {
        if (_stopping || _behaviorHostDispatcher.IsDisposed)
            return;

        try
        {
            _behaviorHostDispatcher.BeginInvoke((Action)(() => DispatchBehaviorHostAction(action)));
        }
        catch (InvalidOperationException) when (_stopping || _behaviorHostDispatcher.IsDisposed)
        {
        }
    }

    private void DispatchBehaviorHostAction(BehaviorAction action)
    {
        if (_stopping)
            return;

        switch (action.Kind)
        {
            case BehaviorActionKind.Clipboard:
                DispatchClipboardBehavior(action.Name);
                break;
            case BehaviorActionKind.Macro:
                _ = RunConfiguredMacroAsync(action.Name ?? string.Empty);
                break;
            default:
                throw new InvalidOperationException($"Unsupported host behavior action '{action.Kind}'.");
        }
    }

    private void DispatchClipboardBehavior(string? command)
    {
        try
        {
            switch (command?.ToUpperInvariant())
            {
                case "HISTORY": _clipboard.ShowPickerAndPaste(); break;
                case "CAPTURE": _clipboard.CaptureLatest(); break;
                case "PASTE": _clipboard.PasteCaptured(); break;
                default: throw new InvalidOperationException($"Unknown clipboard behavior command '{command}'.");
            }
        }
        catch (Exception exception)
        {
            ShowError("Clipboard behavior failed.", exception);
        }
    }

    private async Task RunConfiguredMacroAsync(string template)
    {
        if (_macroCancellation is not null || _stopping)
            return;

        try
        {
            _macroCancellation = new CancellationTokenSource();
            await _macroExecutor.ExecuteAsync(template, MacroRepeat.Once, _macroCancellation.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError("Configured macro failed.", exception);
        }
        finally
        {
            _macroCancellation?.Dispose();
            _macroCancellation = null;
        }
    }

    private void ChangeMode(InputMode mode)
    {
        try
        {
            _runtime.SetMode(mode);
            UpdateModeChecks();
            _notifyIcon.Text = $"iKeyd — {mode} mode";
        }
        catch (Exception exception)
        {
            ShowError("Could not change input mode.", exception);
        }
    }

    private void UpdateModeChecks()
    {
        var current = _runtime.Mode.Mode;
        foreach (var pair in _modeItems)
            pair.Value.Checked = pair.Key == current;
    }

    private void ShowClipboardHistory()
    {
        try
        {
            _clipboard.ShowPickerAndPaste();
        }
        catch (Exception exception)
        {
            ShowError("Clipboard history failed.", exception);
        }
    }

    private async Task EditAndRunMacroAsync()
    {
        if (_macroCancellation is not null)
            return;

        try
        {
            var edited = _macroEditor.Edit(new MacroEditRequest("Ad-hoc", _macroTemplate, _macroRepeat));
            if (edited is null)
                return;

            _macroTemplate = edited.Template;
            _macroRepeat = edited.Repeat;
            _macroCancellation = new CancellationTokenSource();

            var result = await _macroExecutor.ExecuteAsync(
                _macroTemplate,
                _macroRepeat,
                _macroCancellation.Token);
            _macroTemplate = result.UpdatedTemplate;
            _macroRepeat = result.NextRepeat;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError("Macro execution failed.", exception);
        }
        finally
        {
            _macroCancellation?.Dispose();
            _macroCancellation = null;
        }
    }

    private void CancelMacros()
    {
        _macroCancellation?.Cancel();
        _legacyMacroSlots.Cancel();
    }

    private static void ShowError(string message, Exception exception)
        => MessageBox.Show(
            $"{message}\r\n\r\n{exception.Message}",
            "iKeyd",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
}
