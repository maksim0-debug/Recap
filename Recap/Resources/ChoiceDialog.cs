using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class ChoiceDialog : Form
    {
        private Label _messageLabel;
        private Button _btnYes;
        private Button _btnNo;
        private Button _btnCancel;
        private PictureBox _iconBox;

        public ChoiceDialog(string message, string title, string yesText, string noText, string cancelText, Image icon = null)
        {
            this.Text = title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(400, 150);
            this.Padding = new Padding(20);

            if (icon != null)
            {
                _iconBox = new PictureBox
                {
                    Image = icon,
                    Location = new Point(20, 25),
                    Size = new Size(32, 32),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                this.Controls.Add(_iconBox);
            }

            _messageLabel = new Label
            {
                Text = message,
                Location = new Point(icon != null ? 65 : 20, 20),
                Size = new Size(icon != null ? 315 : 360, 60),
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Segoe UI", 9f)
            };

            _btnYes = new Button
            {
                Text = yesText,
                DialogResult = DialogResult.Yes,
                Location = new Point(110, 100),
                Size = new Size(85, 30)
            };

            _btnNo = new Button
            {
                Text = noText,
                DialogResult = DialogResult.No,
                Location = new Point(200, 100),
                Size = new Size(85, 30)
            };

            _btnCancel = new Button
            {
                Text = cancelText,
                DialogResult = DialogResult.Cancel,
                Location = new Point(290, 100),
                Size = new Size(85, 30)
            };

            this.Controls.Add(_messageLabel);
            this.Controls.Add(_btnYes);
            this.Controls.Add(_btnNo);
            this.Controls.Add(_btnCancel);

            this.AcceptButton = _btnYes; 
            this.CancelButton = _btnCancel;

            AppStyler.Apply(this);
        }

        public static DialogResult Show(string message, string title, string yesText, string noText, string cancelText, Image icon = null)
        {
            using (var dialog = new ChoiceDialog(message, title, yesText, noText, cancelText, icon))
            {
                return dialog.ShowDialog();
            }
        }
    }
}
