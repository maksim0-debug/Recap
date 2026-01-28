using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Recap
{
    public class HiddenItem
    {
        public string RawName;
        public string DisplayName;
        public override string ToString() => DisplayName; 
    }

    public class HiddenAppsForm : Form
    {
        private readonly FrameRepository _repo;
        private readonly IconManager _iconManager;
        private ListBox _lstHiddenApps;
        private Button _btnUnhide;
        private Button _btnUnhideAll;
        private Button _btnClose;
        public bool Changed { get; private set; } = false;
        private Dictionary<string, string> _aliases;

        public HiddenAppsForm(FrameRepository repo, IconManager iconManager)
        {
            _repo = repo;
            _iconManager = iconManager;
            
            _aliases = _repo.GetAliases();

            InitializeComp();
            LoadApps();
            AppStyler.Apply(this);
        }

        private void InitializeComp()
        {
            this.Text = Localization.Get("hiddenApps");
            this.Size = new Size(400, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            _lstHiddenApps = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(360, 400),
                SelectionMode = SelectionMode.MultiExtended,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24
            };
            _lstHiddenApps.SelectedIndexChanged += (s, e) => UpdateButtons();
            _lstHiddenApps.DrawItem += LstApp_DrawItem;

            _btnUnhide = new Button { Location = new Point(12, 420), Size = new Size(120, 30), Text = Localization.Get("unhide") };
            _btnUnhide.Click += OnUnhideClick;
            _btnUnhide.Enabled = false;

            _btnUnhideAll = new Button { Location = new Point(135, 420), Size = new Size(130, 30), Text = Localization.Get("unhideAll") };
            _btnUnhideAll.Click += OnUnhideAllClick;

            _btnClose = new Button { Location = new Point(272, 420), Size = new Size(100, 30), Text = Localization.Get("close"), DialogResult = DialogResult.OK };
            
            this.Controls.Add(_lstHiddenApps);
            this.Controls.Add(_btnUnhide);
            this.Controls.Add(_btnUnhideAll);
            this.Controls.Add(_btnClose);
        }

        private void LstApp_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();

            var item = _lstHiddenApps.Items[e.Index] as HiddenItem;
            if (item == null) return;

            string appName = item.RawName;
            string displayName = item.DisplayName;
            
            string exeName = appName;
            if (exeName.Contains("|"))
            {
                exeName = exeName.Split('|')[0];
            }

            if (_iconManager != null)
            {
                var icon = _iconManager.GetIcon(exeName);
                if (icon != null)
                {
                    e.Graphics.DrawImage(icon, e.Bounds.Left + 2, e.Bounds.Top + 4, 16, 16);
                }
            }

            Brush textBrush = SystemBrushes.ControlText;
            if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
            {
                textBrush = SystemBrushes.HighlightText;
            }

            e.Graphics.DrawString(displayName, e.Font, textBrush, e.Bounds.Left + 22, e.Bounds.Top + 5);
            e.DrawFocusRectangle();
        }

        private void LoadApps()
        {
            _lstHiddenApps.Items.Clear();
            var hidden = _repo.GetHiddenApps();
            foreach(var app in hidden)
            {
                 string display = app;
                 if (_aliases != null && _aliases.TryGetValue(app, out string alias))
                 {
                     display = alias;
                 }
                _lstHiddenApps.Items.Add(new HiddenItem { RawName = app, DisplayName = display });
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _btnUnhide.Enabled = _lstHiddenApps.SelectedIndices.Count > 0;
            _btnUnhideAll.Enabled = _lstHiddenApps.Items.Count > 0;
        }

        private void OnUnhideClick(object sender, EventArgs e)
        {
            var selected = new List<string>();
            foreach(HiddenItem item in _lstHiddenApps.SelectedItems) selected.Add(item.RawName);

            foreach(var app in selected)
            {
                _repo.UnhideApp(app);
            }
            Changed = true;
            LoadApps();
        }

        private void OnUnhideAllClick(object sender, EventArgs e)
        {
             if (MessageBox.Show(Localization.Get("unhideAllConfirm"), Localization.Get("windowTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
             {
                 var apps = new List<string>();
                 foreach(HiddenItem item in _lstHiddenApps.Items) apps.Add(item.RawName);
                 
                 foreach(var app in apps) _repo.UnhideApp(app);
                 Changed = true;
                 LoadApps();
             }
        }
    }
}
