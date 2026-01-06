using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class RangePickerForm : Form
    {
        public DateTime StartDate { get; private set; }
        public DateTime EndDate { get; private set; }

        private DateTimePicker _dtpStart;
        private DateTimePicker _dtpEnd;
        private Button _btnOk;
        private Button _btnCancel;

        public RangePickerForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = Localization.Get("rangePickerTitle");
            this.Size = new Size(300, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var lblStart = new Label { Text = Localization.Get("rangePickerStart"), Location = new Point(20, 20), AutoSize = true };
            _dtpStart = new DateTimePicker { Location = new Point(20, 45), Width = 240 };
            
            var lblEnd = new Label { Text = Localization.Get("rangePickerEnd"), Location = new Point(20, 80), AutoSize = true };
            _dtpEnd = new DateTimePicker { Location = new Point(20, 105), Width = 240 };

            _btnOk = new Button { Text = Localization.Get("ok"), Location = new Point(100, 140), DialogResult = DialogResult.OK };
            _btnCancel = new Button { Text = Localization.Get("cancel"), Location = new Point(180, 140), DialogResult = DialogResult.Cancel };

            this.Controls.Add(lblStart);
            this.Controls.Add(_dtpStart);
            this.Controls.Add(lblEnd);
            this.Controls.Add(_dtpEnd);
            this.Controls.Add(_btnOk);
            this.Controls.Add(_btnCancel);

            this.AcceptButton = _btnOk;
            this.CancelButton = _btnCancel;

            _dtpStart.Value = DateTime.Today.AddDays(-7);
            _dtpEnd.Value = DateTime.Today;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (DialogResult == DialogResult.OK)
            {
                StartDate = _dtpStart.Value.Date;
                EndDate = _dtpEnd.Value.Date.AddDays(1).AddTicks(-1); // End of the day
                
                if (StartDate > EndDate)
                {
                    MessageBox.Show("Start date must be before end date.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    e.Cancel = true;
                }
            }
        }
    }
}
