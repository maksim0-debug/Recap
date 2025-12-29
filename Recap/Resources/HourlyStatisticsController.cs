using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class HourlyStatisticsController : IDisposable
    {
        private readonly HourlyActivityHeatmap _heatmap;
        private readonly FrameRepository _repository;
        private readonly ComboBox _periodSelector;
        private readonly DateTimePicker _startDatePicker;
        private readonly DateTimePicker _endDatePicker;
        private readonly Label _customRangeLabel;

        public HourlyStatisticsController(
            HourlyActivityHeatmap heatmap,
            FrameRepository repository,
            ComboBox periodSelector,
            DateTimePicker startDatePicker,
            DateTimePicker endDatePicker,
            Label customRangeLabel)
        {
            _heatmap = heatmap;
            _repository = repository;
            _periodSelector = periodSelector;
            _startDatePicker = startDatePicker;
            _endDatePicker = endDatePicker;
            _customRangeLabel = customRangeLabel;

            _periodSelector.SelectedIndexChanged += OnPeriodChanged;
            _startDatePicker.ValueChanged += OnDateChanged;
            _endDatePicker.ValueChanged += OnDateChanged;

            UpdateLocalization();
        }

        public void UpdateLocalization()
        {
            int selectedIndex = _periodSelector.SelectedIndex;
            
            _periodSelector.Items.Clear();
            _periodSelector.Items.AddRange(new object[] {
                Localization.Get("periodWeek"),
                Localization.Get("periodMonth"),
                Localization.Get("periodAll"),
                Localization.Get("periodCustom")
            });

            if (selectedIndex >= 0 && selectedIndex < _periodSelector.Items.Count)
            {
                _periodSelector.SelectedIndex = selectedIndex;
            }
            else
            {
                _periodSelector.SelectedIndex = 0;
            }
        }

        private async void OnPeriodChanged(object sender, EventArgs e)
        {
            if (_periodSelector.SelectedIndex < 0) return;

            int index = _periodSelector.SelectedIndex;
            bool isCustom = index == 3;    

            _startDatePicker.Visible = isCustom;
            _endDatePicker.Visible = isCustom;
            _customRangeLabel.Visible = isCustom;

            await UpdateDataAsync();
        }

        private async void OnDateChanged(object sender, EventArgs e)
        {
            if (_periodSelector.SelectedIndex == 3)  
            {
                await UpdateDataAsync();
            }
        }

        public async Task UpdateDataAsync()
        {
            if (_periodSelector.SelectedIndex < 0) return;

            int index = _periodSelector.SelectedIndex;
            DateTime start = DateTime.Today;
            DateTime end = DateTime.Today;

            if (index == 0)  
            {
                start = DateTime.Today.AddDays(-6);
                end = DateTime.Today;
            }
            else if (index == 1)  
            {
                start = DateTime.Today.AddDays(-29);
                end = DateTime.Today;
            }
            else if (index == 2)   
            {
                start = DateTime.MinValue;
                end = DateTime.MaxValue;
            }
            else if (index == 3)  
            {
                start = _startDatePicker.Value.Date;
                end = _endDatePicker.Value.Date;
            }

            var data = await Task.Run(() => _repository.GetHourlyActivity(start, end));
            
            _heatmap.SetData(data);
        }

        public void Dispose()
        {
            _periodSelector.SelectedIndexChanged -= OnPeriodChanged;
            _startDatePicker.ValueChanged -= OnDateChanged;
            _endDatePicker.ValueChanged -= OnDateChanged;
        }
    }
}
