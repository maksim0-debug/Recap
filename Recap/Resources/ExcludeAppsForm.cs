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
        private TextBox _txtSearch;
        private Button _btnExcludeSelected;
        private Button _btnExcludeAll;
        private Button _btnCancel;
        
        private IconManager _iconManager;
        private List<string> _allApps;
        private HashSet<string> _checkedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ImageList _imageList;

        public List<string> SelectedApps => _checkedApps.ToList();
        public bool ExcludeAll { get; private set; } = false;

        public ExcludeAppsForm(string groupName, List<string> rawApps, IconManager iconManager)
        {
            _iconManager = iconManager;
            _allApps = rawApps ?? new List<string>();

            InitializeComponent();
            this.Text = $"{Localization.Get("manageGroupTitle")}: {groupName}";

            if (_iconManager != null)
            {
                _iconManager.IconLoaded += OnIconLoaded;
            }

            LoadInitialData();
            AppStyler.Apply(this);
        }

        private void InitializeComponent()
        {
            this.Size = new Size(450, 550);      
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;     
            this.MinimumSize = new Size(400, 400);
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(10)
            };

            var lblInfo = new Label
            {
                Text = Localization.Get("excludeApp"), 
                Location = new Point(10, 10),
                AutoSize = true
            };

            _txtSearch = new TextBox
            {
                Location = new Point(10, 32),
                Width = this.ClientSize.Width - 20,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _txtSearch.Text = Localization.Get("searchApps");
            _txtSearch.ForeColor = Color.Gray;
            _txtSearch.GotFocus += (s, e) => { if (_txtSearch.Text == Localization.Get("searchApps")) { _txtSearch.Text = ""; _txtSearch.ForeColor = SystemColors.WindowText; } };
            _txtSearch.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(_txtSearch.Text)) { _txtSearch.Text = Localization.Get("searchApps"); _txtSearch.ForeColor = Color.Gray; } };
            _txtSearch.TextChanged += TxtSearch_TextChanged;

            pnlTop.Controls.Add(lblInfo);
            pnlTop.Controls.Add(_txtSearch);

            _lvApps = new ListView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                HideSelection = false
            };
            _lvApps.Columns.Add("App", -2);
            _lvApps.ItemChecked += LvApps_ItemChecked;

            _imageList = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            _lvApps.SmallImageList = _imageList;

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
                Width = 140,
                Left = 10,
                Top = 10,
                Enabled = false,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            _btnExcludeAll = new Button
            {
                Text = Localization.Get("excludeAll"),
                DialogResult = DialogResult.OK,
                Width = 120,
                Left = 160,
                Top = 10,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _btnExcludeAll.Click += (s, e) => { ExcludeAll = true; };

            _btnCancel = new Button
            {
                Text = Localization.Get("cancel"),
                DialogResult = DialogResult.Cancel,
                Width = 100,
                Left = 290,
                Top = 10,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            pnlBottom.Controls.Add(_btnExcludeSelected);
            pnlBottom.Controls.Add(_btnExcludeAll);
            pnlBottom.Controls.Add(_btnCancel);

            this.Controls.Add(_lvApps);
            this.Controls.Add(pnlTop);
            this.Controls.Add(pnlBottom);
        }

        private void LoadInitialData()
        {
            foreach (var app in _allApps)
            {
                string searchKey = app + ".exe";
                var icon = _iconManager?.GetIcon(searchKey) ?? new Bitmap(16, 16);
                
                _imageList.Images.Add(app, icon); 
            }

            ApplyFilter("");    
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            string searchText = _txtSearch.Text;
            if (searchText == Localization.Get("searchApps")) searchText = "";
            
            ApplyFilter(searchText);
        }

        private void ApplyFilter(string searchText)
        {
            _lvApps.BeginUpdate();
            _lvApps.ItemChecked -= LvApps_ItemChecked;        
            _lvApps.Items.Clear();

            var filtered = string.IsNullOrWhiteSpace(searchText) 
                ? _allApps 
                : _allApps.Where(a => a.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            foreach (var app in filtered)
            {
                var item = new ListViewItem(app)
                {
                    ImageKey = app,     
                    Checked = _checkedApps.Contains(app)      
                };
                _lvApps.Items.Add(item);
            }

            if (_lvApps.Columns.Count > 0) _lvApps.Columns[0].Width = -2;

            _lvApps.ItemChecked += LvApps_ItemChecked;
            _lvApps.EndUpdate();
        }

        private void LvApps_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            string appName = e.Item.Text;
            if (e.Item.Checked)
                _checkedApps.Add(appName);
            else
                _checkedApps.Remove(appName);

            _btnExcludeSelected.Enabled = _checkedApps.Count > 0;
        }

        private void OnIconLoaded(string iconKey)
        {
            if (this.IsDisposed || _lvApps.IsDisposed) return;

            string appName = iconKey.Replace(".exe", "");

            if (_allApps.Contains(appName))
            {
                this.BeginInvoke((Action)(() =>
                {
                    if (this.IsDisposed) return;

                    var newIcon = _iconManager.GetIcon(iconKey);
                    if (newIcon != null)
                    {
                        if (_imageList.Images.ContainsKey(appName))
                        {
                            _imageList.Images.RemoveByKey(appName);
                        }
                        _imageList.Images.Add(appName, newIcon);
                        
                        foreach (ListViewItem item in _lvApps.Items)
                        {
                            string key = item.Text;
                            item.ImageKey = null;
                            item.ImageKey = key;
                        }
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