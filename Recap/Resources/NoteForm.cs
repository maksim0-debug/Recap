using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class NoteForm : Form
    {
        public string NoteTitle { get { return txtTitle.Text; } }
        public string NoteDescription { get { return txtDescription.Text; } }

        private TextBox txtTitle;
        private TextBox txtDescription;
        private Button btnOk;
        private Button btnCancel;
        private Label lblTitle;
        private Label lblDesc;

        public NoteForm()
        {
            this.Text = Localization.Get("addNoteTitle");
            this.Size = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            lblTitle = new Label { Text = Localization.Get("noteTitleLabel"), Location = new Point(12, 15), AutoSize = true };
            txtTitle = new TextBox { Location = new Point(12, 35), Width = 360, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            lblDesc = new Label { Text = Localization.Get("noteDescLabel"), Location = new Point(12, 70), AutoSize = true };
            txtDescription = new TextBox { Location = new Point(12, 90), Width = 360, Height = 120, Multiline = true, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            btnOk = new Button { Text = Localization.Get("ok"), DialogResult = DialogResult.OK, Location = new Point(216, 225), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnCancel = new Button { Text = Localization.Get("cancel"), DialogResult = DialogResult.Cancel, Location = new Point(297, 225), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

            this.Controls.AddRange(new Control[] { lblTitle, txtTitle, lblDesc, txtDescription, btnOk, btnCancel });
            
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Load += NoteForm_Load;
        }

        private void NoteForm_Load(object sender, EventArgs e)
        {
            txtTitle.Focus();
        }
    }
}
