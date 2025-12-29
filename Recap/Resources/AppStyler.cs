using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Recap
{
    public static class AppStyler
    {
        private static readonly Color BackColor = Color.FromArgb(243, 243, 243);
        private static readonly Color ForeColor = Color.Black;
        private static readonly Color ControlBackColor = Color.FromArgb(255, 255, 255);
        private static readonly Color TextBackColor = Color.White;
        
        public static void Apply(Control control)
        {
            control.BackColor = BackColor;
            control.ForeColor = ForeColor;

            foreach (Control child in control.Controls)
            {
                if (child is Button)
                {
                    child.BackColor = ControlBackColor;
                    child.ForeColor = ForeColor;
                }
                else if (child is TextBox || child is ComboBox || child is DateTimePicker)
                {
                    child.BackColor = TextBackColor;
                    child.ForeColor = ForeColor;
                }
                else if (child is DarkListBox || child is CheckBox || child is Label || child is TrackBar)
                {
                    child.BackColor = BackColor;
                    child.ForeColor = ForeColor;
                }
                else if (child is PictureBox)
                {
                    child.BackColor = ControlBackColor;
                }
                else
                {
                    Apply(child);
                }
            }
        }
    }
}
