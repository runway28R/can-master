namespace PcanCanMonitor;

public sealed class SettingsForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 20, 29);
    private static readonly Color Surface = Color.FromArgb(23, 31, 43);
    private static readonly Color Raised = Color.FromArgb(29, 39, 53);
    private static readonly Color Border = Color.FromArgb(48, 61, 80);
    private static readonly Color TextPrimary = Color.FromArgb(235, 241, 248);
    private static readonly Color TextMuted = Color.FromArgb(143, 158, 177);
    private static readonly Color Teal = Color.FromArgb(46, 205, 177);
    private readonly NumericUpDown _retainedMessagesBox;

    public SettingsForm(int currentMaxRetainedMessages)
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 245);
        BackColor = Canvas;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);

        var title = new Label
        {
            Text = "Traffic display",
            Dock = DockStyle.Fill,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var settingRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Canvas
        };
        settingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        settingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        var label = new Label
        {
            Text = "Messages retained in the table",
            Dock = DockStyle.Fill,
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        settingRow.Controls.Add(label, 0, 0);

        _retainedMessagesBox = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = AppSettings.MinMaxRetainedMessages,
            Maximum = AppSettings.MaxMaxRetainedMessages,
            Value = Math.Clamp(currentMaxRetainedMessages, AppSettings.MinMaxRetainedMessages, AppSettings.MaxMaxRetainedMessages),
            ThousandsSeparator = true,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        settingRow.Controls.Add(_retainedMessagesBox, 1, 0);
        root.Controls.Add(settingRow, 0, 1);

        var info = new Label
        {
            Text = "When the limit is reached, the oldest retained messages are discarded. The default is 5,000.",
            Dock = DockStyle.Fill,
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        };
        root.Controls.Add(info, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Canvas,
            Padding = new Padding(0, 8, 0, 0)
        };
        var save = CreateButton("Save", Teal, Raised, 90);
        save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var cancel = CreateButton("Cancel", TextMuted, Raised, 90);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);

        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            _retainedMessagesBox.Focus();
            _retainedMessagesBox.Select(0, _retainedMessagesBox.Text.Length);
        };
    }

    public int MaxRetainedMessages => (int)_retainedMessagesBox.Value;

    private static Button CreateButton(string text, Color foreground, Color background, int width)
        => new()
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = Border },
            BackColor = background,
            ForeColor = foreground,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Margin = new Padding(8, 0, 0, 0)
        };
}
