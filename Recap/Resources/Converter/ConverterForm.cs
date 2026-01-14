using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Recap;

namespace RecapConverter
{
    public class ConverterForm : Form
    {
        private TextBox txtInputFolder;
        private Button btnBrowse;
        private Button btnConvert;
        private ProgressBar progressBar;
        private TextBox txtLog;
        private Label lblStatus;

        private NumericUpDown numWidth;
        private NumericUpDown numHeight;
        private NumericUpDown numFps;

        private ComboBox cmbEncoder;
        private NumericUpDown numQuality;

        private ConverterEngine _engine;
        private SettingsManager _settingsManager;
        private AppSettings _settings;

        public ConverterForm()
        {
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.Load();

            Recap.Localization.SetLanguage(_settings.Language);

            InitializeComponent();
            _engine = new ConverterEngine();
            _engine.ProgressChanged += OnProgress;
            _engine.LogMessage += OnLog;

            string initialPath = _settings.ConverterLastPath;
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
            {
                initialPath = _settings.StoragePath;
            }

            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
            {
                txtInputFolder.Text = initialPath;
            }
        }

        private void InitializeComponent()
        {
            this.Text = Recap.Localization.Get("convTitle");
            this.Size = new Size(600, 560);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            var lblPath = new Label { Text = Recap.Localization.Get("convInputFolder"), Location = new Point(10, 15), AutoSize = true };
            txtInputFolder = new TextBox { Location = new Point(10, 35), Width = 480, ReadOnly = true };
            
            btnBrowse = new Button { Text = Recap.Localization.Get("browse"), Location = new Point(500, 34), Width = 75 };
            btnBrowse.Click += (s, e) => SelectFolder();

            var grpSettings = new GroupBox { Text = Recap.Localization.Get("convSettingsGroup"), Location = new Point(10, 70), Size = new Size(565, 120) };

            var lblRes = new Label { Text = Recap.Localization.Get("convResolution"), Location = new Point(15, 30), AutoSize = true };
            numWidth = new NumericUpDown { Location = new Point(100, 28), Minimum = 640, Maximum = 7680, Value = 1280, Increment = 2, Width = 60 };
            var lblX = new Label { Text = "x", Location = new Point(165, 30), AutoSize = true };
            numHeight = new NumericUpDown { Location = new Point(180, 28), Minimum = 360, Maximum = 4320, Value = 720, Increment = 2, Width = 60 };

            var lblFps = new Label { Text = Recap.Localization.Get("convFps"), Location = new Point(270, 30), AutoSize = true };
            numFps = new NumericUpDown { Location = new Point(310, 28), Minimum = 1, Maximum = 60, Value = 1, Width = 50 };

            var lblEncoder = new Label { Text = Recap.Localization.Get("convCodec"), Location = new Point(15, 70), AutoSize = true };
            cmbEncoder = new ComboBox { Location = new Point(100, 68), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEncoder.Items.Add("NVIDIA NVENC (hevc_nvenc)");
            cmbEncoder.Items.Add("CPU (libx265)");
            cmbEncoder.SelectedIndex = 0;

            var lblQuality = new Label { Text = Recap.Localization.Get("convQuality"), Location = new Point(320, 70), AutoSize = true };
            numQuality = new NumericUpDown { Location = new Point(430, 68), Minimum = 0, Maximum = 51, Value = 35, Width = 50 };
            var lblQualityHint = new Label { Text = Recap.Localization.Get("convQualityHint"), Location = new Point(485, 70), AutoSize = true, ForeColor = Color.Gray };

            grpSettings.Controls.AddRange(new Control[] {
                lblRes, numWidth, lblX, numHeight,
                lblFps, numFps,
                lblEncoder, cmbEncoder,
                lblQuality, numQuality, lblQualityHint
            });

            btnConvert = new Button { Text = Recap.Localization.Get("convStartBtn"), Location = new Point(10, 200), Size = new Size(565, 40), BackColor = Color.LightGreen };
            btnConvert.Click += (s, e) => StartConversion();

            progressBar = new ProgressBar { Location = new Point(10, 250), Size = new Size(565, 20) };
            lblStatus = new Label { Text = Recap.Localization.Get("convStatusWaiting"), Location = new Point(10, 275), AutoSize = true };

            txtLog = new TextBox { Location = new Point(10, 300), Size = new Size(565, 210), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGray };

            this.Controls.AddRange(new Control[] { lblPath, txtInputFolder, btnBrowse, grpSettings, btnConvert, progressBar, lblStatus, txtLog });
        }

        private void SelectFolder()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(txtInputFolder.Text) && Directory.Exists(txtInputFolder.Text))
                {
                    fbd.SelectedPath = txtInputFolder.Text;
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtInputFolder.Text = fbd.SelectedPath;
                    _settings.ConverterLastPath = fbd.SelectedPath;
                    _settingsManager.SaveConverterPath(fbd.SelectedPath);
                }
            }
        }

        private async void StartConversion()
        {
            string folder = txtInputFolder.Text;
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(Recap.Localization.Get("convErrFolder"), Recap.Localization.Get("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!File.Exists("ffmpeg.exe"))
            {
                MessageBox.Show(Recap.Localization.Get("convErrFfmpeg"), Recap.Localization.Get("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnConvert.Enabled = false;
            btnBrowse.Enabled = false;
            txtLog.Clear();

            int w = (int)numWidth.Value;
            int h = (int)numHeight.Value;
            if (w % 2 != 0) w++;
            if (h % 2 != 0) h++;

            int fps = (int)numFps.Value;
            bool useNvenc = cmbEncoder.SelectedIndex == 0;
            int quality = (int)numQuality.Value;

            try
            {
                var files = Directory.GetFiles(folder, "*.sch");
                if (files.Length == 0)
                {
                    OnLog(Recap.Localization.Get("convErrNoFiles"));
                    return;
                }

                OnLog(Recap.Localization.Format("convLogFound", files.Length, (useNvenc ? "NVIDIA" : "CPU"), fps));

                await Task.Run(() =>
                {
                    foreach (var schFile in files)
                    {
                        _engine.ConvertDay(schFile, w, h, fps, useNvenc, quality);
                    }
                });

                OnLog(Recap.Localization.Get("convLogDone"));
                MessageBox.Show(Recap.Localization.Get("convMsgSuccess"), Recap.Localization.Get("ok"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                OnLog(Recap.Localization.Format("convLogCritical", ex.Message));
                MessageBox.Show(ex.Message, Recap.Localization.Get("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnConvert.Enabled = true;
                btnBrowse.Enabled = true;
            }
        }

        private void OnProgress(int percent, string status)
        {
            if (this.IsDisposed) return;
            this.Invoke((MethodInvoker)delegate
            {
                progressBar.Value = Math.Min(100, Math.Max(0, percent));
                lblStatus.Text = status;
            });
        }

        private void OnLog(string message)
        {
            if (this.IsDisposed) return;
            this.Invoke((MethodInvoker)delegate
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            });
        }
    }
}