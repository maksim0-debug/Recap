using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Recap
{
    public class ExcludeAppsForm : Form
    {
        private ListView _lvApps;
        private Button _btnExcludeSelected;
        private Button _btnExcludeAll;
        private Button _btnCancel;
        private IconManager _iconManager;

        public List<string> SelectedApps { get; private set; } = new List<string>();
        public bool ExcludeAll { get; private set; } = false;

        public ExcludeAppsForm(string groupName, List<string> rawApps, IconManager iconManager)
        {
            _iconManager = iconManager;
            InitializeComponent();
            Text = $"{Localization.Get("manageGroupTitle")}: {groupName}";
            
            if (rawApps != null)
            {
                var imageList = new ImageList();
                imageList.ImageSize = new Size(16, 16);
                imageList.ColorDepth = ColorDepth.Depth32Bit;
                _lvApps.SmallImageList = imageList;

                int index = 0;
                foreach (var app in rawApps)
                {
                    var icon = _iconManager.GetIcon(app);
                    if (icon != null)
                    {
                        imageList.Images.Add(icon);
                    }
                    else
                    {
                        imageList.Images.Add(new Bitmap(16, 16));
                    }

                    var item = new ListViewItem(app);
                    item.ImageIndex = index;
                    item.Checked = false;
                    _lvApps.Items.Add(item);
                    index++;
                }
            }
        }

        private void InitializeComponent()
        {
            this.Size = new Size(350, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var lblInfo = new Label
            {
                Text = Localization.Get("excludeApp"), 
                Dock = DockStyle.Top,
                Padding = new Padding(10),
                Height = 40
            };

            _lvApps = new ListView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true
            };
            
            _lvApps.Columns.Add("App", -2); 

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(5)
            };

            _btnExcludeSelected = new Button
            {
                Text = Localization.Get("excludeSelected"),
                DialogResult = DialogResult.OK,
                Width = 120,
                Left = 10,
                Top = 10,
                Enabled = false
            };
            _btnExcludeSelected.Click += (s, e) => 
            {
                SelectedApps.Clear();
                foreach (ListViewItem item in _lvApps.CheckedItems)
                {
                    SelectedApps.Add(item.Text);
                }
            };

            _btnExcludeAll = new Button
            {
                Text = Localization.Get("excludeAll"),
                DialogResult = DialogResult.OK,
                Width = 100,
                Left = 140,
                Top = 10
            };
            _btnExcludeAll.Click += (s, e) => { ExcludeAll = true; };

            _btnCancel = new Button
            {
                Text = Localization.Get("cancel"),
                DialogResult = DialogResult.Cancel,
                Width = 80,
                Left = 250,
                Top = 10
            };

            _lvApps.ItemChecked += (s, e) =>
            {
                this.BeginInvoke((Action)(() =>
                {
                    _btnExcludeSelected.Enabled = _lvApps.CheckedItems.Count > 0;
                }));
            };

            pnlBottom.Controls.Add(_btnExcludeSelected);
            pnlBottom.Controls.Add(_btnExcludeAll);
            pnlBottom.Controls.Add(_btnCancel);

            this.Controls.Add(_lvApps);
            this.Controls.Add(lblInfo);
            this.Controls.Add(pnlBottom);
        }
    }
}
