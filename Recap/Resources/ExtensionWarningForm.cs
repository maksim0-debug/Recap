using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Recap
{
    public class ExtensionWarningForm : Form
    {
        private CheckBox _chkDontShowAgain;
        private Button _btnOk;
        private Button _btnOpenFolder;
        private RichTextBox _rtbInstructions; 

        public bool DontShowAgain => _chkDontShowAgain.Checked;

        public ExtensionWarningForm()
        {
            InitializeComponent();
            AppStyler.Apply(this);

            _rtbInstructions.BackColor = this.BackColor;
            _rtbInstructions.ForeColor = this.ForeColor;
        }

        private void InitializeComponent()
        {
            this.Text = Localization.Get("extWarnTitle");
            this.Size = new Size(500, 420); 
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            string extPath = GetExtensionPath();
            string instructionText = Localization.Get("extWarnText");

            instructionText += $"\n\nПуть к папке:\n{extPath}";

            _rtbInstructions = new RichTextBox
            {
                Text = instructionText,
                Location = new Point(20, 20),
                Size = new Size(440, 250), 
                Font = new Font("Segoe UI", 10),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = SystemColors.Control 
            };

            _btnOpenFolder = new Button
            {
                Text = Localization.Get("extWarnOpenFolder"),
                Location = new Point(20, 280),
                Size = new Size(200, 30),
                AutoSize = true
            };
            _btnOpenFolder.Click += (s, e) => OpenExtensionFolder();

            _chkDontShowAgain = new CheckBox
            {
                Text = Localization.Get("extWarnDontShow"),
                Location = new Point(20, 330),
                AutoSize = true,
                Width = 300
            };

            _btnOk = new Button
            {
                Text = Localization.Get("ok"),
                Location = new Point(380, 330),
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK
            };

            this.Controls.Add(_rtbInstructions);
            this.Controls.Add(_btnOpenFolder);
            this.Controls.Add(_chkDontShowAgain);
            this.Controls.Add(_btnOk);

            this.AcceptButton = _btnOk;
        }

        private string GetExtensionPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string extPath = Path.Combine(baseDir, "browser-extension");

            if (!Directory.Exists(extPath))
            {
                string debugPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\browser-extension"));
                if (Directory.Exists(debugPath)) return debugPath;
            }
            return extPath;
        }

        private void OpenExtensionFolder()
        {
            try
            {
                string path = GetExtensionPath();
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", path);
                }
                else
                {
                    MessageBox.Show($"Folder not found: {path}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}