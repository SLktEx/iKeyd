using iKeyd.Core.Macros;

namespace iKeyd.Windows.Macros;

public sealed class WindowsMacroEditor : IMacroEditor
{
    public MacroEditResult? Edit(MacroEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return EditOnStaThread(request);

        MacroEditResult? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = EditOnStaThread(request);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "iKeyd Macro Editor"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("Macro editor failed.", failure);
        return result;
    }

    private static MacroEditResult? EditOnStaThread(MacroEditRequest request)
    {
        using var form = new MacroEditorForm(request);
        return form.ShowDialog() == DialogResult.OK ? form.Result : null;
    }

    private sealed class MacroEditorForm : Form
    {
        private readonly TextBox _template = new()
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };

        private readonly TextBox _repeat = new() { Dock = DockStyle.Fill };
        private readonly Label _validation = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill
        };

        public MacroEditorForm(MacroEditRequest request)
        {
            Text = $"Macro {request.Name}";
            Width = 900;
            Height = 620;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            _template.Text = request.Template;
            _repeat.Text = request.Repeat.ToString();

            var help = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = "Legacy syntax: {UP} {LEFT} {RIGHT} {DOWN} {HOME} {END} {PgUp} {PgDn}  " +
                       "{TAB} {BS} {DEL} {ENTER} {INS} {Esc} {AppsKey}\r\n" +
                       "^ Ctrl   ! Alt   + Shift   # Windows   `increment`   {hk MHr}   " +
                       "{Wait 1000}   {Calc (1+2)*3}\r\n" +
                       "Repeat: n runs n times; +n keeps that repeat setting for the next invocation."
            };

            var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label { Text = "Macro", AutoSize = true }, 0, 0);
            layout.Controls.Add(_template, 1, 0);
            layout.Controls.Add(new Label { Text = "Repeat", AutoSize = true }, 0, 1);
            layout.Controls.Add(_repeat, 1, 1);
            layout.Controls.Add(help, 0, 2);
            layout.SetColumnSpan(help, 2);
            layout.Controls.Add(_validation, 0, 3);
            layout.SetColumnSpan(_validation, 2);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;
            FormClosing += ValidateBeforeClose;
        }

        public MacroEditResult? Result { get; private set; }

        private void ValidateBeforeClose(object? sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
                return;

            try
            {
                Result = new MacroEditResult(_template.Text, MacroRepeat.Parse(_repeat.Text));
                _validation.Text = string.Empty;
            }
            catch (FormatException exception)
            {
                _validation.Text = exception.Message;
                DialogResult = DialogResult.None;
                e.Cancel = true;
                _repeat.Focus();
                _repeat.SelectAll();
            }
        }
    }
}
