using iKeyd.Core.Clipboard;
using iKeyd.Core.Configuration;
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
    private readonly WindowsClipboardService _clipboardService;
    private readonly WindowsClipboardController _clipboard;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Control _uiDispatcher = new();
    private readonly Dictionary<InputMode, ToolStripMenuItem> _modeItems = [];
    private readonly ToolStripMenuItem _cancelMacroItem;
    private readonly WindowsMacroEditor _macroEditor = new();
    private readonly MacroExecutor _macroExecutor;

    private string _macroTemplate = string.Empty;
    private MacroRepeat _macroRepeat = MacroRepeat.Once;
    private CancellationTokenSource? _macroCancellation;
    private bool _stopping;

    public IKeydApplicationContext(IKeydConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _uiDispatcher.CreateControl();

        _keyboard = new WindowsKeyboardBackend();
        var desktop = new WindowsDesktopBackend();
        var send = new LegacySendOutput(_keyboard, desktop);
        _runtime = new IKeydRuntimeHandler(
            configuration,
            new WindowsInputMethod(),
            _keyboard.State,
            send,
            desktop);

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

        _menu.Items.Add(new ToolStripMenuItem("Clipboard History...", null, (_, _) => ShowClipboardHistory()));
        _menu.Items.Add(new ToolStripMenuItem("Macro...", null, async (_, _) => await EditAndRunMacroAsync()));
        _cancelMacroItem = new ToolStripMenuItem("Cancel Macro", null, (_, _) => _macroCancellation?.Cancel())
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
        _notifyIcon.DoubleClick += (_, _) => ShowClipboardHistory();

        var hostActions = new DelegateConfiguredHostActionSink(PostConfiguredHostAction);
        UpdateModeChecks();
        _keyboard.Start(new ConfiguredBehaviorKeyboardHandler(
            configuration.Profile.KeyBehaviors,
            _keyboard,
            desktop,
            hostActions,
            _runtime));
    }

    protected override void ExitThreadCore()
    {
        if (_stopping)
            return;
        _stopping = true;

        _macroCancellation?.Cancel();
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
            _uiDispatcher.Dispose();
            _macroCancellation?.Dispose();
            _macroCancellation = null;
        }

        base.ExitThreadCore();
    }

    private void PostConfiguredHostAction(KeyBehaviorAction action)
    {
        if (_stopping || _uiDispatcher.IsDisposed)
            return;

        _uiDispatcher.BeginInvoke((Action)(() => DispatchConfiguredHostAction(action)));
    }

    private void DispatchConfiguredHostAction(KeyBehaviorAction action)
    {
        if (_stopping)
            return;

        switch (action.Kind)
        {
            case KeyBehaviorActionKind.Clipboard when string.Equals(action.Value, "History", StringComparison.OrdinalIgnoreCase):
                ShowClipboardHistory();
                break;
            case KeyBehaviorActionKind.Macro:
                _ = RunConfiguredMacroAsync(action.Value);
                break;
            default:
                ShowError("Configured host action failed.", new InvalidOperationException($"Unsupported host action '{action.Kind}:{action.Value}'."));
                break;
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

    private async Task RunConfiguredMacroAsync(string template)
    {
        if (_macroCancellation is not null)
            return;

        try
        {
            _macroCancellation = new CancellationTokenSource();
            _cancelMacroItem.Enabled = true;
            await _macroExecutor.ExecuteAsync(template, MacroRepeat.Once, _macroCancellation.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError("Configured macro failed.", exception);
        }
        finally
        {
            _cancelMacroItem.Enabled = false;
            _macroCancellation?.Dispose();
            _macroCancellation = null;
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
            _cancelMacroItem.Enabled = true;

            var result = await _macroExecutor.ExecuteAsync(_macroTemplate, _macroRepeat, _macroCancellation.Token);
            _macroTemplate = result.UpdatedTemplate;
            _macroRepeat = result.NextRepeat;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError("Macro execution failed.", exception);
        }
        finally
        {
            _cancelMacroItem.Enabled = false;
            _macroCancellation?.Dispose();
            _macroCancellation = null;
        }
    }

    private static void ShowError(string message, Exception exception)
        => MessageBox.Show(
            $"{message}\r\n\r\n{exception.Message}",
            "iKeyd",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
}
