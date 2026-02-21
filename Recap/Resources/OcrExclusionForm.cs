using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;

namespace Recap
{
    public class OcrExclusionForm : Form
    {
        private ListView _lvBlacklist;
        private Button _btnAddFromHistory;
        private Button _btnAddFromFile;
        private Button _btnRemove;
        private Button _btnOk;
        private IconManager _iconManager;
        private FrameRepository _repo;

        public System.Collections.Specialized.StringCollection Blacklist { get; private set; }

        public OcrExclusionForm(System.Collections.Specialized.StringCollection currentBlacklist, FrameRepository repo, IconManager iconManager)
        {
            _repo = repo;
            _iconManager = iconManager;
            
            Blacklist = new System.Collections.Specialized.StringCollection();
            if (currentBlacklist != null)
            {
                foreach (var item in currentBlacklist) Blacklist.Add(item);
            }

            if (_iconManager != null)
            {
                _iconManager.IconLoaded += OnIconLoaded;
            }

            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(500, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Text = Localization.Get("ocrExclusionTitle");

            var lblInfo = new Label
            {
                Text = Localization.Get("ocrExclusionInfo"),
                Dock = DockStyle.Top,
                Padding = new Padding(10),
                Height = 50,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lvBlacklist = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                MultiSelect = true
            };
            _lvBlacklist.Columns.Add("App", -2);
            
            var imageList = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            _lvBlacklist.SmallImageList = imageList;

            var pnlRight = new Panel
            {
                Dock = DockStyle.Right,
                Width = 140,
                Padding = new Padding(5)
            };

            _btnAddFromHistory = new Button { Text = Localization.Get("addFromHistory"), Dock = DockStyle.Top, Height = 30, Margin = new Padding(0, 0, 0, 5) };
            _btnAddFromFile = new Button { Text = Localization.Get("addFromFile"), Dock = DockStyle.Top, Height = 30, Margin = new Padding(0, 0, 0, 5) };
            _btnRemove = new Button { Text = Localization.Get("removeSelected"), Dock = DockStyle.Top, Height = 30, Margin = new Padding(0, 0, 0, 5), Enabled = false };
            
            _btnOk = new Button { Text = Localization.Get("ok"), Dock = DockStyle.Bottom, Height = 30, DialogResult = DialogResult.OK };

            _btnAddFromHistory.Click += BtnAddFromHistory_Click;
            _btnAddFromFile.Click += BtnAddFromFile_Click;
            _btnRemove.Click += BtnRemove_Click;
            _lvBlacklist.SelectedIndexChanged += (s, e) => _btnRemove.Enabled = _lvBlacklist.SelectedItems.Count > 0;

            pnlRight.Controls.Add(_btnRemove);
            pnlRight.Controls.Add(_btnAddFromFile);
            pnlRight.Controls.Add(_btnAddFromHistory);
            pnlRight.Controls.Add(_btnOk);

            var pnlSpacer = new Panel { Dock = DockStyle.Top, Height = 10 };
            pnlRight.Controls.Add(pnlSpacer);
            pnlRight.Controls.SetChildIndex(_btnAddFromHistory, 0);
            pnlRight.Controls.SetChildIndex(_btnAddFromFile, 1);
            pnlRight.Controls.SetChildIndex(_btnRemove, 2);
            pnlRight.Controls.SetChildIndex(pnlSpacer, 3);
            pnlRight.Controls.SetChildIndex(_btnOk, 4);


            this.Controls.Add(_lvBlacklist);
            this.Controls.Add(pnlRight);
            this.Controls.Add(lblInfo);
        }

        private void RefreshList()
        {
            _lvBlacklist.BeginUpdate();
            _lvBlacklist.Items.Clear();
            _lvBlacklist.SmallImageList.Images.Clear();

            int imgIndex = 0;
            foreach (string appName in Blacklist)
            {
                var item = new ListViewItem(appName);
                
                string searchKey = appName + ".exe";
                var icon = _iconManager?.GetIcon(searchKey);
                if (icon != null)
                {
                    _lvBlacklist.SmallImageList.Images.Add(appName, icon);
                    item.ImageKey = appName;
                }
                else
                {
                }

                _lvBlacklist.Items.Add(item);
            }
            _lvBlacklist.EndUpdate();
        }

        private void BtnAddFromFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Executables (*.exe)|*.exe";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string fileName = Path.GetFileNameWithoutExtension(ofd.FileName);
                    if (!Blacklist.Contains(fileName))
                    {
                        Blacklist.Add(fileName);
                        
                        var icon = Icon.ExtractAssociatedIcon(ofd.FileName);
                        if (icon != null)
                        {
                            _lvBlacklist.SmallImageList.Images.Add(fileName, icon);
                        }
                        
                        RefreshList();
                    }
                }
            }
        }

        private void BtnAddFromHistory_Click(object sender, EventArgs e)
        {
            var appsMap = _repo.GetAppMap();
            var uniqueApps = appsMap.Values
                .Select(a => FrameHelper.ParseAppName(a).ExeName.Replace(".exe", ""))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var candidates = uniqueApps.Where(a => !Blacklist.Contains(a)).ToList();

            var selector = new ExcludeAppsForm("History", candidates, _iconManager);
            selector.Text = Localization.Get("selectAppsToBlacklist");
            
            if (selector.ShowDialog() == DialogResult.OK)
            {
                foreach (var app in selector.SelectedApps)
                {
                    if (!Blacklist.Contains(app))
                    {
                        Blacklist.Add(app);
                    }
                }
                RefreshList();
            }
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in _lvBlacklist.SelectedItems)
            {
                Blacklist.Remove(item.Text);
            }
            RefreshList();
        }

        private void OnIconLoaded(string iconKey)
        {
            if (this.IsDisposed || _lvBlacklist.IsDisposed) return;

            string appName = iconKey.Replace(".exe", "");

            if (Blacklist.Contains(appName))
            {
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed) return;

                    var newIcon = _iconManager.GetIcon(iconKey);
                    if (newIcon != null)
                    {
                        if (_lvBlacklist.SmallImageList.Images.ContainsKey(appName))
                        {
                            _lvBlacklist.SmallImageList.Images.RemoveByKey(appName);
                        }
                        _lvBlacklist.SmallImageList.Images.Add(appName, newIcon);
                        
                        _lvBlacklist.Invalidate();
                    }
                }));
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_iconManager != null)
            {
                _iconManager.IconLoaded -= OnIconLoaded;
            }
            base.OnFormClosed(e);
        }
    }
}
