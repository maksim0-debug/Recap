using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class SuggestionForm : Form
    {
        private ListBox _listBox;
        private TextBox _targetTextBox;
        
        public event EventHandler<string> SuggestionSelected;

        public SuggestionForm(TextBox target)
        {
            _targetTextBox = target;
            
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;    
            this.BackColor = Color.White;
            this.Padding = new Padding(1);   

            _listBox = new ListBox();
            _listBox.Dock = DockStyle.Fill;
            _listBox.BorderStyle = BorderStyle.None;
            _listBox.DrawMode = DrawMode.OwnerDrawFixed;
            _listBox.ItemHeight = 22;
            _listBox.Font = new Font("Segoe UI", 10f);
            
            _listBox.DrawItem += ListBox_DrawItem;
            _listBox.MouseMove += ListBox_MouseMove;
            _listBox.Click += ListBox_Click;
            _listBox.KeyDown += ListBox_KeyDown;

            this.Controls.Add(_listBox);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNA = 8;

        public void SetSuggestions(List<(string Term, int Count)> suggestions)
        {
            _listBox.Items.Clear();
            foreach (var s in suggestions)
            {
                _listBox.Items.Add(new SuggestionItem { Term = s.Term, Count = s.Count });
            }

            if (_listBox.Items.Count > 0)
            {
                int h = Math.Min(_listBox.Items.Count * _listBox.ItemHeight, 200) + 2;
                this.Height = h;
                this.Width = _targetTextBox.Width;
                
                Point p = _targetTextBox.PointToScreen(Point.Empty);
                this.Location = new Point(p.X, p.Y - this.Height);
                
                if (!this.Visible) 
                {
                    ShowWindow(this.Handle, SW_SHOWNA);
                }
                
                if (!_targetTextBox.Focused) _targetTextBox.Focus();
            }
            else
            {
                this.Hide();
            }
        }

        public void SelectNext()
        {
            if (_listBox.Items.Count == 0) return;
            int idx = _listBox.SelectedIndex + 1;
            if (idx >= _listBox.Items.Count) idx = 0;
            _listBox.SelectedIndex = idx;
        }

        public void SelectPrev()
        {
            if (_listBox.Items.Count == 0) return;
            int idx = _listBox.SelectedIndex - 1;
            if (idx < 0) idx = _listBox.Items.Count - 1;
            _listBox.SelectedIndex = idx;
        }

        public bool HasFocus()
        {
            return this.Focused || _listBox.Focused;
        }

        public void ConfirmSelection()
        {
            if (_listBox.SelectedItem is SuggestionItem item)
            {
                SuggestionSelected?.Invoke(this, item.Term);
                this.Hide();
            }
        }

        private void ListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var item = _listBox.Items[e.Index] as SuggestionItem;

            using (var brush = new SolidBrush(isSelected ? Color.LightBlue : Color.White))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            using (var brush = new SolidBrush(Color.Black))    
            {
                string text = $"{item.Term} ({item.Count})";
                e.Graphics.DrawString(text, e.Font, brush, e.Bounds.X + 2, e.Bounds.Y + 2);
            }
        }

        private void ListBox_MouseMove(object sender, MouseEventArgs e)
        {
            int index = _listBox.IndexFromPoint(e.Location);
            if (index >= 0 && index != _listBox.SelectedIndex)
            {
                _listBox.SelectedIndex = index;
            }
        }

        private void ListBox_Click(object sender, EventArgs e)
        {
            ConfirmSelection();
        }

        private void ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ConfirmSelection();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.Hide();
                e.Handled = true;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams baseParams = base.CreateParams;
                baseParams.ExStyle |= 0x08000000;  
                baseParams.ExStyle |= 0x00000080;  
                return baseParams;
            }
        }

        private class SuggestionItem
        {
            public string Term { get; set; }
            public int Count { get; set; }
            public override string ToString() => Term;
        }
    }
}
