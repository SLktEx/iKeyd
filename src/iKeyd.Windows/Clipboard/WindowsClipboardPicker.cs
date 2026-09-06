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
                _list.SelectedIndex = 0;
                _list.Focus();
            };
            Deactivate += (_, _) =>
            {
                if (DialogResult == DialogResult.None)
                    DialogResult = DialogResult.Cancel;
                Close();
            };
        }

        public int SelectedIndex
            => _list.SelectedItem is PickerItem item ? item.Index : -1;

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
