using iKeyd.Core.Clipboard;

namespace iKeyd.Windows.Clipboard;

public sealed class WindowsClipboardPicker : IClipboardPicker, IClipboardPayloadPicker
{
    public int? Pick(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return null;

        return PickPreviews(items.Select(BuildPreview).ToArray());
    }

    public int? Pick(IReadOnlyList<ClipboardPayload> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return null;

        return PickPreviews(items.Select(BuildPayloadPreview).ToArray());
    }

    internal static string BuildPreview(string text)
    {
        const int maxLength = 240;
        var preview = text
            .Replace("\r\n", " ↵ ", StringComparison.Ordinal)
            .Replace("\r", " ↵ ", StringComparison.Ordinal)
            .Replace("\n", " ↵ ", StringComparison.Ordinal)
            .Replace("\t", " ⇥ ", StringComparison.Ordinal);
        return preview.Length <= maxLength ? preview : string.Concat(preview.AsSpan(0, maxLength - 1), "…");
    }

    internal static string BuildPayloadPreview(ClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Kind switch
        {
            ClipboardPayloadKind.Text => BuildPreview(payload.GetText()),
            ClipboardPayloadKind.Image => $"[Image · {payload.ContentType} · {FormatBytes(payload.Data.Length)}]",
            _ => $"[{payload.Kind} · {payload.ContentType} · {FormatBytes(payload.Data.Length)}]"
        };
    }

    private static int? PickPreviews(IReadOnlyList<string> previews)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return PickOnStaThread(previews);

        int? result = null;
        Exception? failure = null;
        var snapshot = previews.ToArray();
        var thread = new Thread(() =>
        {
            try
            {
                result = PickOnStaThread(snapshot);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "iKeyd Clipboard Picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("Clipboard picker failed.", failure);
        return result;
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:0.#} KiB";
        return $"{bytes / (1024d * 1024d):0.#} MiB";
    }

    private static int? PickOnStaThread(IReadOnlyList<string> previews)
    {
        using var form = new ClipboardPickerForm(previews);
        return form.ShowDialog() == DialogResult.OK ? form.SelectedIndex : null;
    }

    private sealed class ClipboardPickerForm : Form
    {
        private readonly ListBox _list = new()
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        private readonly ClipboardPickerActivationState _activationState = new();

        public ClipboardPickerForm(IReadOnlyList<string> previews)
        {
            Text = "Clipboard History";
            Width = 900;
            Height = 560;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            KeyPreview = true;

            for (var index = 0; index < previews.Count; index++)
                _list.Items.Add(new PickerItem(index, previews[index]));

            Controls.Add(_list);
            _list.DoubleClick += (_, _) => AcceptSelection();
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    AcceptSelection();
                }
            };
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.Handled = true;
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            Shown += (_, _) =>
            {
                _activationState.MarkShown();
                _list.SelectedIndex = 0;

                // The picker is invoked from a global low-level keyboard hook while
                // another application normally owns the foreground window. Keep it
                // above that application until Windows grants activation; otherwise
                // the old focus-loss handler can make the dialog disappear before it
                // is ever visible to the user.
                TopMost = true;
                BringToFront();
                Activate();
                _list.Focus();

                // Activated can run before Shown for a modal form. If that happened,
                // arm focus-loss closing now that the form is definitely visible.
                if (ReferenceEquals(ActiveForm, this))
                    ArmFocusLossClose();
            };
            Activated += (_, _) => ArmFocusLossClose();
            Deactivate += (_, _) =>
            {
                // Ignore the initial foreground hand-off. Once the picker has really
                // been shown and activated, preserve the historical focus-loss-close
                // behavior used by the tray path.
                if (!_activationState.CanCloseOnDeactivate || DialogResult != DialogResult.None)
                    return;

                DialogResult = DialogResult.Cancel;
                Close();
            };
        }

        public int SelectedIndex
            => _list.SelectedItem is PickerItem item ? item.Index : -1;

        private void ArmFocusLossClose()
        {
            if (!_activationState.MarkActivated())
                return;

            // TopMost is only an activation bootstrap/fallback. Do not leave the
            // picker permanently above unrelated applications after it owns focus.
            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed && !Disposing)
                    TopMost = false;
            }));
        }

        private void AcceptSelection()
        {
            if (_list.SelectedItem is not PickerItem)
                return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed record PickerItem(int Index, string Preview)
        {
            public override string ToString() => Preview;
        }
    }
}

internal sealed class ClipboardPickerActivationState
{
    private bool _shown;
    private bool _activatedAfterShow;

    public bool CanCloseOnDeactivate => _shown && _activatedAfterShow;

    public void MarkShown() => _shown = true;

    public bool MarkActivated()
    {
        if (!_shown || _activatedAfterShow)
            return false;

        _activatedAfterShow = true;
        return true;
    }
}
