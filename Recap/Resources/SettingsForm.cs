using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RecapConverter;

namespace Recap
{
    public class SettingsForm : Form
    {
        public AppSettings UpdatedSettings { get; private set; }
        public bool HiddenAppsChanged { get; private set; }

        private ComboBox cmbLanguage, cmbMonitor, cmbQuality, cmbFrequency, cmbBlindZone, cmbCaptureMode;
        private Label lblLang, lblMonitor, lblQuality, lblInterval, lblBlindZone, lblCaptureMode;
        private Label lblMotionThreshold, lblMotionThresholdValue;
        private TrackBar tbMotionThreshold;
        private Button btnOk, btnCancel, btnConverter, btnAdvancedSettings, btnHiddenApps;
        private CheckBox chkGlobalSearch;
        private CheckBox chkShowFrameCount;
        private CheckBox chkEnableOCR;
        private CheckBox chkEnableHighlighting;
        private CheckBox chkDisableVideoPreviews;
        private ToolTip _toolTip;

        private readonly int[] _intervals = { 200, 500, 1000, 2000, 3000, 5000, 10000, 30000 };
        private readonly int[] _blindZoneValues = { 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70 };

        private FrameRepository _repo;
        private IconManager _iconManager;

        public SettingsForm(AppSettings currentSettings, FrameRepository repo, IconManager iconManager)
        {
            this.Text = Localization.Get("settingsTitle");
            this.UpdatedSettings = currentSettings.Clone();
            _repo = repo;
            _iconManager = iconManager;

            InitializeComponent();
            SetInitialValues();
            TranslateUI();

            AppStyler.Apply(this);
        }

        private void InitializeComponent()
        {
            this.Size = new Size(420, 600);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int leftMargin = 20;
            int topMargin = 20;
            int labelX = leftMargin;
            int controlX = 160;
            int controlWidth = 220;
            int rowHeight = 40;

            lblLang = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            cmbLanguage = new ComboBox { Location = new Point(controlX, topMargin), Size = new Size(controlWidth, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbLanguage.Items.AddRange(new object[] { "English", "Русский", "Українська" });
            cmbLanguage.SelectedIndexChanged += OnLanguageChanged;
            topMargin += rowHeight;

            lblMonitor = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            cmbMonitor = new ComboBox { Location = new Point(controlX, topMargin), Size = new Size(controlWidth, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            
            var friendlyNames = DisplayHelper.GetMonitorFriendlyNames();
            var deviceIds = DisplayHelper.GetMonitorDeviceIds();

            foreach (var screen in Screen.AllScreens)
            {
                string cleanName = screen.DeviceName.Replace(@"\\.\", "");
                string friendlyName = friendlyNames.ContainsKey(screen.DeviceName) ? friendlyNames[screen.DeviceName] : cleanName;
                string deviceId = deviceIds.ContainsKey(screen.DeviceName) ? deviceIds[screen.DeviceName] : "";
                
                string label = $"{friendlyName} ({screen.Bounds.Width}x{screen.Bounds.Height})";
                if (screen.Primary) label += " [Primary]";
                cmbMonitor.Items.Add(new ScreenItem { DeviceName = screen.DeviceName, DeviceId = deviceId, DisplayName = label });
            }
            cmbMonitor.Items.Add(new ScreenItem { DeviceName = "AllScreens", DeviceId = "AllScreens", DisplayName = Localization.Get("monitorAll") });
            topMargin += rowHeight;

            lblQuality = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            cmbQuality = new ComboBox { Location = new Point(controlX, topMargin), Size = new Size(controlWidth, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            topMargin += rowHeight;

            lblInterval = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            cmbFrequency = new ComboBox { Location = new Point(controlX, topMargin), Size = new Size(controlWidth, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            topMargin += rowHeight;

            lblBlindZone = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            cmbBlindZone = new ComboBox { Location = new Point(controlX, topMargin), Size = new Size(controlWidth, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            topMargin += rowHeight;

            lblCaptureMode = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            cmbCaptureMode = new ComboBox { Location = new Point(controlX, topMargin), Size = new Size(controlWidth, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCaptureMode.Items.AddRange(new object[] { "Auto", "DirectX (DXGI)", "Windows Graphics Capture", "GDI+" });
            topMargin += rowHeight;

            lblMotionThreshold = new Label { Location = new Point(labelX, topMargin + 3), AutoSize = true };
            tbMotionThreshold = new TrackBar { Location = new Point(controlX, topMargin), Size = new Size(controlWidth - 40, 45), Minimum = 0, Maximum = 100, TickFrequency = 10 };
            lblMotionThresholdValue = new Label { Location = new Point(controlX + controlWidth - 35, topMargin + 3), AutoSize = true };
            tbMotionThreshold.Scroll += (s, e) => lblMotionThresholdValue.Text = $"{tbMotionThreshold.Value}%";
            topMargin += rowHeight + 10;

            chkGlobalSearch = new CheckBox { Location = new Point(labelX, topMargin), AutoSize = true, Text = "Global Search (Experimental)" };
            topMargin += 25;
            chkShowFrameCount = new CheckBox { Location = new Point(labelX, topMargin), AutoSize = true, Text = "Show Frame Count" };
            topMargin += 25;
            chkEnableOCR = new CheckBox { Location = new Point(labelX, topMargin), AutoSize = true, Text = "Enable OCR" };
            topMargin += 25;
            chkEnableHighlighting = new CheckBox { Location = new Point(labelX, topMargin), AutoSize = true, Text = "Enable Text Highlighting" };
            topMargin += 25;
            chkDisableVideoPreviews = new CheckBox { Location = new Point(labelX, topMargin), AutoSize = true, Text = "Disable Video Previews" };

            _toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 450,
                ReshowDelay = 500,
                ShowAlways = true,
                OwnerDraw = true
            };

            _toolTip.Popup += (s, e) =>
            {
                string text = _toolTip.GetToolTip(e.AssociatedControl);
                if (string.IsNullOrEmpty(text)) return;

                using (Graphics g = this.CreateGraphics())
                {
                    SizeF size = g.MeasureString(text, this.Font, 300);
                    e.ToolTipSize = new Size((int)Math.Ceiling(size.Width) + 10, (int)Math.Ceiling(size.Height) + 10);
                }
            };

            _toolTip.Draw += (s, e) =>
            {
                e.DrawBackground();
                e.DrawBorder();
                e.Graphics.DrawString(e.ToolTipText, this.Font, Brushes.Black, new RectangleF(5, 5, e.Bounds.Width - 10, e.Bounds.Height - 10));
            };

            btnAdvancedSettings = new Button { Location = new Point(labelX, this.ClientSize.Height - 80), Size = new Size(160, 23), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Text = "Advanced Settings" };
            btnAdvancedSettings.Click += (s, e) => { new AdvancedSettingsForm().ShowDialog(); };

            btnHiddenApps = new Button { Location = new Point(labelX + 170, this.ClientSize.Height - 80), Size = new Size(160, 23), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Text = Localization.Get("hiddenApps") };
            btnHiddenApps.Click += (s, e) => { 
                var form = new HiddenAppsForm(_repo, _iconManager);
                form.ShowDialog();
                if (form.Changed) HiddenAppsChanged = true;
            };

            btnConverter = new Button { Location = new Point(labelX, this.ClientSize.Height - 45), Size = new Size(120, 23), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Text = "Recap Converter" };
            btnConverter.Click += (s, e) => { new RecapConverter.ConverterForm().Show(); };

            btnOk = new Button { Location = new Point(this.ClientSize.Width - 170, this.ClientSize.Height - 45), Size = new Size(75, 23), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnOk.Click += BtnOk_Click;

            btnCancel = new Button { Location = new Point(this.ClientSize.Width - 85, this.ClientSize.Height - 45), Size = new Size(75, 23), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

            this.Controls.AddRange(new Control[] { lblLang, cmbLanguage, lblMonitor, cmbMonitor, lblQuality, cmbQuality, lblInterval, cmbFrequency, lblBlindZone, cmbBlindZone, lblCaptureMode, cmbCaptureMode, lblMotionThreshold, tbMotionThreshold, lblMotionThresholdValue, chkGlobalSearch, chkShowFrameCount, chkEnableOCR, chkEnableHighlighting, chkDisableVideoPreviews, btnAdvancedSettings, btnHiddenApps, btnOk, btnCancel, btnConverter });
        }

        private void SetInitialValues()
        {
            chkGlobalSearch.Checked = UpdatedSettings.GlobalSearch;
            chkShowFrameCount.Checked = UpdatedSettings.ShowFrameCount;
            chkEnableOCR.Checked = UpdatedSettings.EnableOCR;
            chkEnableHighlighting.Checked = UpdatedSettings.EnableTextHighlighting;
            chkDisableVideoPreviews.Checked = UpdatedSettings.DisableVideoPreviews;

            tbMotionThreshold.Value = Math.Min(100, Math.Max(0, UpdatedSettings.MotionThreshold));
            lblMotionThresholdValue.Text = $"{tbMotionThreshold.Value}%";

            if (UpdatedSettings.Language == "ru") cmbLanguage.SelectedIndex = 1;
            else if (UpdatedSettings.Language == "uk") cmbLanguage.SelectedIndex = 2;
            else cmbLanguage.SelectedIndex = 0;

            cmbMonitor.SelectedIndex = 0;
            bool found = false;

            if (!string.IsNullOrEmpty(UpdatedSettings.MonitorDeviceId))
            {
                for (int i = 0; i < cmbMonitor.Items.Count; i++)
                {
                    var item = (ScreenItem)cmbMonitor.Items[i];
                    if (item.DeviceId == UpdatedSettings.MonitorDeviceId)
                    {
                        cmbMonitor.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
            }

            if (!found && !string.IsNullOrEmpty(UpdatedSettings.MonitorDeviceName))
            {
                for (int i = 0; i < cmbMonitor.Items.Count; i++)
                {
                    var item = (ScreenItem)cmbMonitor.Items[i];
                    if (item.DeviceName == UpdatedSettings.MonitorDeviceName)
                    {
                        cmbMonitor.SelectedIndex = i;
                        break;
                    }
                }
            }

            cmbQuality.SelectedIndex = GetQualityIndexFromValue(UpdatedSettings.JpegQuality);
            cmbFrequency.SelectedIndex = Math.Max(0, Array.IndexOf(_intervals, UpdatedSettings.IntervalMs));
            cmbBlindZone.SelectedIndex = Math.Max(0, Array.IndexOf(_blindZoneValues, UpdatedSettings.BlindZone));
            cmbCaptureMode.SelectedIndex = (int)UpdatedSettings.CaptureMode;
        }

        private void TranslateUI()
        {
            this.Text = Localization.Get("settingsTitle");
            lblLang.Text = Localization.Get("lang");
            lblMonitor.Text = Localization.Get("monitor");
            lblQuality.Text = Localization.Get("quality");
            lblInterval.Text = Localization.Get("freq");
            lblBlindZone.Text = Localization.Get("blindZone");
            lblCaptureMode.Text = Localization.Get("captureMethod");
            lblMotionThreshold.Text = Localization.Get("motionThreshold");
            btnOk.Text = Localization.Get("ok");
            btnCancel.Text = Localization.Get("cancel");
            btnAdvancedSettings.Text = Localization.Get("advancedSettings");

            chkGlobalSearch.Text = Localization.Get("globalSearchExp");
            chkShowFrameCount.Text = Localization.Get("showFrameCount");
            chkEnableOCR.Text = Localization.Get("enableOCR");
            chkEnableHighlighting.Text = Localization.Get("enableHighlighting");
            chkDisableVideoPreviews.Text = Localization.Get("disableVideoPreviews");

            _toolTip.SetToolTip(lblLang, Localization.Get("tooltip_Language"));
            _toolTip.SetToolTip(cmbLanguage, Localization.Get("tooltip_Language"));

            _toolTip.SetToolTip(lblMonitor, Localization.Get("tooltip_Monitor"));
            _toolTip.SetToolTip(cmbMonitor, Localization.Get("tooltip_Monitor"));

            _toolTip.SetToolTip(lblQuality, Localization.Get("tooltip_Quality"));
            _toolTip.SetToolTip(cmbQuality, Localization.Get("tooltip_Quality"));

            _toolTip.SetToolTip(lblInterval, Localization.Get("tooltip_Frequency"));
            _toolTip.SetToolTip(cmbFrequency, Localization.Get("tooltip_Frequency"));

            _toolTip.SetToolTip(lblBlindZone, Localization.Get("tooltip_BlindZone"));
            _toolTip.SetToolTip(cmbBlindZone, Localization.Get("tooltip_BlindZone"));

            _toolTip.SetToolTip(lblCaptureMode, Localization.Get("tooltip_CaptureMethod"));
            _toolTip.SetToolTip(cmbCaptureMode, Localization.Get("tooltip_CaptureMethod"));

            _toolTip.SetToolTip(lblMotionThreshold, Localization.Get("tooltip_MotionThreshold"));
            _toolTip.SetToolTip(tbMotionThreshold, Localization.Get("tooltip_MotionThreshold"));

            _toolTip.SetToolTip(chkGlobalSearch, Localization.Get("tooltip_GlobalSearch"));
            _toolTip.SetToolTip(chkShowFrameCount, Localization.Get("tooltip_ShowFrameCount"));
            _toolTip.SetToolTip(chkEnableOCR, Localization.Get("tooltip_EnableOCR"));
            _toolTip.SetToolTip(chkEnableHighlighting, Localization.Get("tooltip_EnableHighlighting"));

            string thumbCachePath = Path.Combine(Application.LocalUserAppDataPath, "ThumbCache");
            _toolTip.SetToolTip(chkDisableVideoPreviews, Localization.Format("tooltip_DisableVideoPreviews", thumbCachePath));

            for (int i = 0; i < cmbMonitor.Items.Count; i++)
            {
                var item = (ScreenItem)cmbMonitor.Items[i];
                if (item.DeviceName == "AllScreens")
                {
                    item.DisplayName = Localization.Get("monitorAll");
                    cmbMonitor.Items[i] = item; 
                }
            }

            int qualityIdx = cmbQuality.SelectedIndex;
            cmbQuality.Items.Clear();
            cmbQuality.Items.AddRange(new object[] { Localization.Get("qualityLow"), Localization.Get("qualityMedium"), Localization.Get("qualityHigh"), Localization.Get("qualityMax") });
            cmbQuality.SelectedIndex = qualityIdx;

            int intervalIdx = cmbFrequency.SelectedIndex;
            string[] freqLabels = {
                Localization.Get("interval_5fps"),
                Localization.Get("interval_2fps"),
                Localization.Get("interval_1fps"),
                Localization.Get("interval_0_5fps"),
                Localization.Get("interval_3s"),
                Localization.Get("interval_5s"),
                Localization.Get("interval_10s"),
                Localization.Get("interval_30s")
            };
            cmbFrequency.Items.Clear();
            for (int i = 0; i < freqLabels.Length; i++)
            {
                cmbFrequency.Items.Add(i == 4 ? $"{freqLabels[i]} {Localization.Get("standard")}" : freqLabels[i]);
            }
            cmbFrequency.SelectedIndex = intervalIdx;

            int blindZoneIdx = cmbBlindZone.SelectedIndex;
            cmbBlindZone.Items.Clear();
            for (int i = 0; i < _blindZoneValues.Length; i++)
            {
                string label = $"{_blindZoneValues[i]}px";
                if (i == 0) label += $" {Localization.Get("tiny")}";
                if (i == 2) label += $" {Localization.Get("standard")}";
                if (i == _blindZoneValues.Length - 1) label += $" {Localization.Get("extreme")}";
                cmbBlindZone.Items.Add(label);
            }
            cmbBlindZone.SelectedIndex = blindZoneIdx;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            UpdatedSettings.Language = cmbLanguage.SelectedIndex == 1 ? "ru" : (cmbLanguage.SelectedIndex == 2 ? "uk" : "en");
            Localization.SetLanguage(UpdatedSettings.Language);
            TranslateUI();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            UpdatedSettings.GlobalSearch = chkGlobalSearch.Checked;
            UpdatedSettings.ShowFrameCount = chkShowFrameCount.Checked;
            UpdatedSettings.EnableOCR = chkEnableOCR.Checked;
            UpdatedSettings.EnableTextHighlighting = chkEnableHighlighting.Checked;
            UpdatedSettings.DisableVideoPreviews = chkDisableVideoPreviews.Checked;
            UpdatedSettings.MotionThreshold = tbMotionThreshold.Value;
            UpdatedSettings.Language = cmbLanguage.SelectedIndex == 1 ? "ru" : (cmbLanguage.SelectedIndex == 2 ? "uk" : "en");

            if (cmbMonitor.SelectedItem is ScreenItem selectedScreen)
            {
                UpdatedSettings.MonitorDeviceName = selectedScreen.DeviceName;
                UpdatedSettings.MonitorDeviceId = selectedScreen.DeviceId;
            }

            UpdatedSettings.JpegQuality = GetQualityValueFromIndex(cmbQuality.SelectedIndex);
            UpdatedSettings.IntervalMs = _intervals[cmbFrequency.SelectedIndex];
            UpdatedSettings.BlindZone = _blindZoneValues[cmbBlindZone.SelectedIndex];
            UpdatedSettings.CaptureMode = (CaptureMode)cmbCaptureMode.SelectedIndex;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private int GetQualityIndexFromValue(long value)
        {
            if (value <= 25) return 0;
            if (value <= 45) return 1;
            if (value <= 75) return 2;
            return 3;
        }

        private long GetQualityValueFromIndex(int index)
        {
            switch (index)
            {
                case 0: return 25L;
                case 1: return 45L;
                case 2: return 75L;
                case 3: return 95L;
                default: return 45L;
            }
        }

        private class ScreenItem
        {
            public string DeviceName { get; set; }
            public string DeviceId { get; set; }
            public string DisplayName { get; set; }
            public override string ToString() => DisplayName;
        }
    }
}