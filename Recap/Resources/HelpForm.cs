using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class HelpForm : Form
    {
        public HelpForm()
        {
            this.Text = Localization.Get("helpTitle");
            this.Size = new Size(500, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;

            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Padding = new Padding(20)
            };

            this.Controls.Add(rtb);

            Font headerFont = new Font("Segoe UI", 14, FontStyle.Bold);
            Font subHeaderFont = new Font("Segoe UI", 11, FontStyle.Bold);
            Font normalFont = new Font("Segoe UI", 10, FontStyle.Regular);
            Font keyFont = new Font("Consolas", 10, FontStyle.Bold);

            Color headerColor = Color.FromArgb(0, 120, 215);
            Color textColor = Color.FromArgb(64, 64, 64);

            AppendText(rtb, Localization.Get("helpNavigation") + "\n", headerFont, headerColor);
            AppendText(rtb, "\n", normalFont, textColor);

            AddShortcut(rtb, "A / D  " + Localization.Get("or") + "  ← / →", Localization.Get("helpPrevNext"), keyFont, normalFont, textColor);
            AddShortcut(rtb, Localization.Get("mouseWheel"), Localization.Get("helpWheel"), keyFont, normalFont, textColor);
            AddShortcut(rtb, "Ctrl + " + Localization.Get("mouseWheel"), Localization.Get("helpCtrlWheel"), keyFont, normalFont, textColor);
            AddShortcut(rtb, "Shift + " + Localization.Get("mouseWheel"), Localization.Get("helpShiftWheel"), keyFont, normalFont, textColor);

            AppendText(rtb, "\n", normalFont, textColor);

            AppendText(rtb, Localization.Get("helpNotes") + "\n", headerFont, headerColor);
            AppendText(rtb, "\n", normalFont, textColor);

            AddShortcut(rtb, "B", Localization.Get("helpCreateNote"), keyFont, normalFont, textColor);
            AddShortcut(rtb, "Ctrl + B", Localization.Get("helpToggleNotes"), keyFont, normalFont, textColor);
            AddShortcut(rtb, Localization.Get("rightClick"), Localization.Get("helpDeleteNote"), keyFont, normalFont, textColor);
            
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };
        }

        private void AddShortcut(RichTextBox box, string keys, string desc, Font keyFont, Font descFont, Color color)
        {
            AppendText(box, "• ", descFont, color);
            AppendText(box, keys, keyFont, Color.Black);
            AppendText(box, ": " + desc + "\n", descFont, color);
            AppendText(box, "\n", new Font(descFont.FontFamily, 4), color);   
        }

        private void AppendText(RichTextBox box, string text, Font font, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionColor = color;
            box.SelectionFont = font;
            box.AppendText(text);
            box.SelectionColor = box.ForeColor;
        }
    }
}
