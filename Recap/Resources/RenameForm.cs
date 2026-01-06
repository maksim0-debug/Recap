using System;
using System.Windows.Forms;
using System.Drawing;

namespace Recap
{
    public class RenameForm : Form
    {
        private TextBox txtAlias;
        private Button btnOk;
        private Button btnCancel;
        public string NewName => txtAlias.Text;

        public RenameForm(string currentName)
        {
            this.Text = Localization.Get("rename");
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Size = new Size(350, 150);

            var lbl = new Label() { Text = Localization.Get("enterNewName"), Left = 10, Top = 10, AutoSize = true };
            this.Controls.Add(lbl);

            txtAlias = new TextBox() { Left = 10, Top = 35, Width = 310, Text = currentName };
            this.Controls.Add(txtAlias);

            btnOk = new Button() { Text = Localization.Get("ok"), Left = 150, Top = 70, DialogResult = DialogResult.OK };
            this.Controls.Add(btnOk);

            btnCancel = new Button() { Text = Localization.Get("cancel"), Left = 230, Top = 70, DialogResult = DialogResult.Cancel };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        public void SetAlias(string alias)
        {
            txtAlias.Text = alias;
        }
    }
}
