using System.Globalization;
using System.Text;

namespace PcanCanMonitor;

public sealed class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 20, 29);
    private static readonly Color Surface = Color.FromArgb(23, 31, 43);
    private static readonly Color SurfaceRaised = Color.FromArgb(29, 39, 53);
    private static readonly Color Border = Color.FromArgb(48, 61, 80);
    private static readonly Color TextPrimary = Color.FromArgb(235, 241, 248);
    private static readonly Color TextMuted = Color.FromArgb(143, 158, 177);
    private static readonly Color Teal = Color.FromArgb(46, 205, 177);
    private static readonly Color Amber = Color.FromArgb(249, 183, 73);
    private static readonly Color RtdGreen = Color.FromArgb(76, 207, 125);
    private static readonly Color Red = Color.FromArgb(246, 103, 103);
    private const float DetailBodyFontSize = 8.5F;

    private readonly TrafficBuffer _traffic = new();
    private readonly IdentifierRegistry _registry = new();
    private readonly PcanMonitorService _monitor = new();
    private readonly List<CanFrame> _visibleFrames = [];
    private readonly Font _primarySelectedRowFont = new("Segoe UI", 9F, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private DataGridView _grid = null!;
    private TextBox _searchBox = null!;
    private ComboBox _frameTypeFilter = null!;
    private Label _statusLabel = null!;
    private Label _statusDot = null!;
    private Label _hardwareLabel = null!;
    private Label _captureLabel = null!;
    private Label _selectedHint = null!;
    private Label _rtdStatusLabel = null!;
    private Label _detailBusInfo = null!;
    private Label _detailHeading = null!;
    private Label _detailId = null!;
    private Label _detailType = null!;
    private Label _detailTime = null!;
    private Label _detailSender = null!;
    private Label _detailLength = null!;
    private Label _detailMessage = null!;
    private TableLayoutPanel _detailFieldsTable = null!;
    private Panel _detailEmpty = null!;
    private Panel _detailContent = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private ToolStripMenuItem _identifierListsMenu = null!;
    private SplitContainer _mainSplit = null!;
    private long _version;
    private long _renderedVersion = -1;
    private bool _hasRtdState;
    private RtdState _lastRtdState = RtdState.Unknown;
    private bool _syncingIdentifierSelection;
    private int _primarySelectedRowIndex = -1;
    private bool _closing;

    public MainForm()
    {
        Text = "PCAN CAN Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 690);
        ClientSize = new Size(1420, 850);
        BackColor = Canvas;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildMenu();
        BuildLayout();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _refreshTimer.Tick += (_, _) => RefreshGridIfNeeded();
        _refreshTimer.Start();

        _monitor.FrameReceived += MonitorOnFrameReceived;
        _monitor.StatusChanged += MonitorOnStatusChanged;
        AppLog.Info("Main window initialized.");
        Load += (_, _) => ConfigureMainSplit();
        Shown += async (_, _) => await _monitor.StartAsync();
        FormClosing += MainFormOnClosing;
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Surface,
            ForeColor = Color.White,
            Renderer = new DarkMenuRenderer()
        };

        var file = new ToolStripMenuItem("File") { ForeColor = Color.White };
        file.DropDown.ForeColor = Color.White;
        file.DropDown.Renderer = new DarkMenuRenderer();
        file.DropDownItems.Add(CreateMenuItem("Import capture…", "Ctrl+O", (_, _) => ImportCapture()));
        file.DropDownItems.Add(CreateMenuItem("Export retained traffic…", "Ctrl+S", (_, _) => ExportCapture()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("Exit", "Alt+F4", (_, _) => Close()));

        var identifiers = new ToolStripMenuItem("Identifiers") { ForeColor = Color.White };
        identifiers.DropDown.ForeColor = Color.White;
        identifiers.DropDown.Renderer = new DarkMenuRenderer();
        _identifierListsMenu = new ToolStripMenuItem { ForeColor = Color.White };
        _identifierListsMenu.DropDown.ForeColor = Color.White;
        _identifierListsMenu.DropDown.Renderer = new DarkMenuRenderer();
        identifiers.DropDownItems.Add(_identifierListsMenu);
        identifiers.DropDownItems.Add(new ToolStripSeparator());
        identifiers.DropDownItems.Add(CreateMenuItem("Name observed identifiers…", string.Empty, (_, _) => OpenIdentifierEditor()));
        identifiers.DropDownItems.Add(CreateMenuItem("Edit message declarations…", string.Empty, (_, _) => OpenMessageEditor()));
        identifiers.DropDownItems.Add(CreateMenuItem("Open identifier file", string.Empty, (_, _) => OpenIdentifierFile()));

        var view = new ToolStripMenuItem("View") { ForeColor = Color.White };
        view.DropDown.ForeColor = Color.White;
        view.DropDown.Renderer = new DarkMenuRenderer();
        view.DropDownItems.Add(CreateMenuItem("Clear retained traffic", string.Empty, (_, _) => ClearTraffic()));
        view.DropDownItems.Add(CreateMenuItem("Reset filters", string.Empty, (_, _) => ResetFilters()));

        menu.Items.Add(file);
        menu.Items.Add(identifiers);
        menu.Items.Add(view);
        Controls.Add(menu);
        MainMenuStrip = menu;
        RefreshIdentifierListMenu();
    }

    private void RefreshIdentifierListMenu()
    {
        _identifierListsMenu.Text = $"List: {_registry.ActiveListName}";
        _identifierListsMenu.DropDownItems.Clear();

        foreach (var listName in _registry.ListNames)
        {
            var item = CreateMenuItem(listName, string.Empty, (_, _) => SelectIdentifierList(listName));
            item.Checked = string.Equals(listName, _registry.ActiveListName, StringComparison.OrdinalIgnoreCase);
            _identifierListsMenu.DropDownItems.Add(item);
        }

        _identifierListsMenu.DropDownItems.Add(new ToolStripSeparator());
        _identifierListsMenu.DropDownItems.Add(CreateMenuItem("Create new list…", string.Empty, (_, _) => CreateIdentifierList()));
    }

    private void SelectIdentifierList(string listName)
    {
        try
        {
            _registry.SelectList(listName);
            RefreshIdentifierListMenu();
            RemapTrafficNames();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not select identifier list '{listName}'.", exception);
            MessageBox.Show(this, exception.Message, "Identifier list", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateIdentifierList()
    {
        using var dialog = new IdentifierListNameForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            _registry.CreateList(dialog.ListName);
            RefreshIdentifierListMenu();
            RemapTrafficNames();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not create identifier list '{dialog.ListName}'.", exception);
            MessageBox.Show(this, exception.Message, "Identifier list", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static ToolStripMenuItem CreateMenuItem(string text, string shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.White,
            ShortcutKeyDisplayString = shortcut
        };
        item.Click += handler;
        return item;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Canvas,
            Padding = new Padding(18, 12, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 7,
            BackColor = Canvas,
        };
        _mainSplit = split;
        split.Panel1.Controls.Add(BuildMessagePanel());
        split.Panel2.Controls.Add(BuildDetailPanel());
        root.Controls.Add(split, 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
    }

    private void ConfigureMainSplit()
    {
        const int leftMinimum = 600;
        const int rightMinimum = 310;
        if (_mainSplit.Width < leftMinimum + rightMinimum + _mainSplit.SplitterWidth)
            return;
        _mainSplit.Panel1MinSize = leftMinimum;
        _mainSplit.Panel2MinSize = rightMinimum;
        _mainSplit.SplitterDistance = Math.Min(900, _mainSplit.Width - rightMinimum - _mainSplit.SplitterWidth);
        AppLog.Info($"Main split layout configured: width={_mainSplit.Width}; splitter={_mainSplit.SplitterDistance}.");
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        var title = new Label
        {
            Text = "PCAN CAN Monitor",
            AutoSize = true,
            Location = new Point(0, 3),
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        var subtitle = new Label
        {
            Text = "Receive-only observation · PEAK-System PCAN-USB",
            AutoSize = true,
            Location = new Point(2, 39),
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextMuted
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);

        var right = new Panel { Dock = DockStyle.Right, Width = 620 };
        _captureLabel = MakeLabel("0 / 5000 retained", TextMuted, 9.5F, ContentAlignment.MiddleRight);
        _captureLabel.Dock = DockStyle.Right;
        _captureLabel.Width = 130;
        right.Controls.Add(_captureLabel);
        var hardwareCard = new Panel
        {
            Dock = DockStyle.Right,
            Width = 250,
            BackColor = Surface,
            Padding = new Padding(12, 5, 12, 5)
        };
        _hardwareLabel = MakeLabel("PCAN-USB · scanning…", TextPrimary, 9F, ContentAlignment.MiddleLeft);
        _hardwareLabel.Dock = DockStyle.Fill;
        hardwareCard.Controls.Add(_hardwareLabel);
        right.Controls.Add(hardwareCard);

        var statusCard = new Panel { Dock = DockStyle.Right, Width = 190, BackColor = Surface, Padding = new Padding(11, 5, 10, 5) };
        _statusDot = MakeLabel("●", Amber, 11F, ContentAlignment.MiddleCenter);
        _statusDot.Dock = DockStyle.Left;
        _statusDot.Width = 20;
        _statusLabel = MakeLabel("SCANNING", Amber, 8.5F, ContentAlignment.MiddleLeft);
        _statusLabel.Dock = DockStyle.Fill;
        statusCard.Controls.Add(_statusLabel);
        statusCard.Controls.Add(_statusDot);
        right.Controls.Add(statusCard);
        panel.Controls.Add(right);
        return panel;
    }

    private Control BuildToolbar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(10, 7, 10, 7) };
        _startButton = MakeButton("Start capture", Teal, SurfaceRaised, 120);
        _startButton.Click += async (_, _) => await StartCapture();
        _stopButton = MakeButton("Stop", TextMuted, SurfaceRaised, 76);
        _stopButton.Enabled = false;
        _stopButton.Click += async (_, _) => await StopCapture();
        var clear = MakeButton("Clear", TextMuted, SurfaceRaised, 72);
        clear.Click += (_, _) => ClearTraffic();
        var export = MakeButton("Export", TextPrimary, SurfaceRaised, 82);
        export.Click += (_, _) => ExportCapture();
        var import = MakeButton("Import", TextPrimary, SurfaceRaised, 82);
        import.Click += (_, _) => ImportCapture();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 455, WrapContents = false, BackColor = Color.Transparent };
        buttons.Controls.AddRange([_startButton, _stopButton, clear, export, import]);
        panel.Controls.Add(buttons);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 440, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent };
        var searchLabel = MakeLabel("FILTER", TextMuted, 8F, ContentAlignment.MiddleCenter);
        searchLabel.Width = 48;
        filters.Controls.Add(searchLabel);
        _searchBox = new TextBox
        {
            Width = 205,
            Height = 28,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            PlaceholderText = "ID, name, data…",
            Margin = new Padding(2, 2, 10, 2)
        };
        _searchBox.TextChanged += (_, _) => RefreshGrid();
        filters.Controls.Add(_searchBox);
        _frameTypeFilter = new ComboBox
        {
            Width = 145,
            Height = 28,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 2, 0, 2)
        };
        _frameTypeFilter.Items.AddRange(["All frame types", "Standard", "Extended", "Remote"]);
        _frameTypeFilter.SelectedIndex = 0;
        _frameTypeFilter.SelectedIndexChanged += (_, _) => RefreshGrid();
        filters.Controls.Add(_frameTypeFilter);
        panel.Controls.Add(filters);
        return panel;
    }

    private Control BuildMessagePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(1) };
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Border,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 29 },
            VirtualMode = true,
            AutoGenerateColumns = false,
            ScrollBars = ScrollBars.Both
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceRaised,
            ForeColor = TextMuted,
            SelectionBackColor = SurfaceRaised,
            SelectionForeColor = TextMuted,
            Font = new Font("Segoe UI Semibold", 8F),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 4, 0)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = TextPrimary,
            SelectionBackColor = Color.FromArgb(37, 78, 81),
            SelectionForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9F),
            Padding = new Padding(8, 0, 4, 0),
            NullValue = string.Empty
        };
        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(25, 34, 47) };
        AddColumn("Time", "time", 102);
        AddColumn("Identifier", "id", 105);
        AddColumn("Name", "name", 160);
        AddColumn("Data", "data", 250);
        AddColumn("Message", "message", 250, DataGridViewAutoSizeColumnMode.Fill);
        _grid.CellValueNeeded += GridOnCellValueNeeded;
        _grid.SelectionChanged += GridOnSelectionChanged;
        _grid.CellFormatting += GridOnCellFormatting;
        panel.Controls.Add(_grid);
        return panel;
    }

    private void AddColumn(string header, string name, int width, DataGridViewAutoSizeColumnMode mode = DataGridViewAutoSizeColumnMode.None)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            Name = name,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = mode == DataGridViewAutoSizeColumnMode.None
                ? DataGridViewAutoSizeColumnMode.AllCells
                : mode
        };
        _grid.Columns.Add(column);
    }

    private Control BuildDetailPanel()
    {
        var outer = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(18, 16, 18, 12) };
        _rtdStatusLabel = MakeLabel("RTD: —", TextMuted, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _rtdStatusLabel.Dock = DockStyle.Top;
        _rtdStatusLabel.Height = 28;

        _detailBusInfo = MakeLabel("Channel · bitrate: —", TextMuted, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _detailBusInfo.Dock = DockStyle.Top;
        _detailBusInfo.Height = 25;

        _detailHeading = MakeLabel("FRAME INSPECTOR", TextMuted, 8.5F, ContentAlignment.BottomLeft);
        _detailHeading.Padding = new Padding(0, 0, 0, 3);
        _detailHeading.Dock = DockStyle.Top;
        _detailHeading.Height = 25;
        outer.Controls.Add(_detailHeading);
        outer.Controls.Add(_rtdStatusLabel);
        outer.Controls.Add(_detailBusInfo);

        _detailEmpty = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        var emptyStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Surface, Padding = new Padding(10, 160, 10, 0) };
        emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        emptyStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var emptyIcon = MakeLabel("⌁", Teal, 27F, ContentAlignment.MiddleCenter);
        var emptyTitle = MakeLabel("Select a frame", TextPrimary, 12F, ContentAlignment.TopCenter);
        var emptyText = MakeLabel("Decoded CAN fields will appear here.", TextMuted, 9F, ContentAlignment.TopCenter);
        emptyStack.Controls.Add(emptyIcon, 0, 0);
        emptyStack.Controls.Add(emptyTitle, 0, 1);
        emptyStack.Controls.Add(emptyText, 0, 2);
        _detailEmpty.Controls.Add(emptyStack);
        _detailEmpty.Visible = false;
        outer.Controls.Add(_detailEmpty);
        _detailHeading.Visible = false;
        _rtdStatusLabel.Visible = false;
        _detailBusInfo.Visible = false;

        _detailContent = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Visible = false, AutoScroll = true };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Surface,
            Padding = new Padding(0, 4, 0, 0)
        };
        content.Controls.Add(BuildDetailIdentityCard());
        _detailContent.Controls.Add(content);
        outer.Controls.Add(_detailContent);
        return outer;
    }

    private Control BuildDetailIdentityCard()
    {
        var card = new Panel
        {
            Width = 330,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(330, 0),
            BackColor = SurfaceRaised,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 12)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 7,
            BackColor = SurfaceRaised,
            Padding = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (var i = 0; i < 6; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _detailId = MakeLabel("—", TextPrimary, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _detailType = MakeLabel("—", Teal, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _detailSender = MakeLabel("—", TextPrimary, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _detailLength = MakeLabel("—", TextPrimary, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _detailMessage = MakeLabel("—", TextPrimary, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        _detailTime = MakeLabel("—", TextMuted, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        AddInspectorRow(table, 0, "Identifier", _detailId);
        AddInspectorRow(table, 1, "Frame", _detailType);
        AddInspectorRow(table, 2, "Sender", _detailSender);
        AddInspectorRow(table, 3, "Length", _detailLength);
        AddInspectorRow(table, 4, "Message", _detailMessage);
        AddInspectorRow(table, 5, "Time", _detailTime);

        _detailFieldsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = SurfaceRaised,
            Margin = new Padding(0, 7, 0, 0),
            Padding = new Padding(0)
        };
        _detailFieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        _detailFieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        table.Controls.Add(_detailFieldsTable, 0, 6);
        table.SetColumnSpan(_detailFieldsTable, 2);
        RenderDecodedFields([]);
        card.Controls.Add(table);
        return card;
    }

    private static void AddInspectorRow(TableLayoutPanel table, int row, string key, Control value)
    {
        var keyLabel = MakeLabel(key, TextMuted, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        value.Dock = DockStyle.Fill;
        table.Controls.Add(keyLabel, 0, row);
        table.Controls.Add(value, 1, row);
    }

    private void RenderDecodedFields(IReadOnlyList<CanDecodedField> fields)
    {
        _detailFieldsTable.SuspendLayout();
        _detailFieldsTable.Controls.Clear();
        _detailFieldsTable.RowStyles.Clear();
        _detailFieldsTable.RowCount = Math.Max(2, fields.Count + 1);
        _detailFieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var header = MakeLabel("Decoded fields", TextMuted, DetailBodyFontSize, ContentAlignment.MiddleLeft);
        header.Dock = DockStyle.Fill;
        _detailFieldsTable.Controls.Add(header, 0, 0);
        _detailFieldsTable.SetColumnSpan(header, 2);

        if (fields.Count == 0)
        {
            _detailFieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            var empty = MakeLabel("No decoded fields declared", TextMuted, DetailBodyFontSize, ContentAlignment.MiddleLeft);
            empty.Dock = DockStyle.Fill;
            _detailFieldsTable.Controls.Add(empty, 0, 1);
            _detailFieldsTable.SetColumnSpan(empty, 2);
        }
        else
        {
            for (var i = 0; i < fields.Count; i++)
            {
                _detailFieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
                var field = fields[i];
                var name = MakeLabel(field.Name, TextPrimary, DetailBodyFontSize, ContentAlignment.MiddleLeft);
                var value = MakeLabel(field.Value, Teal, DetailBodyFontSize, ContentAlignment.MiddleLeft);
                name.Dock = DockStyle.Fill;
                value.Dock = DockStyle.Fill;
                _detailFieldsTable.Controls.Add(name, 0, i + 1);
                _detailFieldsTable.Controls.Add(value, 1, i + 1);
            }
        }

        _detailFieldsTable.ResumeLayout(true);
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        _selectedHint = MakeLabel("No frame selected", TextMuted, 8.5F, ContentAlignment.MiddleLeft);
        _selectedHint.Dock = DockStyle.Left;
        _selectedHint.Width = 250;
        footer.Controls.Add(_selectedHint);
        var right = MakeLabel("Receive-only observer · buffer limit 5,000 frames", TextMuted, 8.5F, ContentAlignment.MiddleRight);
        right.Dock = DockStyle.Fill;
        footer.Controls.Add(right);
        return footer;
    }

    private void MonitorOnFrameReceived(object? sender, CanFrame frame)
    {
        var name = _registry.GetName(frame.Identifier, frame.IsExtended);
        _traffic.Add(frame with { IdentifierName = name });
        Interlocked.Increment(ref _version);

        if (CanFrameDecoder.TryGetRtdState(frame, out var rtdState))
        {
            _lastRtdState = rtdState;
            _hasRtdState = true;
            try
            {
                BeginInvoke(() => ApplyRtdStatus(rtdState));
            }
            catch (InvalidOperationException) { }
        }
    }

    private void ApplyRtdStatus(RtdState state)
    {
        _rtdStatusLabel.Text = state switch
        {
            RtdState.TrueValue => "RTD: True",
            RtdState.FalseValue => "RTD: False",
            _ => "RTD: Unknown"
        };
        _rtdStatusLabel.ForeColor = state switch
        {
            RtdState.TrueValue => Amber,
            RtdState.FalseValue => RtdGreen,
            _ => Red
        };
    }

    private void UpdateRtdStatusFromFrames(IEnumerable<CanFrame> frames)
    {
        foreach (var frame in frames)
        {
            if (!CanFrameDecoder.TryGetRtdState(frame, out var rtdState))
                continue;
            _lastRtdState = rtdState;
            _hasRtdState = true;
        }

        if (_hasRtdState)
            ApplyRtdStatus(_lastRtdState);
    }

    private void MonitorOnStatusChanged(object? sender, MonitorStatusEventArgs status)
    {
        AppLog.Info($"Monitor status: {status.Mode}; {status.Message}");
        if (IsDisposed || _closing)
            return;
        try
        {
            BeginInvoke(() => ApplyStatus(status));
        }
        catch (InvalidOperationException) { }
    }

    private void ApplyStatus(MonitorStatusEventArgs status)
    {
        var color = status.Mode switch
        {
            MonitorMode.Live => Teal,
            MonitorMode.Offline => Teal,
            MonitorMode.Error => Red,
            MonitorMode.DriverUnavailable => Amber,
            _ => Amber
        };
        _statusDot.ForeColor = color;
        _statusLabel.ForeColor = color;
        _statusLabel.Text = status.Mode switch
        {
            MonitorMode.Live => "LIVE",
            MonitorMode.Offline => "OFFLINE",
            MonitorMode.DriverUnavailable => "DRIVER MISSING",
            MonitorMode.Error => "ERROR",
            _ => "SCANNING"
        };
        _hardwareLabel.Text = status.Channel?.DisplayName ?? status.Message;
        _startButton.Enabled = !_monitor.IsRunning;
        _stopButton.Enabled = _monitor.IsRunning;
    }

    private async Task StartCapture()
    {
        _startButton.Enabled = false;
        _statusLabel.Text = "SCANNING";
        await _monitor.StartAsync();
    }

    private async Task StopCapture()
    {
        await _monitor.StopAsync();
        _statusLabel.Text = "PAUSED";
        _statusLabel.ForeColor = TextMuted;
        _statusDot.ForeColor = TextMuted;
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
    }

    private void RefreshGridIfNeeded()
    {
        if (_renderedVersion == _version && _grid.RowCount == _visibleFrames.Count)
        {
            _captureLabel.Text = $"{_traffic.Count:N0} / 5000 retained";
            return;
        }
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        if (IsDisposed)
            return;
        var query = _searchBox?.Text.Trim() ?? string.Empty;
        var type = _frameTypeFilter?.SelectedItem?.ToString() ?? "All frame types";
        var snapshot = _traffic.Snapshot();
        var matches = snapshot.AsEnumerable().Reverse().Where(frame =>
            (type == "All frame types" || frame.FrameType.Equals(type, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || FrameMatches(frame, query)));

        _visibleFrames.Clear();
        _visibleFrames.AddRange(matches);
        _grid.RowCount = _visibleFrames.Count;
        _grid.Invalidate();
        _captureLabel.Text = $"{_traffic.Count:N0} / 5000 retained";
        _renderedVersion = _version;
    }

    private static bool FrameMatches(CanFrame frame, string query)
    {
        var normalized = query.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty);
        return frame.IdText.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               (frame.IdentifierName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               frame.DataText.Replace(" ", string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               frame.Channel.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void GridOnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visibleFrames.Count)
            return;
        var frame = _visibleFrames[e.RowIndex];
        e.Value = _grid.Columns[e.ColumnIndex].Name switch
        {
            "time" => frame.TimeText,
            "id" => $"0x{frame.IdText}",
            "name" => frame.IdentifierName ?? "—",
            "message" => CanFrameDecoder.GetTableMessage(frame, _registry.GetMessage(frame.Identifier, frame.IsExtended)) ?? string.Empty,
            "data" => frame.DataText,
            _ => string.Empty
        };
    }

    private void GridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visibleFrames.Count)
            return;
        var frame = _visibleFrames[e.RowIndex];
        e.CellStyle!.Font = e.RowIndex == _primarySelectedRowIndex
            ? _primarySelectedRowFont
            : _grid.DefaultCellStyle.Font;
        if (_grid.Columns[e.ColumnIndex].Name == "id")
            e.CellStyle!.ForeColor = frame.IsExtended ? Amber : Teal;
        else if (_grid.Columns[e.ColumnIndex].Name == "name" && frame.IdentifierName is not null)
            e.CellStyle!.ForeColor = Teal;
        else if (_grid.Columns[e.ColumnIndex].Name == "data")
            e.CellStyle!.ForeColor = Color.FromArgb(186, 202, 221);
    }

    private void GridOnSelectionChanged(object? sender, EventArgs e)
    {
        if (_grid.CurrentCell is null || _grid.CurrentCell.RowIndex < 0 || _grid.CurrentCell.RowIndex >= _visibleFrames.Count)
        {
            _primarySelectedRowIndex = -1;
            _detailEmpty.Visible = false;
            _detailContent.Visible = false;
            _detailHeading.Visible = false;
            _rtdStatusLabel.Visible = false;
            _detailBusInfo.Visible = false;
            _selectedHint.Text = "No frame selected";
            return;
        }

        var selectedRowIndex = _grid.CurrentCell.RowIndex;
        var previousSelectedRowIndex = _primarySelectedRowIndex;
        _primarySelectedRowIndex = selectedRowIndex;
        if (previousSelectedRowIndex >= 0 && previousSelectedRowIndex < _grid.RowCount)
            _grid.InvalidateRow(previousSelectedRowIndex);
        _grid.InvalidateRow(selectedRowIndex);

        var frame = _visibleFrames[selectedRowIndex];
        if (_syncingIdentifierSelection)
            return;

        _syncingIdentifierSelection = true;
        try
        {
            _grid.SuspendLayout();
            for (var rowIndex = 0; rowIndex < _visibleFrames.Count; rowIndex++)
            {
                var candidate = _visibleFrames[rowIndex];
                var shouldBeSelected = candidate.Identifier == frame.Identifier && candidate.IsExtended == frame.IsExtended;
                if (_grid.Rows[rowIndex].Selected != shouldBeSelected)
                    _grid.Rows[rowIndex].Selected = shouldBeSelected;
            }
        }
        finally
        {
            _grid.ResumeLayout(true);
            _syncingIdentifierSelection = false;
        }

        var decoded = CanFrameDecoder.Decode(frame, _registry.GetMessage(frame.Identifier, frame.IsExtended));
        _detailEmpty.Visible = false;
        _detailContent.Visible = true;
        _detailHeading.Visible = true;
        _rtdStatusLabel.Visible = true;
        _detailBusInfo.Visible = true;
        _selectedHint.Text = $"Selected frame {frame.TimeText}";
        _detailId.Text = $"0x{frame.IdText}";
        _detailType.Text = frame.IdentifierName is null ? frame.FrameType.ToUpperInvariant() : $"{frame.FrameType.ToUpperInvariant()}  ·  {frame.IdentifierName}";
        _detailTime.Text = $"{frame.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff} local time";
        _detailSender.Text = decoded.Sender;
        _detailLength.Text = decoded.Length;
        _detailMessage.Text = decoded.Message;
        RenderDecodedFields(decoded.Fields);
        _detailBusInfo.Text = $"{frame.Channel}   ·   {FormatRate(frame.Bitrate)}";
    }

    private void ClearTraffic()
    {
        _traffic.Clear();
        Interlocked.Increment(ref _version);
        _detailEmpty.Visible = false;
        _detailContent.Visible = false;
        _detailHeading.Visible = false;
        _rtdStatusLabel.Visible = false;
        _detailBusInfo.Visible = false;
        RefreshGrid();
    }

    private void ResetFilters()
    {
        _searchBox.Clear();
        _frameTypeFilter.SelectedIndex = 0;
    }

    private void ExportCapture()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export retained CAN traffic",
            Filter = "PCAN capture (*.pcanlog)|*.pcanlog|Text file (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "pcanlog",
            FileName = $"pcan-capture-{DateTime.Now:yyyyMMdd-HHmmss}.pcanlog",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            AppLog.Info($"Exporting {_traffic.Count} retained frames to {dialog.FileName}.");
            CaptureFileService.Export(dialog.FileName, _traffic.Snapshot());
            _hardwareLabel.Text = $"Exported {_traffic.Count:N0} frames";
        }
        catch (Exception exception)
        {
            AppLog.Error($"Capture export failed for {dialog.FileName}.", exception);
            MessageBox.Show(this, exception.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportCapture()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import CAN capture",
            Filter = "PCAN capture (*.pcanlog;*.txt)|*.pcanlog;*.txt|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            AppLog.Info($"Importing capture from {dialog.FileName}.");
            var frames = CaptureFileService.Import(dialog.FileName)
                .Select(frame => frame with { IdentifierName = _registry.GetName(frame.Identifier, frame.IsExtended) })
                .ToArray();
            _traffic.Clear();
            _traffic.AddRange(frames);
            UpdateRtdStatusFromFrames(frames);
            Interlocked.Increment(ref _version);
            _statusLabel.Text = "OFFLINE";
            _statusLabel.ForeColor = Teal;
            _statusDot.ForeColor = Teal;
            _hardwareLabel.Text = $"Offline capture · {Path.GetFileName(dialog.FileName)}";
            RefreshGrid();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Capture import failed for {dialog.FileName}.", exception);
            MessageBox.Show(this, exception.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenIdentifierEditor()
    {
        using var dialog = new IdentifierEditorForm(_registry, _traffic.Snapshot());
        if (dialog.ShowDialog(this) == DialogResult.OK)
            RemapTrafficNames();
    }

    private void OpenMessageEditor()
    {
        using var dialog = new MessageEditorForm(_registry);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Interlocked.Increment(ref _version);
            RefreshGrid();
        }
    }

    private void RemapTrafficNames()
    {
        var remapped = _traffic.Snapshot().Select(frame => frame with { IdentifierName = _registry.GetName(frame.Identifier, frame.IsExtended) }).ToArray();
        _traffic.Clear();
        _traffic.AddRange(remapped);
        Interlocked.Increment(ref _version);
        RefreshGrid();
    }

    private void OpenIdentifierFile()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _registry.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The identifier file is stored at:\n{_registry.FilePath}\n\n{exception.Message}", "Identifier file", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void MainFormOnClosing(object? sender, FormClosingEventArgs e)
    {
        AppLog.Info("Main window closing.");
        _closing = true;
        _refreshTimer.Stop();
        _monitor.StopAsync().GetAwaiter().GetResult();
        _monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static Label MakeLabel(string text, Color color, float size, ContentAlignment alignment)
        => new() { Text = text, ForeColor = color, Font = new Font("Segoe UI", size), TextAlign = alignment, AutoSize = false, AutoEllipsis = true };

    private static Button MakeButton(string text, Color foreground, Color background, int width)
        => new()
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = Border, MouseOverBackColor = Color.FromArgb(42, 54, 72) },
            BackColor = background,
            ForeColor = foreground,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Margin = new Padding(0, 0, 7, 0),
            Cursor = Cursors.Hand
        };

    private static string FormatRate(int rate) => rate >= 1_000_000 ? $"{rate / 1_000_000d:0.#} Mbit/s" : rate <= 0 ? "unknown" : $"{rate / 1_000d:0.#} kbit/s";

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => SurfaceRaised;
        public override Color MenuItemSelectedGradientBegin => SurfaceRaised;
        public override Color MenuItemSelectedGradientEnd => SurfaceRaised;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
    }
}

public sealed class IdentifierEditorForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 20, 29);
    private static readonly Color Surface = Color.FromArgb(23, 31, 43);
    private static readonly Color Raised = Color.FromArgb(29, 39, 53);
    private static readonly Color Border = Color.FromArgb(48, 61, 80);
    private static readonly Color TextPrimary = Color.FromArgb(235, 241, 248);
    private static readonly Color TextMuted = Color.FromArgb(143, 158, 177);
    private static readonly Color Teal = Color.FromArgb(46, 205, 177);
    private readonly IdentifierRegistry _registry;
    private readonly DataGridView _grid;

    public IdentifierEditorForm(IdentifierRegistry registry, IReadOnlyList<CanFrame> observedFrames)
    {
        _registry = registry;
        Text = $"Name observed identifiers — {registry.ActiveListName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(650, 430);
        ClientSize = new Size(760, 540);
        BackColor = Canvas;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(18), BackColor = Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        var title = new Label { Text = $"Identifier names · {registry.ActiveListName}", Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold), ForeColor = TextPrimary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(title, 0, 0);
        var info = new Label { Text = "Names are saved in the shared identifier file under this list. Edit the Name column, then save.", ForeColor = TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        root.Controls.Add(info, 0, 1);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Border,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            EnableHeadersVisualStyles = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 31 }
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Raised, ForeColor = TextMuted, Font = new Font("Segoe UI Semibold", 8F), Padding = new Padding(8, 0, 4, 0) };
        _grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextPrimary, SelectionBackColor = Color.FromArgb(37, 78, 81), SelectionForeColor = TextPrimary, Padding = new Padding(8, 0, 4, 0) };
        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(25, 34, 47) };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = "Type", Width = 110, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "Identifier", Width = 150, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Name (editable)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        var definitions = registry.GetAll().ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var frame in observedFrames)
        {
            var key = IdentifierRegistry.MakeKey(frame.Identifier, frame.IsExtended);
            if (!definitions.ContainsKey(key))
                definitions[key] = new IdentifierDefinition(frame.Identifier, frame.IsExtended, registry.GetName(frame.Identifier, frame.IsExtended) ?? string.Empty);
        }
        foreach (var definition in definitions.Values.OrderBy(item => item.IsExtended).ThenBy(item => item.Identifier))
            _grid.Rows.Add(definition.IsExtended ? "Extended" : "Standard", definition.IdText, definition.Name);
        root.Controls.Add(_grid, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Canvas, Padding = new Padding(0, 10, 0, 0) };
        var save = CreateButton("Save names", Teal, Raised, 115);
        save.Click += SaveNames;
        var cancel = CreateButton("Cancel", TextMuted, Raised, 90);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);
    }

    private void SaveNames(object? sender, EventArgs e)
    {
        var definitions = new List<IdentifierDefinition>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var type = row.Cells[0].Value?.ToString();
            var idText = row.Cells[1].Value?.ToString() ?? string.Empty;
            var name = row.Cells[2].Value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(idText) || string.IsNullOrWhiteSpace(name))
                continue;
            var hex = idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? idText[2..] : idText;
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                definitions.Add(new IdentifierDefinition(id, type.Equals("Extended", StringComparison.OrdinalIgnoreCase), name));
        }
        try
        {
            _registry.Replace(definitions);
            _registry.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save names", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Button CreateButton(string text, Color foreground, Color background, int width)
        => new() { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = Border }, BackColor = background, ForeColor = foreground, Font = new Font("Segoe UI Semibold", 8.5F), Margin = new Padding(8, 0, 0, 0) };
}

public sealed class MessageEditorForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 20, 29);
    private static readonly Color Surface = Color.FromArgb(23, 31, 43);
    private static readonly Color Raised = Color.FromArgb(29, 39, 53);
    private static readonly Color Border = Color.FromArgb(48, 61, 80);
    private static readonly Color TextPrimary = Color.FromArgb(235, 241, 248);
    private static readonly Color TextMuted = Color.FromArgb(143, 158, 177);
    private static readonly Color Teal = Color.FromArgb(46, 205, 177);
    private readonly IdentifierRegistry _registry;
    private readonly List<MessageDraft> _drafts;
    private readonly DataGridView _messageGrid;
    private readonly DataGridView _signalGrid;
    private int _selectedMessageIndex = -1;
    private bool _loading;

    public MessageEditorForm(IdentifierRegistry registry)
    {
        _registry = registry;
        _drafts = registry.GetMessages().Select(message => new MessageDraft(message)).ToList();
        Text = $"Edit message declarations — {registry.ActiveListName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1080, 720);
        BackColor = Canvas;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(18),
            BackColor = Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        var title = new Label
        {
            Text = $"Message declarations · {registry.ActiveListName}",
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var info = new Label
        {
            Text = "Define each CAN message and its byte signals. Values are saved in the active identifier list.",
            ForeColor = TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.Controls.Add(info, 0, 1);

        _messageGrid = CreateGrid();
        var messageType = new DataGridViewComboBoxColumn { Name = "type", HeaderText = "Frame", Width = 115, SortMode = DataGridViewColumnSortMode.NotSortable };
        messageType.Items.AddRange("Standard", "Extended");
        _messageGrid.Columns.Add(messageType);
        _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "Identifier", Width = 150, SortMode = DataGridViewColumnSortMode.NotSortable });
        _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Message name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        foreach (var message in _drafts)
            _messageGrid.Rows.Add(message.IsExtended ? "Extended" : "Standard", FormatIdentifier(message.Identifier, message.IsExtended), message.Name);
        _messageGrid.SelectionChanged += (_, _) => SelectMessageRow();
        root.Controls.Add(_messageGrid, 0, 2);

        _signalGrid = CreateGrid();
        _signalGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _signalGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "start", HeaderText = "Start byte", Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable });
        _signalGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "length", HeaderText = "Bytes", Width = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
        var kind = new DataGridViewComboBoxColumn { Name = "kind", HeaderText = "Value", Width = 105, SortMode = DataGridViewColumnSortMode.NotSortable };
        kind.Items.AddRange("Unsigned", "Signed", "Boolean");
        _signalGrid.Columns.Add(kind);
        var byteOrder = new DataGridViewComboBoxColumn { Name = "order", HeaderText = "Byte order", Width = 115, SortMode = DataGridViewColumnSortMode.NotSortable };
        byteOrder.Items.AddRange("LittleEndian", "BigEndian");
        _signalGrid.Columns.Add(byteOrder);
        _signalGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "scale", HeaderText = "Scale", Width = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
        _signalGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "unit", HeaderText = "Unit", Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable });
        _signalGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Signal name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        root.Controls.Add(_signalGrid, 0, 3);

        var buttons = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Canvas };
        var addMessage = CreateButton("Add message", Teal, Raised, 110);
        addMessage.Click += (_, _) => AddMessage();
        var deleteMessage = CreateButton("Delete message", TextMuted, Raised, 125);
        deleteMessage.Click += (_, _) => DeleteMessage();
        var addSignal = CreateButton("Add signal", Teal, Raised, 95);
        addSignal.Click += (_, _) => AddSignal();
        var deleteSignal = CreateButton("Delete signal", TextMuted, Raised, 110);
        deleteSignal.Click += (_, _) => DeleteSignal();
        leftButtons.Controls.Add(addMessage);
        leftButtons.Controls.Add(deleteMessage);
        leftButtons.Controls.Add(addSignal);
        leftButtons.Controls.Add(deleteSignal);
        buttons.Controls.Add(leftButtons);

        var rightButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Canvas };
        var save = CreateButton("Save declarations", Teal, Raised, 135);
        save.Click += (_, _) => SaveDeclarations();
        var cancel = CreateButton("Cancel", TextMuted, Raised, 90);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        rightButtons.Controls.Add(save);
        rightButtons.Controls.Add(cancel);
        buttons.Controls.Add(rightButtons);
        root.Controls.Add(buttons, 0, 4);

        AcceptButton = save;
        CancelButton = cancel;
        ReloadMessageRows(_drafts.Count == 0 ? -1 : 0);
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Border,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            EnableHeadersVisualStyles = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 31 }
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Raised, ForeColor = TextMuted, Font = new Font("Segoe UI Semibold", 8F), Padding = new Padding(8, 0, 4, 0) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextPrimary, SelectionBackColor = Color.FromArgb(37, 78, 81), SelectionForeColor = TextPrimary, Padding = new Padding(8, 0, 4, 0) };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(25, 34, 47) };
        return grid;
    }

    private void SelectMessageRow()
    {
        if (_loading)
            return;
        var newIndex = _messageGrid.CurrentRow?.Index ?? -1;
        if (newIndex == _selectedMessageIndex)
            return;

        if (_selectedMessageIndex >= 0 && !TryCommitDraft(_selectedMessageIndex, out var error))
        {
            MessageBox.Show(this, error, "Message declaration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _loading = true;
            if (_selectedMessageIndex < _messageGrid.Rows.Count)
                _messageGrid.Rows[_selectedMessageIndex].Selected = true;
            _loading = false;
            return;
        }

        _selectedMessageIndex = newIndex;
        LoadSignals(_selectedMessageIndex >= 0 && _selectedMessageIndex < _drafts.Count ? _drafts[_selectedMessageIndex] : null);
    }

    private void LoadSignals(MessageDraft? message)
    {
        _loading = true;
        _signalGrid.Rows.Clear();
        if (message is not null)
        {
            foreach (var signal in message.Signals)
            {
                _signalGrid.Rows.Add(
                    signal.StartByte,
                    signal.Length,
                    signal.ValueKind.ToString(),
                    signal.ByteOrder.ToString(),
                    signal.Scale.ToString("R", CultureInfo.InvariantCulture),
                    signal.Unit,
                    signal.Name);
            }
        }
        _loading = false;
    }

    private void AddMessage()
    {
        if (!CommitSelectedDraft())
            return;
        _drafts.Add(new MessageDraft { Name = "New message" });
        ReloadMessageRows(_drafts.Count - 1);
    }

    private void DeleteMessage()
    {
        if (_selectedMessageIndex < 0 || _selectedMessageIndex >= _drafts.Count)
            return;
        _drafts.RemoveAt(_selectedMessageIndex);
        ReloadMessageRows(Math.Min(_selectedMessageIndex, _drafts.Count - 1));
    }

    private void AddSignal()
    {
        if (!CommitSelectedDraft())
            return;
        _signalGrid.Rows.Add(0, 1, "Unsigned", "LittleEndian", "1", string.Empty, "New signal");
    }

    private void DeleteSignal()
    {
        if (_signalGrid.CurrentRow is not null && _signalGrid.CurrentRow.Index >= 0)
            _signalGrid.Rows.RemoveAt(_signalGrid.CurrentRow.Index);
    }

    private void ReloadMessageRows(int selectedIndex)
    {
        _loading = true;
        _messageGrid.Rows.Clear();
        foreach (var message in _drafts)
            _messageGrid.Rows.Add(message.IsExtended ? "Extended" : "Standard", FormatIdentifier(message.Identifier, message.IsExtended), message.Name);
        if (selectedIndex >= 0 && selectedIndex < _messageGrid.Rows.Count)
            _messageGrid.Rows[selectedIndex].Selected = true;
        _loading = false;
        _selectedMessageIndex = -1;
        if (selectedIndex >= 0)
        {
            _messageGrid.CurrentCell = _messageGrid.Rows[selectedIndex].Cells[0];
            SelectMessageRow();
        }
        else
            LoadSignals(null);
    }

    private bool CommitSelectedDraft()
    {
        if (_selectedMessageIndex < 0)
            return true;
        if (TryCommitDraft(_selectedMessageIndex, out var error))
            return true;
        MessageBox.Show(this, error, "Message declaration", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool TryCommitDraft(int index, out string error)
    {
        error = string.Empty;
        if (index < 0 || index >= _drafts.Count)
            return true;
        var draft = _drafts[index];
        if (!TryReadMessage(_messageGrid.Rows[index], draft, out error))
            return false;
        return TryReadSignals(draft, out error);
    }

    private bool TryReadMessage(DataGridViewRow row, MessageDraft draft, out string error)
    {
        error = string.Empty;
        var type = row.Cells[0].Value?.ToString() ?? string.Empty;
        var idText = row.Cells[1].Value?.ToString()?.Trim() ?? string.Empty;
        var name = row.Cells[2].Value?.ToString()?.Trim() ?? string.Empty;
        var cleanId = idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? idText[2..] : idText;
        if (!uint.TryParse(cleanId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var identifier))
        {
            error = $"'{idText}' is not a valid hexadecimal CAN identifier.";
            return false;
        }
        if (!type.Equals("Standard", StringComparison.OrdinalIgnoreCase) && !type.Equals("Extended", StringComparison.OrdinalIgnoreCase))
        {
            error = "Select Standard or Extended for every message.";
            return false;
        }
        if (name.Length == 0)
        {
            error = "Every message must have a name.";
            return false;
        }
        draft.Identifier = identifier;
        draft.IsExtended = type.Equals("Extended", StringComparison.OrdinalIgnoreCase);
        draft.Name = name;
        return true;
    }

    private bool TryReadSignals(MessageDraft draft, out string error)
    {
        error = string.Empty;
        var signals = new List<CanSignalDefinition>();
        foreach (DataGridViewRow row in _signalGrid.Rows)
        {
            var name = row.Cells[6].Value?.ToString()?.Trim() ?? string.Empty;
            if (name.Length == 0)
                continue;
            if (!int.TryParse(row.Cells[0].Value?.ToString(), out var startByte) ||
                !int.TryParse(row.Cells[1].Value?.ToString(), out var length) ||
                startByte < 0 || length is < 1 or > 8 || startByte + length > 8)
            {
                error = $"Signal '{name}' must use a valid byte range within bytes 0 through 7.";
                return false;
            }
            if (!Enum.TryParse<CanSignalValueKind>(row.Cells[2].Value?.ToString(), true, out var valueKind) ||
                !Enum.TryParse<CanSignalByteOrder>(row.Cells[3].Value?.ToString(), true, out var byteOrder) ||
                !double.TryParse(row.Cells[4].Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                error = $"Signal '{name}' has an invalid value type, byte order, or scale.";
                return false;
            }
            var unit = row.Cells[5].Value?.ToString()?.Trim() ?? string.Empty;
            signals.Add(new CanSignalDefinition(startByte, length, name, valueKind, byteOrder, scale, unit));
        }
        draft.Signals = signals;
        return true;
    }

    private void SaveDeclarations()
    {
        if (!CommitSelectedDraft())
            return;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _drafts.Count; index++)
        {
            if (!TryReadMessage(_messageGrid.Rows[index], _drafts[index], out var error))
            {
                MessageBox.Show(this, $"Message {index + 1}: {error}", "Could not save declarations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!keys.Add(_drafts[index].Key))
            {
                MessageBox.Show(this, $"The message identifier {FormatIdentifier(_drafts[index].Identifier, _drafts[index].IsExtended)} is declared more than once.", "Could not save declarations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        try
        {
            _registry.ReplaceMessages(_drafts.Select(draft => draft.ToDefinition()));
            _registry.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save declarations", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string FormatIdentifier(uint identifier, bool isExtended)
        => isExtended ? $"0x{identifier:X8}" : $"0x{identifier:X3}";

    private static Button CreateButton(string text, Color foreground, Color background, int width)
        => new() { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = Border }, BackColor = background, ForeColor = foreground, Font = new Font("Segoe UI Semibold", 8.5F), Margin = new Padding(8, 0, 0, 0) };

    private sealed class MessageDraft
    {
        public MessageDraft() { }

        public MessageDraft(CanMessageDefinition message)
        {
            Identifier = message.Identifier;
            IsExtended = message.IsExtended;
            Name = message.Name;
            Signals = message.Signals.ToList();
        }

        public uint Identifier { get; set; }
        public bool IsExtended { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<CanSignalDefinition> Signals { get; set; } = [];
        public string Key => IdentifierRegistry.MakeKey(Identifier, IsExtended);

        public CanMessageDefinition ToDefinition()
            => new(Identifier, IsExtended, Name, Signals.ToArray());
    }
}

public sealed class IdentifierListNameForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 20, 29);
    private static readonly Color Surface = Color.FromArgb(23, 31, 43);
    private static readonly Color Raised = Color.FromArgb(29, 39, 53);
    private static readonly Color Border = Color.FromArgb(48, 61, 80);
    private static readonly Color TextPrimary = Color.FromArgb(235, 241, 248);
    private static readonly Color TextMuted = Color.FromArgb(143, 158, 177);
    private static readonly Color Teal = Color.FromArgb(46, 205, 177);
    private readonly TextBox _nameBox;

    public IdentifierListNameForm()
    {
        Text = "Create identifier list";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 170);
        BackColor = Canvas;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var label = new Label
        {
            Text = "List name",
            Dock = DockStyle.Fill,
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(label, 0, 0);

        _nameBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "e.g. drivetrain, test vehicle 2"
        };
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AcceptName();
            }
        };
        root.Controls.Add(_nameBox, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Canvas,
            Padding = new Padding(0, 8, 0, 0)
        };
        var create = CreateButton("Create", Teal, Raised, 90);
        create.Click += (_, _) => AcceptName();
        var cancel = CreateButton("Cancel", TextMuted, Raised, 90);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(create);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

        AcceptButton = create;
        CancelButton = cancel;
        Shown += (_, _) => _nameBox.Focus();
    }

    public string ListName => _nameBox.Text.Trim();

    private void AcceptName()
    {
        if (ListName.Length == 0)
        {
            MessageBox.Show(this, "Enter a name for the identifier list.", "Create identifier list", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _nameBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button CreateButton(string text, Color foreground, Color background, int width)
        => new() { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = Border }, BackColor = background, ForeColor = foreground, Font = new Font("Segoe UI Semibold", 8.5F), Margin = new Padding(8, 0, 0, 0) };
}
