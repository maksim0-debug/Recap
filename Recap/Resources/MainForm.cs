using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace Recap
{
    public partial class MainForm : Form
    {
        public event Action<Image> FrameChanged;

        public PictureBox MainPictureBox { get; private set; }

        public VideoView MainVideoView { get; private set; }
        public LibVLC LibVLC { get; private set; }
        public MediaPlayer MainMediaPlayer { get; private set; }

        private Button btnStart, btnStop, btnBrowse, btnFullscreen, btnSettings, btnPrevMonth, btnNextMonth, btnHelp;
        private TextBox txtStoragePath, txtAppSearch, txtOcrSearch;
        private DateTimePicker datePicker;
        private TrackBar timeTrackBar;
        private Label lblStatus, lblInfo, lblTime, lblPath, lblAppFilter, lblMonth, lblFormatBadge, lblOcrSearch;
        private DarkListBox lstAppFilter;
        private DarkListBox lstNotes;
        private SuggestionForm _suggestionForm;
        private CheckBox chkAutoStart, chkAutoScroll, chkGlobalSearch;
        private TabControl mainTabControl;
        private ActivityHeatmap activityHeatmap;

        private CancellationTokenSource _searchCts;

        private readonly SettingsManager _settingsManager;
        private FrameRepository _frameRepository;
        private readonly ScreenshotService _screenshotService;
        private readonly IconManager _iconManager;
        private OcrDatabase _ocrDb;
        private OcrService _ocrService;

        private TrayIconManager _trayIconManager;
        private GlobalHotkeyManager _hotkeyManager;

        private CaptureController _captureController;
        private HistoryViewController _historyViewController;
        private UIStateManager _uiStateManager;
        private StatisticsViewController _statisticsViewController;

        private AppSettings _currentSettings;
        private bool _startMinimized = false;

        private TabPage tabPageView;
        private TabPage tabPageStats;
        private TabControl statsTabControl;
        private TabPage tabPageHeatmap;
        private TabPage tabPageCharts;
        private TabPage tabPageHourly;
        private ChartsView chartsView;
        private HourlyActivityHeatmap hourlyActivityHeatmap;
        private ComboBox cmbHourlyPeriod;
        private DateTimePicker dtpHourlyStart;
        private DateTimePicker dtpHourlyEnd;
        private Label lblHourlyCustom;
        private Label lblHeatmapTotal;
        private Label lblHourlyTotal;
        private HourlyStatisticsController _hourlyStatisticsController;

        private bool _suppressDateEvent = false;
        private bool _isNotesMode = false;
        private ContextMenuStrip _ctxMenuNotes;

        public MainForm(
            bool autoStart,
            SettingsManager settingsManager,
            AppSettings settings,
            ScreenshotService screenshotService,
            IconManager iconManager,
            FrameRepository frameRepository,
            OcrDatabase ocrDb,
            OcrService ocrService)
        {
            _startMinimized = autoStart;
            _settingsManager = settingsManager;
            _currentSettings = settings;
            _screenshotService = screenshotService;
            _iconManager = iconManager;
            _frameRepository = frameRepository;
            _ocrDb = ocrDb;
            _ocrService = ocrService;

            Icon = IconGenerator.GenerateAppIcon();

            InitializeComponent();
            
            _trayIconManager = new TrayIconManager(this.Icon, this.Text);
            _trayIconManager.ShowRequested += OnShowClicked;
            _trayIconManager.ExitRequested += OnExitClicked;

            _hotkeyManager = new GlobalHotkeyManager(this);
            _hotkeyManager.NavigationRequested += NavigateFrames;

            AppStyler.Apply(this);
            ApplyLanguage();

            InitializeControllers();

            _uiStateManager.SetState(false);
            lblTime.BringToFront();
            lblTime.BackColor = Color.Transparent;
        }

        private void InitializeControllers()
        {
            if (_captureController != null)
            {
                _captureController.FrameCaptured -= OnFrameCaptured;
                _captureController.DayChanged -= OnDayChanged;
                _captureController.Dispose();
            }

            _historyViewController?.Dispose();
            _statisticsViewController?.Dispose();
            _hourlyStatisticsController?.Dispose();

            _captureController = new CaptureController(_screenshotService, _frameRepository, _currentSettings, _ocrDb, _ocrService, _iconManager);
            _captureController.FrameCaptured += OnFrameCaptured;
            _captureController.DayChanged += OnDayChanged;

            _historyViewController = new HistoryViewController(
                MainPictureBox, MainVideoView,
                timeTrackBar, lstAppFilter, txtAppSearch,
                lblTime, lblInfo, chkAutoScroll, lblFormatBadge,
                _frameRepository, _iconManager,
                _currentSettings, _ocrDb, txtOcrSearch,
                LibVLC, MainMediaPlayer);

            _historyViewController.SetNotesListBox(lstNotes);

            _historyViewController.FrameChanged += (img) =>
            {
                FrameChanged?.Invoke(img);
            };

            _historyViewController.CurrentDateChanged += (date) =>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((Action)(() =>
                    {
                        _suppressDateEvent = true;
                        datePicker.Value = date;
                        _suppressDateEvent = false;
                    }));
                }
                else
                {
                    _suppressDateEvent = true;
                    datePicker.Value = date;
                    _suppressDateEvent = false;
                }
            };

            _uiStateManager = new UIStateManager(
                btnStart, btnStop, btnBrowse, txtStoragePath, btnSettings, lblStatus);

            Button btnRefresh = null;
            foreach (Control c in tabPageHeatmap.Controls)
            {
                if (c is Button b && b.Text == "Refresh")
                {
                    btnRefresh = b;
                    break;
                }
            }

            _statisticsViewController = new StatisticsViewController(
                activityHeatmap, chartsView, btnPrevMonth, btnNextMonth, btnRefresh, lblMonth, lblHeatmapTotal, _frameRepository, _currentSettings, _iconManager, _ocrDb);
            _statisticsViewController.DaySelected += OnStatisticsDaySelected;

            _hourlyStatisticsController = new HourlyStatisticsController(
                hourlyActivityHeatmap, _frameRepository, cmbHourlyPeriod, dtpHourlyStart, dtpHourlyEnd, lblHourlyCustom, lblHourlyTotal);
        }

        private void OnDayChanged()
        {
            if (this.InvokeRequired) this.Invoke((Action)(OnDayChanged));
            else if (datePicker.Value.Date == DateTime.Today.AddDays(-1)) datePicker.Value = DateTime.Today;
        }

        private void OnFrameCaptured(FrameIndex newFrame)
        {
            if (this.InvokeRequired) this.Invoke((Action)(() => HandleFrameCaptureUI(newFrame)));
            else HandleFrameCaptureUI(newFrame);
        }

        private void HandleFrameCaptureUI(FrameIndex newFrame)
        {
            _historyViewController.HandleNewFrame(newFrame);

            if (AdvancedSettings.Instance.ShowCaptureMethod && _screenshotService != null)
            {
                string method = _screenshotService.LastUsedCaptureMethod;
                if (!string.IsNullOrEmpty(method) && method != "None")
                {
                    lblStatus.Text = $"Capture: {method}";
                }
            }
        }

        public void UpdateLocalization()
        {
            this.Text = Localization.Get("windowTitle");
            btnStart.Text = Localization.Get("startBtn");
            btnStop.Text = Localization.Get("stopBtn");
            btnSettings.Text = Localization.Get("settingsBtn");
            btnBrowse.Text = Localization.Get("browse");
            chkAutoStart.Text = Localization.Get("startWithWindows");
            lblPath.Text = Localization.Get("folder");

            if (btnHelp != null)
            {
                var toolTip = new ToolTip();
                toolTip.SetToolTip(btnHelp, Localization.Get("helpTitle"));

                btnHelp.BackColor = Color.Transparent;
                btnHelp.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnHelp.FlatAppearance.MouseOverBackColor = Color.Transparent;
            }

            chkAutoScroll.Text = Localization.Get("liveFeed");
            btnFullscreen.Text = Localization.Get("fullscreen");
            lblAppFilter.Text = Localization.Get("filterApps");
            chkGlobalSearch.Text = Localization.Get("globalSearch");

            _trayIconManager?.UpdateLocalization();

            tabPageView.Text = Localization.Get("dayViewTab");
            tabPageStats.Text = Localization.Get("statsTab");
            tabPageHeatmap.Text = Localization.Get("heatmap");
            tabPageCharts.Text = Localization.Get("charts");
            tabPageHourly.Text = Localization.Get("hourlyActivity");

            _statisticsViewController?.UpdateLocalization();
            _hourlyStatisticsController?.UpdateLocalization();

            foreach (Control c in tabPageHeatmap.Controls)
            {
                if (c is Button b && (b.Text == "Refresh" || b.Text == "Обновить" || b.Text == "Оновити"))
                {
                    b.Text = Localization.Get("refreshBtn");
                }
            }

            lblHourlyCustom.Text = Localization.Get("periodLabel");
            chkGlobalSearch.Text = Localization.Get("globalSearch");
            lblOcrSearch.Text = Localization.Get("ocrSearch");

            if (datePicker.ContextMenuStrip != null && datePicker.ContextMenuStrip.Items.Count > 0)
            {
                datePicker.ContextMenuStrip.Items[0].Text = Localization.Get("selectRange");
            }

            lblAppFilter.Text = Localization.Get("filterApps");

            if (_ctxMenuNotes != null && _ctxMenuNotes.Items.Count > 0)
            {
                _ctxMenuNotes.Items[0].Text = Localization.Get("deleteNote");
            }

            chartsView?.UpdateLocalization();

            if (_historyViewController != null)
            {
                _historyViewController.UpdateLocalization();
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Recap";
            this.Size = new Size(1000, 700);
            this.MinimumSize = new Size(900, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Load += MainForm_Load;
            this.KeyPreview = true;

            LibVLC = new LibVLC();
            MainMediaPlayer = new MediaPlayer(LibVLC);

            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 135 };
            topPanel.Layout += TopPanel_Layout;

            btnStart = new Button { Location = new Point(12, 12), Size = new Size(100, 30) };
            btnStart.Click += BtnStart_Click;
            btnStop = new Button { Location = new Point(120, 12), Size = new Size(100, 30) };
            btnStop.Click += BtnStop_Click;
            lblStatus = new Label { Location = new Point(230, 18), AutoSize = true };

            btnSettings = new Button { Size = new Size(130, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnSettings.Click += BtnSettings_Click;

            btnHelp = new Button
            {
                Size = new Size(20, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                Image = GenerateInfoIcon(20)
            };
            btnHelp.FlatAppearance.BorderSize = 0;
            btnHelp.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btnHelp.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btnHelp.Click += (s, e) => new HelpForm().ShowDialog(this);

            chkAutoStart = new CheckBox { Location = new Point(12, 57), AutoSize = true };
            chkAutoStart.CheckedChanged += ChkAutoStart_CheckedChanged;
            lblPath = new Label { Location = new Point(12, 94), AutoSize = true };
            txtStoragePath = new TextBox { Location = new Point(80, 91), Size = new Size(600, 20), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnBrowse = new Button { Size = new Size(110, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnBrowse.Click += BtnBrowse_Click;
            topPanel.Controls.AddRange(new Control[] { btnStart, btnStop, lblStatus, btnSettings, btnHelp, chkAutoStart, lblPath, txtStoragePath, btnBrowse });

            mainTabControl = new TabControl { Dock = DockStyle.Fill };
            mainTabControl.SelectedIndexChanged += MainTabControl_SelectedIndexChanged;

            tabPageView = new TabPage("Просмотр дня");
            tabPageView.Resize += (s, e) => UpdateLayout();
            tabPageStats = new TabPage("Статистика");

            statsTabControl = new TabControl { Dock = DockStyle.Fill };

            tabPageHeatmap = new TabPage("Тепловая карта");
            tabPageCharts = new TabPage("Диаграммы");

            btnPrevMonth = new Button { Location = new Point(12, 8), Size = new Size(100, 28), Text = "<" };
            lblMonth = new Label { Location = new Point(120, 14), Size = new Size(200, 20), Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnNextMonth = new Button { Location = new Point(328, 8), Size = new Size(100, 28), Text = ">" };

            var btnRefresh = new Button { Location = new Point(440, 8), Size = new Size(100, 28), Text = "Refresh" };
            lblHeatmapTotal = new Label { Location = new Point(550, 14), AutoSize = true, Text = "", Font = new Font(this.Font, FontStyle.Bold) };

            activityHeatmap = new ActivityHeatmap { Location = new Point(12, 40), Size = new Size(tabPageHeatmap.ClientSize.Width - 24, tabPageHeatmap.ClientSize.Height - 52), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            tabPageHeatmap.Controls.AddRange(new Control[] { btnPrevMonth, lblMonth, btnNextMonth, btnRefresh, lblHeatmapTotal, activityHeatmap });

            chartsView = new ChartsView();
            tabPageCharts.Controls.Add(chartsView);

            tabPageHourly = new TabPage("Активность по часам");

            cmbHourlyPeriod = new ComboBox { Location = new Point(12, 12), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            lblHourlyCustom = new Label { Location = new Point(170, 15), AutoSize = true, Text = "Период:", Visible = false };
            dtpHourlyStart = new DateTimePicker { Location = new Point(230, 12), Width = 100, Format = DateTimePickerFormat.Short, Visible = false };
            dtpHourlyEnd = new DateTimePicker { Location = new Point(340, 12), Width = 100, Format = DateTimePickerFormat.Short, Visible = false };
            lblHourlyTotal = new Label { Location = new Point(460, 15), AutoSize = true, Text = "", Font = new Font(this.Font, FontStyle.Bold) };

            hourlyActivityHeatmap = new HourlyActivityHeatmap
            {
                Location = new Point(12, 50),
                Size = new Size(tabPageHourly.ClientSize.Width - 24, tabPageHourly.ClientSize.Height - 62),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(243, 243, 243),
                ForeColor = Color.Black,
                BaseColor = Color.FromArgb(16, 124, 16)
            };

            tabPageHourly.Controls.AddRange(new Control[] { cmbHourlyPeriod, lblHourlyCustom, dtpHourlyStart, dtpHourlyEnd, lblHourlyTotal, hourlyActivityHeatmap });

            statsTabControl.TabPages.Add(tabPageHeatmap);
            statsTabControl.TabPages.Add(tabPageCharts);
            statsTabControl.TabPages.Add(tabPageHourly);
            tabPageStats.Controls.Add(statsTabControl);
            datePicker = new DateTimePicker { Location = new Point(12, 15), Format = DateTimePickerFormat.Short, Width = 100 };
            datePicker.ValueChanged += (s, e) =>
            {
                if (!_suppressDateEvent) _historyViewController?.LoadFramesForDate(datePicker.Value);
            };

            var dateMenu = new ContextMenuStrip();
            dateMenu.Items.Add("Select Range...", null, (s, e) => ShowRangeSelectionDialog());
            datePicker.ContextMenuStrip = dateMenu;

            chkGlobalSearch = new CheckBox { Location = new Point(tabPageView.ClientSize.Width - 162, 10), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Text = "Global Search" };
            chkGlobalSearch.CheckedChanged += ChkGlobalSearch_CheckedChanged;

            lblOcrSearch = new Label { Location = new Point(tabPageView.ClientSize.Width - 182, 35), Anchor = AnchorStyles.Top | AnchorStyles.Left, AutoSize = true, Text = "OCR Search" };
            txtOcrSearch = new TextBox { Location = new Point(tabPageView.ClientSize.Width - 182, 50), Size = new Size(170, 20), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            txtOcrSearch.TextChanged += TxtOcrSearch_TextChanged;
            txtOcrSearch.KeyDown += TxtOcrSearch_KeyDown;
            txtOcrSearch.Leave += (s, e) => {
                Task.Delay(200).ContinueWith(t => { if (this.IsHandleCreated) this.Invoke((Action)(() => { if (_suggestionForm != null && !_suggestionForm.HasFocus()) _suggestionForm.Hide(); })); });
            };

            _suggestionForm = new SuggestionForm(txtOcrSearch) { Owner = this };
            _suggestionForm.SuggestionSelected += (s, term) => {
                txtOcrSearch.Text = term;
                txtOcrSearch.SelectionStart = term.Length;
                txtOcrSearch.Focus();
            };

            lblAppFilter = new Label { Location = new Point(tabPageView.ClientSize.Width - 182, 75), Anchor = AnchorStyles.Top | AnchorStyles.Left, AutoSize = true };
            txtAppSearch = new TextBox { Location = new Point(tabPageView.ClientSize.Width - 182, 90), Size = new Size(170, 20), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            txtAppSearch.TextChanged += TxtAppSearch_TextChanged;
            lstAppFilter = new DarkListBox { Location = new Point(tabPageView.ClientSize.Width - 182, 115), Size = new Size(170, tabPageView.ClientSize.Height - 125), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            lstNotes = new DarkListBox { Location = new Point(tabPageView.ClientSize.Width - 182, 115), Size = new Size(170, tabPageView.ClientSize.Height - 125), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom, Visible = false };
            lstNotes.SelectedIndexChanged += LstNotes_SelectedIndexChanged;
            lstNotes.MouseDown += LstNotes_MouseDown;

            _ctxMenuNotes = new ContextMenuStrip();
            var deleteNoteItem = new ToolStripMenuItem(Localization.Get("deleteNote"));
            deleteNoteItem.Click += DeleteNoteItem_Click;
            _ctxMenuNotes.Items.Add(deleteNoteItem);

            timeTrackBar = new TrackBar { Location = new Point(190, 15), Size = new Size(tabPageView.ClientSize.Width - 364, 45), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TickStyle = TickStyle.None, Minimum = 0, Maximum = 100 };
            lblTime = new Label { Name = "lblTime", AutoSize = true, Location = new Point(timeTrackBar.Right - 66, timeTrackBar.Bottom + -22), TextAlign = ContentAlignment.TopRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            chkAutoScroll = new CheckBox { Location = new Point(12, 65), AutoSize = true, Checked = false };
            btnFullscreen = new Button { Location = new Point(230, 63), Size = new Size(140, 28) };
            btnFullscreen.Click += BtnFullscreen_Click;
            lblInfo = new Label { Name = "lblInfo", Location = new Point(380, 67), Size = new Size(400, 20), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Top | AnchorStyles.Left };

            lblFormatBadge = new Label { AutoSize = true, Location = new Point(tabPageView.ClientSize.Width - 250, 70), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.Gray, ForeColor = Color.White, Padding = new Padding(3), Text = "", Visible = false, Font = new Font(this.Font, FontStyle.Bold) };

            MainPictureBox = new PictureBox { Location = new Point(12, 100), Size = new Size(tabPageView.ClientSize.Width - 214, tabPageView.ClientSize.Height - 112), BorderStyle = BorderStyle.None, SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            MainPictureBox.DoubleClick += BtnFullscreen_Click;

            MainVideoView = new VideoView { Location = new Point(12, 100), Size = new Size(tabPageView.ClientSize.Width - 214, tabPageView.ClientSize.Height - 112), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, Visible = false, MediaPlayer = MainMediaPlayer };
            MainVideoView.DoubleClick += BtnFullscreen_Click;

            tabPageView.Controls.AddRange(new Control[] { datePicker, timeTrackBar, lblTime, chkGlobalSearch, lblOcrSearch, txtOcrSearch, lblAppFilter, txtAppSearch, lstAppFilter, lstNotes, chkAutoScroll, btnFullscreen, lblInfo, lblFormatBadge, MainPictureBox, MainVideoView });
            lblFormatBadge.BringToFront();

            mainTabControl.TabPages.Add(tabPageView);
            mainTabControl.TabPages.Add(tabPageStats);
            this.Controls.Add(mainTabControl);
            this.Controls.Add(topPanel);

            UpdateLocalization();
        }

        private void TopPanel_Layout(object sender, LayoutEventArgs e)
        {
            Panel panel = sender as Panel;
            if (panel != null)
            {
                btnSettings.Location = new Point(panel.ClientSize.Width - 142, 12);
                btnHelp.Location = new Point(panel.ClientSize.Width - 142 - 25, 17);
                btnBrowse.Location = new Point(panel.ClientSize.Width - 122, 89);
                txtStoragePath.Width = panel.ClientSize.Width - 212;
            }
        }

        private Bitmap GenerateInfoIcon(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                g.Clear(Color.Transparent);

                using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    using (FontFamily fontFamily = new FontFamily("Times New Roman"))
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        float emSize = size * 0.85f;
                        RectangleF rect = new RectangleF(0, 0, size, size);

                        path.AddString("i", fontFamily, (int)(FontStyle.Bold | FontStyle.Italic), emSize, rect, sf);
                    }

                    using (Pen pen = new Pen(Color.Black, 2.0f))
                    {
                        pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                        g.DrawPath(pen, path);
                    }

                    g.FillPath(Brushes.White, path);
                }
            }
            return bmp;
        }

        public void NavigateFrames(int offset) => _historyViewController.NavigateFrames(offset);

        private void OnStatisticsDaySelected(DateTime date)
        {
            mainTabControl.SelectedIndex = 0;
            datePicker.Value = date;
        }

        private async void MainTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mainTabControl.SelectedIndex == 1) 
            {
                _statisticsViewController.RefreshAliases();
                await _statisticsViewController.ActivateAsync();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                if (_captureController != null) _captureController.DayChanged -= OnDayChanged;

                _captureController?.Dispose();
                _screenshotService?.Dispose();
                _historyViewController?.Dispose();
                _statisticsViewController?.Dispose();
                _iconManager.Dispose();
                
                _trayIconManager?.Dispose();
                _hotkeyManager?.Dispose();

                _ocrService?.Stop();
                _ocrDb?.Dispose();

                MainMediaPlayer?.Dispose();
                LibVLC?.Dispose();

                base.OnFormClosing(e);
            }
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            _uiStateManager.SetControlsEnabled(false, datePicker, timeTrackBar, lstAppFilter);

            if (_startMinimized)
            {
                await Task.Delay(4000);
            }
            if (_currentSettings.MonitorDeviceName != "AllScreens")
            {
                bool monitorExists = false;
                foreach (var s in Screen.AllScreens)
                {
                    if (s.DeviceName == _currentSettings.MonitorDeviceName)
                    {
                        monitorExists = true;
                        break;
                    }
                }
                if (!monitorExists)
                {
                    DebugLogger.Log("Saved monitor not found at startup. Will use Primary.");
                }
            }

            await _historyViewController.LoadFramesForDate(datePicker.Value);
            _uiStateManager.SetControlsEnabled(true, datePicker, timeTrackBar, lstAppFilter);
            UpdateLayout();

            CheckExtensionWarning();

            if (_startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                StartCapture();
            }
        }

        private void CheckExtensionWarning()
        {
            if (!_currentSettings.SuppressExtensionWarning)
            {
                this.BeginInvoke((Action)(() =>
                {
                    using (var form = new ExtensionWarningForm())
                    {
                        form.ShowDialog(this);

                        if (form.DontShowAgain)
                        {
                            _currentSettings.SuppressExtensionWarning = true;
                            SaveSettings();
                        }
                    }
                }));
            }
        }

        private void InitializeOcr()
        {
            _ocrService?.Stop();
            _ocrDb?.Dispose();
            _ocrService = null;
            _ocrDb = null;

            if (!string.IsNullOrEmpty(_currentSettings.StoragePath))
            {
                try
                {
                    _ocrDb = new OcrDatabase(_currentSettings.StoragePath);
                    string tempOcrPath = Path.Combine(_currentSettings.StoragePath, "tempOCR");
                    _ocrService = new OcrService(tempOcrPath, _ocrDb);
                    _ocrService.EnableOCR = _currentSettings.EnableOCR;
                    _ocrService.EnableTextHighlighting = _currentSettings.EnableTextHighlighting;
                    _ocrService.Start();

                    _frameRepository?.SetOcrDatabase(_ocrDb);

                    Task.Run(async () =>
                    {
                        try
                        {
                            await _frameRepository.SyncAllDaysToDbAsync();
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogError("MainForm.SyncAllDays", ex);
                        }
                    });
                }
                catch (Exception ex) { DebugLogger.LogError("MainForm.InitOCR", ex); }
            }
        }

        private void LoadSettingsAndApply()
        {
            _currentSettings = _settingsManager.Load();
            _frameRepository = new FrameRepository(_currentSettings.StoragePath);

            if (_ocrDb != null)
            {
                _frameRepository.SetOcrDatabase(_ocrDb);
            }

            _screenshotService.Settings = _currentSettings;
            _iconManager.DisableVideoPreviews = _currentSettings.DisableVideoPreviews;

            if (_ocrService != null)
            {
                _ocrService.EnableOCR = _currentSettings.EnableOCR;
                _ocrService.EnableTextHighlighting = _currentSettings.EnableTextHighlighting;
            }

            AppStyler.Apply(this);

            ApplyLanguage();
        }

        private void SaveSettings() => _settingsManager.Save(_currentSettings);

        private void ApplyLanguage()
        {
            Localization.SetLanguage(_currentSettings.Language);
            UpdateLocalization();

            txtStoragePath.Text = _currentSettings.StoragePath;
            chkAutoStart.Checked = _currentSettings.StartWithWindows;
            chkGlobalSearch.Checked = _currentSettings.GlobalSearch;
        }

        private void StartCapture()
        {
            if (string.IsNullOrEmpty(_currentSettings.StoragePath) || !Directory.Exists(Path.GetDirectoryName(_currentSettings.StoragePath)))
            {
                try { Directory.CreateDirectory(_currentSettings.StoragePath); }
                catch { MessageBox.Show(Localization.Get("selectFolder"), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            _captureController.Start();
            _uiStateManager.SetState(true);
        }

        private void StopCapture()
        {
            _captureController.Stop();
            _uiStateManager.SetState(false);
        }

        private void BtnStart_Click(object sender, EventArgs e) => StartCapture();
        private void BtnStop_Click(object sender, EventArgs e) => StopCapture();
        private void OnShowClicked(object sender, EventArgs e) { Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void OnExitClicked(object sender, EventArgs e)
        {
            _ocrService?.Stop();
            _trayIconManager?.SetVisible(false);
            Application.Exit();
        }

        private async void BtnSettings_Click(object sender, EventArgs e)
        {
            try
            {
                List<MiniFrame> cachedFrames = null;
                List<string> cachedFilter = null;
                int cachedIndex = -1;
                bool canRestoreState = false;
                bool wasCapturing = _captureController != null && _captureController.IsCapturing;

                if (_historyViewController != null)
                {
                    _historyViewController.GetState(out cachedFrames, out cachedFilter, out cachedIndex);
                }

                string oldStoragePath = _currentSettings.StoragePath;
                bool oldGlobalSearch = _currentSettings.GlobalSearch;

                using (var settingsForm = new SettingsForm(_currentSettings.Clone(), _frameRepository, _iconManager))
                {
                    var result = settingsForm.ShowDialog(this);

                    if (result == DialogResult.OK)
                    {
                        if (wasCapturing) StopCapture();

                        if (_historyViewController != null)
                        {
                            _historyViewController.Dispose();
                            _historyViewController = null;
                        }

                        if (MainMediaPlayer != null)
                        {
                            if (MainMediaPlayer.IsPlaying) MainMediaPlayer.Stop();

                            if (MainVideoView != null)
                            {
                                MainVideoView.MediaPlayer = null;
                                MainVideoView.Visible = false;
                            }

                            MainMediaPlayer.Dispose();
                            MainMediaPlayer = null;
                        }

                        _currentSettings = settingsForm.UpdatedSettings;
                        SaveSettings();
                        LoadSettingsAndApply();
                        UpdateLocalization();

                        if (oldStoragePath == _currentSettings.StoragePath &&
                            oldGlobalSearch == _currentSettings.GlobalSearch && 
                            !settingsForm.HiddenAppsChanged)
                        {
                            canRestoreState = true;
                        }

                        MainMediaPlayer = new MediaPlayer(LibVLC);
                        if (MainVideoView != null) MainVideoView.MediaPlayer = MainMediaPlayer;

                        InitializeControllers();

                        _uiStateManager.SetControlsEnabled(false, datePicker, timeTrackBar, lstAppFilter);

                        if (canRestoreState && cachedFrames != null)
                        {
                            _historyViewController.RestoreState(cachedFrames, cachedFilter, cachedIndex);
                        }
                        else
                        {
                            await _historyViewController.LoadFramesForDate(datePicker.Value);
                        }

                        _uiStateManager.SetControlsEnabled(true, datePicker, timeTrackBar, lstAppFilter);

                        if (wasCapturing) StartCapture();
                        else _uiStateManager.SetState(false);
                    }
                    else
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in Settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(txtStoragePath.Text) && Directory.Exists(txtStoragePath.Text))
                {
                    fbd.SelectedPath = txtStoragePath.Text;
                }

                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    txtStoragePath.Text = fbd.SelectedPath;
                    _currentSettings.StoragePath = fbd.SelectedPath;

                    SaveSettings();

                    LoadSettingsAndApply();
                    InitializeOcr();
                    InitializeControllers();
                    await _historyViewController.LoadFramesForDate(datePicker.Value);
                }
            }
        }

        private void BtnFullscreen_Click(object sender, EventArgs e)
        {
            bool isVideo = MainVideoView != null && MainVideoView.Visible;
            bool hasImage = MainPictureBox != null && MainPictureBox.Image != null;

            if (hasImage || isVideo)
            {
                using (var form = new FullscreenForm(this)) form.ShowDialog();
            }
        }

        private void ChkAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            if (_captureController == null) return;
            _currentSettings.StartWithWindows = chkAutoStart.Checked;
            SaveSettings();
        }

        private async void ChkGlobalSearch_CheckedChanged(object sender, EventArgs e)
        {
            if (_historyViewController == null) return;

            _currentSettings.GlobalSearch = chkGlobalSearch.Checked;
            SaveSettings();

            _uiStateManager.SetControlsEnabled(false, datePicker, timeTrackBar, lstAppFilter);

            if (!_currentSettings.GlobalSearch)
            {
                if (datePicker.Value.Date != DateTime.Today)
                {
                    datePicker.Value = DateTime.Today;
                }
                else
                {
                    await _historyViewController.LoadFramesForDate(DateTime.Today);
                }
            }
            else
            {
                await _historyViewController.LoadFramesForDate(datePicker.Value);
            }

            _uiStateManager.SetControlsEnabled(true, datePicker, timeTrackBar, lstAppFilter);
        }

        private void UpdateLayout()
        {
            if (tabPageView == null || MainPictureBox == null || lstAppFilter == null) return;
            if (this.WindowState == FormWindowState.Minimized) return;

            int leftMargin = 12;
            int rightMargin = 12;
            int gap = 20;
            int minListWidth = 180;

            int topOffset = 100;
            int bottomMargin = 12;
            int availableHeight = tabPageView.ClientSize.Height - topOffset - bottomMargin;
            if (availableHeight <= 0) return;

            int maxPbWidth = tabPageView.ClientSize.Width - leftMargin - gap - minListWidth - rightMargin;

            int finalPbWidth = maxPbWidth;
            int finalPbHeight = availableHeight;

            if (finalPbWidth < 100) finalPbWidth = 100;
            if (finalPbHeight < 60) finalPbHeight = 60;

            if (MainPictureBox.Width != finalPbWidth || MainPictureBox.Height != finalPbHeight)
            {
                MainPictureBox.SetBounds(leftMargin, topOffset, finalPbWidth, finalPbHeight);
                MainPictureBox.SizeMode = PictureBoxSizeMode.StretchImage;
                MainPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            }

            if (MainVideoView != null && (MainVideoView.Width != finalPbWidth || MainVideoView.Height != finalPbHeight))
            {
                MainVideoView.SetBounds(leftMargin, topOffset, finalPbWidth, finalPbHeight);
                MainVideoView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            }

            int listLeft = leftMargin + finalPbWidth + gap;
            int listWidth = tabPageView.ClientSize.Width - listLeft - rightMargin;

            if (listWidth < minListWidth) listWidth = minListWidth;

            lblOcrSearch.Left = listLeft;
            txtOcrSearch.SetBounds(listLeft, txtOcrSearch.Top, listWidth, txtOcrSearch.Height);

            lblAppFilter.Left = listLeft;
            lblAppFilter.Top = 75;
            txtAppSearch.SetBounds(listLeft, 90, listWidth, txtAppSearch.Height);
            lstAppFilter.SetBounds(listLeft, 115, listWidth, tabPageView.ClientSize.Height - 115 - bottomMargin);
            lstNotes.SetBounds(listLeft, 115, listWidth, tabPageView.ClientSize.Height - 115 - bottomMargin);

            chkGlobalSearch.Left = listLeft;

            int trackBarLeft = 110;
            int trackBarRightLimit = listLeft - 15;
            int newTrackBarWidth = trackBarRightLimit - trackBarLeft;

            if (newTrackBarWidth > 50)
            {
                timeTrackBar.SetBounds(trackBarLeft, timeTrackBar.Top, newTrackBarWidth, timeTrackBar.Height);
                lblTime.Left = timeTrackBar.Right - 66;
            }

            if (lblFormatBadge != null)
            {
                lblFormatBadge.Left = MainPictureBox.Right - 36;
            }
        }



        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.B && !IsTextBoxFocused())
            {
                ShowAddNoteDialog();
                return true;
            }

            if (keyData == (Keys.Control | Keys.B))
            {
                ToggleNotesMode();
                return true;
            }

            if (IsTextBoxFocused())
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == Keys.Left || keyData == Keys.A)
            {
                NavigateFrames(-1);
                return true;
            }
            if (keyData == Keys.Right || keyData == Keys.D)
            {
                NavigateFrames(1);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void TxtAppSearch_TextChanged(object sender, EventArgs e)
        {
            if (_isNotesMode)
            {
                string searchText = txtAppSearch.Text.Trim();
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    _historyViewController.ReloadNotes();
                    return;
                }

                var notes = _ocrDb.SearchNotes(searchText);
                lstNotes.Items.Clear();
                foreach (var note in notes)
                {
                    string displayName = note.Title;
                    displayName = $"{new DateTime(note.Timestamp):dd.MM.yyyy HH:mm} {note.Title}";

                    lstNotes.Items.Add(new FilterItem
                    {
                        RawName = note.Title,
                        DisplayName = displayName,
                        DurationMs = 0,
                        FrameCount = 0,
                        Level = 0,
                        HasChildren = false,
                        IsNote = true,
                        NoteTimestamp = note.Timestamp,
                        NoteDescription = note.Description
                    });
                }
            }
        }

        private async void TxtOcrSearch_TextChanged(object sender, EventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            string text = txtOcrSearch.Text;

            try
            {
                if (text.Length >= 2)
                {
                    await Task.Delay(400, token);
                    if (token.IsCancellationRequested) return;

                    long? start = null;
                    long? end = null;

                    if (!chkGlobalSearch.Checked)
                    {
                        var date = datePicker.Value.Date;
                        start = date.Ticks;
                        end = date.AddDays(1).Ticks - 1;
                    }

                    var suggestions = await Task.Run(() => 
                    {
                        if (token.IsCancellationRequested) return new List<(string, int)>();
                        return _ocrDb.GetSearchSuggestions(text, 10, start, end);
                    }, token);

                    if (token.IsCancellationRequested) return;

                    if (suggestions.Count > 0)
                    {
                        _suggestionForm.SetSuggestions(suggestions);
                    }
                    else
                    {
                        _suggestionForm.Hide();
                    }

                    _historyViewController?.ApplyFilter();
                }
                else
                {
                    _suggestionForm.Hide();
                     if (text.Length == 0) _historyViewController?.ApplyFilter();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("TxtOcrSearch_TextChanged", ex);
            }
        }

        private void TxtOcrSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _suggestionForm.Hide();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up && _suggestionForm.Visible)
            {
                _suggestionForm.SelectPrev();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down && _suggestionForm.Visible)
            {
                _suggestionForm.SelectNext();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter && _suggestionForm.Visible)
            {
                _suggestionForm.ConfirmSelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private bool IsTextBoxFocused()
        {
            Control ctrl = this.ActiveControl;
            while (ctrl is ContainerControl container && container.ActiveControl != null)
            {
                ctrl = container.ActiveControl;
            }
            return ctrl is TextBox;
        }

        private void ShowRangeSelectionDialog()
        {
            using (var form = new Form())
            {
                form.Text = "Select Date Range";
                form.Size = new Size(300, 180);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var lblStart = new Label { Text = "Start Date:", Location = new Point(20, 20), AutoSize = true };
                var dtpStart = new DateTimePicker { Location = new Point(100, 17), Format = DateTimePickerFormat.Short, Width = 150, Value = datePicker.Value };

                var lblEnd = new Label { Text = "End Date:", Location = new Point(20, 60), AutoSize = true };
                var dtpEnd = new DateTimePicker { Location = new Point(100, 57), Format = DateTimePickerFormat.Short, Width = 150, Value = datePicker.Value };

                var btnOk = new Button { Text = "OK", Location = new Point(110, 100), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(190, 100), DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[] { lblStart, dtpStart, lblEnd, dtpEnd, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    var start = dtpStart.Value.Date;
                    var end = dtpEnd.Value.Date;

                    if (start > end)
                    {
                        var temp = start;
                        start = end;
                        end = temp;
                    }

                    _historyViewController?.LoadFramesForRange(start, end);
                }
            }
        }

        private void ShowAddNoteDialog()
        {
            long currentTimestamp = _historyViewController.GetCurrentTimestamp();
            if (currentTimestamp <= 0) return;

            using (var form = new NoteForm())
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    if (string.IsNullOrWhiteSpace(form.NoteTitle)) return;

                    if (_ocrDb.AddNote(currentTimestamp, form.NoteTitle, form.NoteDescription))
                    {
                        if (_isNotesMode)
                        {
                            _historyViewController.ReloadNotes();
                        }
                    }
                    else
                    {
                        MessageBox.Show("A note for this frame already exists.", Localization.Get("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ToggleNotesMode()
        {
            _isNotesMode = !_isNotesMode;
            lstAppFilter.Visible = !_isNotesMode;
            lstNotes.Visible = _isNotesMode;

            lblAppFilter.Text = _isNotesMode ? Localization.Get("notes") : Localization.Get("filterApps");
            txtAppSearch.Text = "";

            if (_isNotesMode)
            {
                _historyViewController.ReloadNotes();
            }
        }

        private void LstNotes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstNotes.SelectedItem is FilterItem note && note.IsNote)
            {
                _historyViewController.JumpToTimestamp(note.NoteTimestamp);
            }
        }

        private void LstNotes_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = lstNotes.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    lstNotes.SelectedIndex = index;
                    _ctxMenuNotes.Show(lstNotes, e.Location);
                }
            }
        }

        private void DeleteNoteItem_Click(object sender, EventArgs e)
        {
            if (lstNotes.SelectedItem is FilterItem note && note.IsNote)
            {
                _ocrDb.DeleteNote(note.NoteTimestamp);
                lstNotes.Items.Remove(note);
            }
        }
    }
}