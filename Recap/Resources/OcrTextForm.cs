using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class OcrTextForm : Form
    {
        private readonly TextBox _textBox;
        private readonly Label _lblInfo;
        private readonly Button _btnPrev;
        private readonly Button _btnNext;
        private readonly Button _btnCopy;
        private readonly HistoryViewController _controller;
        private int _currentOffset = 0;

        public OcrTextForm(string text, string info, HistoryViewController controller)
        {
            _controller = controller;
            this.Text = Localization.Get("ocrTextTitle");
            this.Size = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.KeyPreview = true;

            _lblInfo = new Label
            {
                Text = info,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };

            _btnPrev = new Button { Text = "<", Width = 40, Left = 10, Top = 8 };
            _btnNext = new Button { Text = ">", Width = 40, Left = 60, Top = 8 };
            _btnCopy = new Button { Text = Localization.Get("copyText"), Width = 100, Left = 110, Top = 8 };

            _btnPrev.Click += (s, e) => Navigate(-1);
            _btnNext.Click += (s, e) => Navigate(1);
            _btnCopy.Click += (s, e) => {
                if (!string.IsNullOrEmpty(_textBox.Text))
                {
                    Clipboard.SetText(_textBox.Text);
                }
            };

            bottomPanel.Controls.AddRange(new Control[] { _btnPrev, _btnNext, _btnCopy });

            _textBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Text = text,
                Font = new Font("Consolas", 10)
            };

            this.Controls.Add(_textBox);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(_lblInfo);

            this.KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.NumPad4)
            {
                Navigate(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.NumPad6)
            {
                Navigate(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }

        private void Navigate(int offset)
        {
            _controller.NavigateFrames(offset);
        }

        public void UpdateText(string text, string info)
        {
            _textBox.Text = text;
            _lblInfo.Text = info;
        }

        public void UpdateLocalization()
        {
            this.Text = Localization.Get("ocrTextTitle");
            _btnCopy.Text = Localization.Get("copyText");
        }
    }
}
