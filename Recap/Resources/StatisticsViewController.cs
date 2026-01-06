using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class StatisticsViewController : IDisposable
    {
        public event Action<DateTime> DaySelected;

        private readonly ActivityHeatmap _activityHeatmap;
        private readonly ChartsView _chartsView;
        private readonly Button _btnPrevMonth;
        private readonly Button _btnNextMonth;
        private readonly Button _btnRefresh;    
        private readonly Label _lblMonth;
        private readonly Label _lblTotal;
        private FrameRepository _frameRepository;
        private readonly OcrDatabase _ocrDb;
        private Dictionary<string, string> _aliases = new Dictionary<string, string>();

        private DateTime _displayMonth;
        private double _lastTotalSeconds = 0;
        private AppSettings _settings;

        public StatisticsViewController(
            ActivityHeatmap activityHeatmap, 
            ChartsView chartsView,
            Button btnPrevMonth, 
            Button btnNextMonth, 
            Button btnRefresh,   
            Label lblMonth, 
            Label lblTotal,
            FrameRepository frameRepository, 
            AppSettings settings,
            IconManager iconManager,
            OcrDatabase ocrDb)
        {
            _activityHeatmap = activityHeatmap;
            _chartsView = chartsView;
            _btnPrevMonth = btnPrevMonth;
            _btnNextMonth = btnNextMonth;
            _btnRefresh = btnRefresh;
            _lblMonth = lblMonth;
            _lblTotal = lblTotal;
            _frameRepository = frameRepository;
            _settings = settings;
            _ocrDb = ocrDb;

            RefreshAliases();

            _chartsView.SetIconManager(iconManager);
            _chartsView.TimeRangeChanged += OnChartTimeRangeChanged;

            _displayMonth = DateTime.Today;

            _btnPrevMonth.Click += OnPrevMonthClick;
            _btnNextMonth.Click += OnNextMonthClick;
            if (_btnRefresh != null) _btnRefresh.Click += OnRefreshClick;    
            _activityHeatmap.DayClicked += OnHeatmapDayClicked;
        }

        public void RefreshAliases()
        {
            if (_ocrDb != null)
            {
                _aliases = _ocrDb.LoadAppAliases();
            }
        }

        private async void OnRefreshClick(object sender, EventArgs e)
        {
            var startDate = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                _frameRepository.InvalidateActivityCache(date);
            }

            await LoadHeatmapDataAsync();
        }

        public async Task ActivateAsync()
        {
            await LoadHeatmapDataAsync();
            OnChartTimeRangeChanged("Day");
        }

        private async void OnChartTimeRangeChanged(string range)
        {
            DateTime start = DateTime.MinValue;
            DateTime end = DateTime.MaxValue;

            if (range == "Range")
            {
                using (var form = new RangePickerForm())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        start = form.StartDate;
                        end = form.EndDate;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            _chartsView.SetStatus(Localization.Get("loadingData"));
            
            if (range == "Day")
            {
                start = DateTime.Today;
                end = DateTime.Today.AddDays(1).AddTicks(-1);
            }
            else if (range == "Week")
            {
                start = DateTime.Today.AddDays(-6);      
                end = DateTime.Today.AddDays(1).AddTicks(-1);
            }
            else if (range == "Month")
            {
                start = DateTime.Today.AddDays(-29);
                end = DateTime.Today.AddDays(1).AddTicks(-1);
            }
            else if (range == "All")
            {
            }

            var stats = await Task.Run(() => 
            {
                var appMap = _frameRepository.GetAppMap();

                if (range == "Day")
                {
                    var frames = _frameRepository.LoadMiniFramesForDateFast(start);
                    return AggregateFrames(frames, appMap);
                }
                else if (range == "All")
                {
                    var frames = _frameRepository.GlobalSearch("");
                    var miniFrames = new List<MiniFrame>(frames.Count);
                    var nameToId = appMap.ToDictionary(x => x.Value, x => x.Key);
                    foreach(var f in frames)
                    {
                        int id = -1;
                        if (f.AppName != null && nameToId.TryGetValue(f.AppName, out int val)) id = val;
                        miniFrames.Add(new MiniFrame { TimestampTicks = f.TimestampTicks, AppId = id, IntervalMs = f.IntervalMs });
                    }
                    return AggregateFrames(miniFrames, appMap);
                }
                else
                {
                    return AggregateRange(start, end, appMap);
                }
            });

            Dictionary<string, string> aliasMap = null;
            if (_aliases != null && _aliases.Count > 0)
            {
                aliasMap = new Dictionary<string, string>();
                foreach (var kvp in _aliases)
                {
                    if (!aliasMap.ContainsKey(kvp.Value))
                    {
                        aliasMap[kvp.Value] = kvp.Key;
                    }
                }
            }

            _chartsView.SetData(stats, aliasMap);
            _chartsView.SetStatus("");
        }

        private Dictionary<string, double> AggregateFrames(List<MiniFrame> frames, Dictionary<int, string> appMap)
        {
            var result = new Dictionary<string, double>();
            if (frames == null || frames.Count == 0) return result;

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                long durationMs = f.IntervalMs;
                if (durationMs <= 0) durationMs = 3000;  

                string app = "";
                if (appMap.TryGetValue(f.AppId, out string name)) app = name;
                
                if (app.Contains("|")) app = app.Split('|')[0]; 
                
                if (_aliases != null && _aliases.ContainsKey(app)) app = _aliases[app];   

                if (!result.ContainsKey(app)) result[app] = 0;
                result[app] += (durationMs / 1000.0);
            }
            return result;
        }

        private Dictionary<string, double> AggregateRange(DateTime start, DateTime end, Dictionary<int, string> appMap)
        {
            var result = new Dictionary<string, double>();
            var days = _frameRepository.GetDaysWithData();      

            foreach (var day in days)
            {
                if (day >= start.Date && day <= end.Date)
                {
                    var frames = _frameRepository.LoadMiniFramesForDateFast(day);
                    var dayStats = AggregateFrames(frames, appMap);
                    
                    foreach(var kv in dayStats)
                    {
                        if (!result.ContainsKey(kv.Key)) result[kv.Key] = 0;
                        result[kv.Key] += kv.Value;
                    }
                }
            }
            return result;
        }

        public void UpdateSettings(AppSettings newSettings, FrameRepository newFrameRepository)
        {
            _settings = newSettings;
            _frameRepository = newFrameRepository;
        }

        private async void OnPrevMonthClick(object sender, EventArgs e)
        {
            _displayMonth = _displayMonth.AddMonths(-1);
            await LoadHeatmapDataAsync();
        }

        private async void OnNextMonthClick(object sender, EventArgs e)
        {
            _displayMonth = _displayMonth.AddMonths(1);
            await LoadHeatmapDataAsync();
        }

        private void OnHeatmapDayClicked(DateTime date)
        {
            DaySelected?.Invoke(date);
        }

        private async Task LoadHeatmapDataAsync()
        {
            var parentForm = _activityHeatmap.FindForm();
            if (parentForm != null) parentForm.Cursor = Cursors.WaitCursor;

            var culture = new CultureInfo(_settings.Language);
            _lblMonth.Text = _displayMonth.ToString("MMMM yyyy", culture);

            var startDate = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var data = await Task.Run(() => _frameRepository.GetActivityDurations(startDate, endDate));

            double totalSeconds = 0;
            foreach (var kv in data)
            {
                totalSeconds += kv.Value.TotalSeconds;
            }
            _lastTotalSeconds = totalSeconds;
            int totalHours = (int)(totalSeconds / 3600);
            _lblTotal.Text = Localization.Format("totalMonthActivity", totalHours);

            _activityHeatmap.BaseColor = System.Drawing.Color.FromArgb(16, 124, 16);  
            _activityHeatmap.ForeColor = System.Drawing.Color.Black;
            _activityHeatmap.BackColor = System.Drawing.Color.FromArgb(243, 243, 243);

            _activityHeatmap.SetDisplayMonth(_displayMonth);
            _activityHeatmap.SetData(data);

            if (parentForm != null) parentForm.Cursor = Cursors.Default;
        }

        public void UpdateLocalization()
        {
            var culture = new CultureInfo(_settings.Language);
            _lblMonth.Text = _displayMonth.ToString("MMMM yyyy", culture);
            
            int totalHours = (int)(_lastTotalSeconds / 3600);
            if (_lblTotal != null) _lblTotal.Text = Localization.Format("totalMonthActivity", totalHours);
        }

        public void Dispose()
        {
            _btnPrevMonth.Click -= OnPrevMonthClick;
            _btnNextMonth.Click -= OnNextMonthClick;
            _activityHeatmap.DayClicked -= OnHeatmapDayClicked;
            if (_chartsView != null) _chartsView.TimeRangeChanged -= OnChartTimeRangeChanged;
        }
    }
}