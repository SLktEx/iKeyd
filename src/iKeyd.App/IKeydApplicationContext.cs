using iKeyd.Core.Clipboard;
using iKeyd.Core.Macros;
using iKeyd.Core.Modes;
using iKeyd.Profiles.HotkeySkg.Runtime;
using iKeyd.Windows.Clipboard;
using iKeyd.Windows.Desktop;
using iKeyd.Windows.Input;
using iKeyd.Windows.Macros;

namespace iKeyd.App;

internal sealed class IKeydApplicationContext : ApplicationContext, IHotkeySkgInteractiveActions
{
    private readonly WindowsKeyboardBackend _keyboard;
    private readonly IKeydRuntimeHandler _runtime;
    private readonly WindowsClipboardService _clipboardService;
    private readonly WindowsClipboardController _clipboard;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Dictionary<InputMode, ToolStripMenuItem> _modeItems = [];
    private readonly ToolStripMenuItem _cancelMacroItem;
    private readonly WindowsMacroEditor _macroEditor = new();
    private readonly MacroExecutor _macroExecutor;
    private readonly object _macroGate = new();
    private readonly Dictionary<char, string> _legacyMacros = new()
    {
        ['H'] = string.Empty,
        ['Y'] = string.Empty
    };
    private readonly SynchronizationContext _uiContext;

    private string _adHocMacroTemplate = string.Empty;
    private MacroRepeat _macroRepeat = MacroRepeat.Once;
    private CancellationTokenSource? _macroCancellation;
    private bool _stopping;

    public IKeydApplicationContext(IKeydConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _keyboard = new WindowsKeyboardBackend();
        var desktop = new WindowsDesktopBackend();
        var send = new LegacySendOutput(_keyboard, desktop);
        _runtime = new IKeydRuntimeHandler(
            configuration,
            new WindowsInputMethod(),
            _keyboard.State,
            send,
            desktop,
            this);

        _clipboardService = new WindowsClipboardService();
        _clipboard = new WindowsClipboardController(
            _clipboardService,
            new ClipboardHistory(),
            new WindowsClipboardPicker(),
            _keyboard);

        _macroExecutor = new MacroExecutor(send, _runtime);

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

        _menu.Items.Add(new ToolStripMenuItem("Clipboard History...", null, (_, _) => ShowClipboardHistoryCore()));
        _menu.Items.Add(new ToolStripMenuItem("Macro...", null, async (_, _) => await EditAndRunAdHocMacroAsync()));
        _cancelMacroItem = new ToolStripMenuItem("Cancel Macro", null, (_, _) => CancelMacro())
        {
            Enabled = false
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
        _notifyIcon.DoubleClick += (_, _) => ShowClipboardHistoryCore();

        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        UpdateModeChecks();
        _keyboard.Start(_runtime);
    }

    public void RunMacro(char slot)
    {
        slot = NormalizeMacroSlot(slot);
        _ = Task.Run(() => RunLegacyMacroAsync(slot));
    }

    public void EditMacro(char slot)
    {
        slot = NormalizeMacroSlot(slot);
        _ = Task.Run(() => EditLegacyMacroCore(slot));
    }

    public void EditMacroRepeat()
        => _ = Task.Run(EditMacroRepeatCore);

    public void ShowClipboardHistory()
        => _ = Task.Run(() => RunInteractiveAction(ShowClipboardHistoryCore, "Clipboard history failed."));

    public void CaptureLatestClipboard()
        => _ = Task.Run(() => RunInteractiveAction(() => _clipboard.CaptureLatest(), "Could not capture clipboard history item."));

    public void PasteCapturedClipboard()
        => _ = Task.Run(() => RunInteractiveAction(() => _clipboard.PasteCaptured(), "Could not paste captured clipboard history item."));

    protected override void ExitThreadCore()
    {
        if (_stopping)
            return;
        _stopping = true;

        CancelMacro();
        _notifyIcon.Visible = false;

        try
        {
            _keyboard.Stop();
        }
        finally
        {
            _clipboard.Dispose();
            _clipboardService.Dispose();
            _runtime.Dispose();
            _keyboard.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        base.ExitThreadCore();
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

    private void ShowClipboardHistoryCore()
    {
        try
        {
            _clipboard.ShowPickerAndPaste();
        }
        catch (Exception exception)
        {
            ShowErrorOnUi("Clipboard history failed.", exception);
        }
    }

    private async Task EditAndRunAdHocMacroAsync()
    {
        MacroRepeat repeat;
        lock (_macroGate)
            repeat = _macroRepeat;

        var edited = _macroEditor.Edit(new MacroEditRequest("Ad-hoc", _adHocMacroTemplate, repeat));
        if (edited is null)
            return;

        _adHocMacroTemplate = edited.Template;
        lock (_macroGate)
            _macroRepeat = edited.Repeat;

        await RunMacroTemplateAsync(
            () => _adHocMacroTemplate,
            value => _adHocMacroTemplate = value,
            edited.Repeat);
    }

    private async Task RunLegacyMacroAsync(char slot)
    {
        string template;
        MacroRepeat repeat;
        lock (_macroGate)
        {
            template = _legacyMacros[slot];
            repeat = _macroRepeat;
        }

        await RunMacroTemplateAsync(
            () =>
            {
                lock (_macroGate)
                    return _legacyMacros[slot];
            },
            value =>
            {
                lock (_macroGate)
                    _legacyMacros[slot] = value;
            },
            repeat);
    }

    private async Task RunMacroTemplateAsync(
        Func<string> getTemplate,
        Action<string> setTemplate,
        MacroRepeat repeat)
    {
        CancellationTokenSource cancellation;
        lock (_macroGate)
        {
            if (_macroCancellation is not null || _stopping)
                return;
            cancellation = new CancellationTokenSource();
            _macroCancellation = cancellation;
        }
        SetCancelMacroEnabled(true);

        try
        {
            var result = await _macroExecutor.ExecuteAsync(getTemplate(), repeat, cancellation.Token);
            setTemplate(result.UpdatedTemplate);
            lock (_macroGate)
                _macroRepeat = result.NextRepeat;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowErrorOnUi("Macro execution failed.", exception);
        }
        finally
        {
            lock (_macroGate)
            {
                if (ReferenceEquals(_macroCancellation, cancellation))
                    _macroCancellation = null;
            }
            cancellation.Dispose();
            SetCancelMacroEnabled(false);
        }
    }

    private void EditLegacyMacroCore(char slot)
    {
        try
        {
            string template;
            MacroRepeat repeat;
            lock (_macroGate)
            {
                template = _legacyMacros[slot];
                repeat = _macroRepeat;
            }

            var edited = _macroEditor.Edit(new MacroEditRequest(slot.ToString(), template, repeat));
            if (edited is null)
                return;

            // Legacy MH edits only the selected macro; its loop count is shared and unchanged.
            lock (_macroGate)
                _legacyMacros[slot] = edited.Template;
        }
        catch (Exception exception)
        {
            ShowErrorOnUi($"Could not edit Macro {slot}.", exception);
        }
    }

    private void EditMacroRepeatCore()
    {
        try
        {
            MacroRepeat repeat;
            lock (_macroGate)
                repeat = _macroRepeat;

            var edited = _macroEditor.Edit(new MacroEditRequest("Loop", string.Empty, repeat));
            if (edited is null)
                return;

            // Legacy HM edits only macronum; ignore any template text in this editor invocation.
            lock (_macroGate)
                _macroRepeat = edited.Repeat;
        }
        catch (Exception exception)
        {
            ShowErrorOnUi("Could not edit macro loop count.", exception);
        }
    }

    private void CancelMacro()
    {
        lock (_macroGate)
            _macroCancellation?.Cancel();
    }

    private void SetCancelMacroEnabled(bool enabled)
        => _uiContext.Post(_ =>
        {
            if (!_stopping)
                _cancelMacroItem.Enabled = enabled;
        }, null);

    private void RunInteractiveAction(Action action, string errorMessage)
    {
        try
        {
            if (!_stopping)
                action();
        }
        catch (Exception exception)
        {
            ShowErrorOnUi(errorMessage, exception);
        }
    }

    private void RunInteractiveAction(Func<bool> action, string errorMessage)
        => RunInteractiveAction(() => _ = action(), errorMessage);

    private void ShowErrorOnUi(string message, Exception exception)
        => _uiContext.Post(_ =>
        {
            if (!_stopping)
                ShowError(message, exception);
        }, null);

    private static char NormalizeMacroSlot(char slot)
    {
        var normalized = char.ToUpperInvariant(slot);
        if (normalized is not ('H' or 'Y'))
            throw new ArgumentOutOfRangeException(nameof(slot), "Legacy hotkeySKG has macro slots H and Y only.");
        return normalized;
    }

    private static void ShowError(string message, Exception exception)
        => MessageBox.Show(
            $"{message}\r\n\r\n{exception.Message}",
            "iKeyd",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
}
