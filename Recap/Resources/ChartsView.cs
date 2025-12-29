using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace Recap
{
    public class ChartsView : UserControl
    {
        public event Action<string> TimeRangeChanged;     
        public event Action ResetExcludedRequested;

        private Chart _pieChart;
        private FlowLayoutPanel _panelControls;
        private Button _btnDay, _btnWeek, _btnMonth, _btnAll, _btnReset;
        private Label _lblStatus;
        
        private Dictionary<string, double> _currentData;
        private HashSet<string> _excludedApps = new HashSet<string>();
        private IconManager _iconManager;

        private ListView _legendList;
        private ImageList _legendIcons;
        private ContextMenuStrip _contextMenu;
        private string _contextMenuTargetApp;

        public ChartsView()
        {
            InitializeComponent();
        }

        public void SetIconManager(IconManager iconManager)
        {
            _iconManager = iconManager;
            _iconManager.IconLoaded += OnIconLoaded;
        }

        private void OnIconLoaded(string appName)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => OnIconLoaded(appName)));
                return;
            }

            if (_currentData != null && _currentData.ContainsKey(appName))
            {
                RebuildChart();
            }
        }

        public void UpdateLocalization()
        {
            _btnDay.Text = Localization.Get("chartDay");
            _btnWeek.Text = Localization.Get("chartWeek");
            _btnMonth.Text = Localization.Get("chartMonth");
            _btnAll.Text = Localization.Get("chartAll");
            _btnReset.Text = Localization.Get("chartReset");
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            _panelControls = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(5),
                AutoSize = false
            };

            _btnDay = CreateButton(Localization.Get("chartDay"), "Day");
            _btnWeek = CreateButton(Localization.Get("chartWeek"), "Week");
            _btnMonth = CreateButton(Localization.Get("chartMonth"), "Month");
            _btnAll = CreateButton(Localization.Get("chartAll"), "All");
            
            _btnReset = new Button { Text = Localization.Get("chartReset"), AutoSize = true, BackColor = Color.LightCoral, FlatStyle = FlatStyle.Flat };
            _btnReset.Click += (s, e) => 
            { 
                _excludedApps.Clear(); 
                RebuildChart();
                ResetExcludedRequested?.Invoke(); 
            };

            _lblStatus = new Label { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 8, 0, 0) };

            _panelControls.Controls.AddRange(new Control[] { _btnDay, _btnWeek, _btnMonth, _btnAll, _btnReset, _lblStatus });

            var splitContainer = new SplitContainer 
            { 
                Dock = DockStyle.Fill, 
                FixedPanel = FixedPanel.Panel2,
                SplitterDistance = 500        
            };

            _pieChart = new Chart
            {
                Dock = DockStyle.Fill,
                BackColor = Color.WhiteSmoke
            };

            var area = new ChartArea("MainArea");
            area.BackColor = Color.Transparent;
            _pieChart.ChartAreas.Add(area);

            _pieChart.Legends.Clear();

            _pieChart.Series.Add(new Series("Apps")
            {
                ChartType = SeriesChartType.Pie,
                IsValueShownAsLabel = false,
                Label = "#PERCENT{P1}"
            });

            _pieChart.MouseClick += OnChartMouseClick;

            _legendList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.WhiteSmoke
            };
            _legendList.Columns.Add("App", -2);   

            _legendIcons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            _legendList.SmallImageList = _legendIcons;
            _legendList.MouseClick += OnLegendMouseClick;

            _contextMenu = new ContextMenuStrip();
            var itemExclude = new ToolStripMenuItem("Исключить приложение");
            itemExclude.Click += OnExcludeAppClick;
            _contextMenu.Items.Add(itemExclude);

            splitContainer.Panel1.Controls.Add(_pieChart);
            splitContainer.Panel2.Controls.Add(_legendList);
            
            splitContainer.Panel2MinSize = 50;
            
            this.Load += (s, e) => 
            {
                if (this.Width > 300)
                {
                    try 
                    {
                        splitContainer.SplitterDistance = this.Width - 250;
                    }
                    catch { }
                }
            };

            this.Controls.Add(splitContainer);
            this.Controls.Add(_panelControls);
        }

        private Button CreateButton(string text, string tag)
        {
            var btn = new Button { Text = text, Tag = tag, AutoSize = true, FlatStyle = FlatStyle.Flat };
            btn.Click += (s, e) => 
            {
                foreach(Control c in _panelControls.Controls) if(c is Button b && b != _btnReset) b.BackColor = SystemColors.Control;
                btn.BackColor = Color.LightBlue;
                TimeRangeChanged?.Invoke(tag);
            };
            return btn;
        }

        public void SetStatus(string text)
        {
            if (this.InvokeRequired) this.Invoke((Action)(() => _lblStatus.Text = text));
            else _lblStatus.Text = text;
        }

        public void SetData(Dictionary<string, double> appDurations)
        {
            _currentData = appDurations;
            RebuildChart();
        }

        private void RebuildChart()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)RebuildChart);
                return;
            }

            _pieChart.Series[0].Points.Clear();
            _legendList.Items.Clear();
            _legendIcons.Images.Clear();

            if (_currentData == null || _currentData.Count == 0) return;

            var filtered = _currentData.Where(kv => !_excludedApps.Contains(kv.Key)).ToList();
            double total = filtered.Sum(kv => kv.Value);

            var sorted = filtered.OrderByDescending(kv => kv.Value).ToList();

            _legendList.BeginUpdate();

            foreach (var item in sorted)
            {
                string appName = item.Key;
                double val = item.Value;
                double percentage = val / total;
                
                if (percentage < 0.01) continue;

                int idx = _pieChart.Series[0].Points.AddXY(appName, val);
                DataPoint point = _pieChart.Series[0].Points[idx];

                TimeSpan t = TimeSpan.FromSeconds(val);
                point.ToolTip = $"{appName}\n{(int)t.TotalHours}h {t.Minutes}m";
                point.LegendText = appName;

                if (percentage < 0.03)
                {
                    point.Label = "";
                }
                else
                {
                    point.Label = "#PERCENT{P1}";
                }

                Color domColor = Color.Black;
                if (_iconManager != null)
                {
                    var icon = _iconManager.GetIcon(appName);
                    if (icon != null)
                    {
                        if (!_legendIcons.Images.ContainsKey(appName))
                        {
                            _legendIcons.Images.Add(appName, icon);
                        }

                        using (var bmp = new Bitmap(icon))
                        {
                            domColor = bmp.GetDominantColor();
                        }
                    }
                    else
                    {
                        if (!_legendIcons.Images.ContainsKey(appName))
                        {
                            using(var bmp = new Bitmap(16,16)) _legendIcons.Images.Add(appName, bmp);
                        }
                    }
                }

                point.Color = domColor;

                var lvItem = new ListViewItem($"{appName} ({percentage:P1})");
                lvItem.ImageKey = appName;
                lvItem.ForeColor = domColor;
                _legendList.Items.Add(lvItem);
            }
            
            _legendList.EndUpdate();
            
            if (_legendList.Columns.Count > 0) _legendList.Columns[0].Width = -2;
        }

        private void OnLegendMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var hit = _legendList.HitTest(e.Location);
                if (hit.Item != null)
                {
                    _contextMenuTargetApp = hit.Item.Text;
                    _contextMenu.Show(_legendList, e.Location);
                }
            }
        }

        private void OnExcludeAppClick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_contextMenuTargetApp))
            {
                _excludedApps.Add(_contextMenuTargetApp);
                RebuildChart();
            }
        }

        private void OnChartMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                HitTestResult hit = _pieChart.HitTest(e.X, e.Y);
                if (hit.ChartElementType == ChartElementType.DataPoint)
                {
                    var point = _pieChart.Series[0].Points[hit.PointIndex];
                    string appName = point.AxisLabel;
                    if (string.IsNullOrEmpty(appName)) appName = point.LegendText;

                    if (!string.IsNullOrEmpty(appName))
                    {
                        _contextMenuTargetApp = appName;
                        _contextMenu.Show(_pieChart, e.Location);
                    }
                }
            }
        }
    }
}
