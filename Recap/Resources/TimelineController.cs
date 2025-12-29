using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Recap
{
    public class TimelineController : IDisposable
    {
        public event Action<int> TimeChanged;

        private readonly TrackBar _timeTrackBar;
        private readonly Label _lblTime;
        private readonly Label _lblInfo;
        private readonly CheckBox _chkAutoScroll;
        private readonly DateTimePicker _datePicker;

        private readonly FrameRepository _frameRepository;
        private readonly TimelinePreviewManager _previewManager;

        private List<MiniFrame> _currentFrames = new List<MiniFrame>();
        private bool _isDragging = false;
        private int _lastHoverIndex = -1;

        public TimelineController(
            TrackBar timeTrackBar,
            Label lblTime,
            Label lblInfo,
            CheckBox chkAutoScroll,
            DateTimePicker datePicker,
            FrameRepository frameRepository,
            IconManager iconManager)
        {
            _timeTrackBar = timeTrackBar;
            _lblTime = lblTime;
            _lblInfo = lblInfo;
            _chkAutoScroll = chkAutoScroll;
            _datePicker = datePicker;
            _frameRepository = frameRepository;

            _previewManager = new TimelinePreviewManager(frameRepository, iconManager);

            _timeTrackBar.Scroll += OnTrackBarScroll;
            _timeTrackBar.MouseDown += TimeTrackBar_MouseDown;
            _timeTrackBar.MouseUp += TimeTrackBar_MouseUp;
            _timeTrackBar.MouseMove += TimeTrackBar_MouseMove;
            _timeTrackBar.MouseLeave += TimeTrackBar_MouseLeave;

            var parentForm = _timeTrackBar.FindForm();
            if (parentForm != null)
            {
                parentForm.Deactivate += OnParentFormDeactivate;
                parentForm.Move += OnParentFormMove;
            }
        }

        public int CurrentIndex
        {
            get => _timeTrackBar.Value;
            set
            {
                if (value >= _timeTrackBar.Minimum && value <= _timeTrackBar.Maximum)
                {
                    _timeTrackBar.Value = value;
                    UpdateInfoLabel();
                }
            }
        }

        public void SetFrames(List<MiniFrame> frames, bool isLiveUpdate, bool isInitialLoad)
        {
            _currentFrames = frames;
            _timeTrackBar.Tag = frames;

            if (frames == null || frames.Count == 0)
            {
                _timeTrackBar.Enabled = false;
                _timeTrackBar.Minimum = 0;
                _timeTrackBar.Maximum = 0;
                _lblTime.Text = "";
                UpdateInfoLabel();
            }
            else
            {
                _timeTrackBar.Enabled = true;
                _timeTrackBar.Minimum = 0;

                _timeTrackBar.Maximum = frames.Count - 1;

                if ((isLiveUpdate && _chkAutoScroll.Checked) || isInitialLoad)
                {
                    int targetValue = _timeTrackBar.Maximum;

                    _timeTrackBar.Value = targetValue;
                    TimeChanged?.Invoke(_timeTrackBar.Value);
                }
            }
            UpdateInfoLabel();
        }

        public void Navigate(int offset)
        {
            if (_currentFrames != null && _currentFrames.Count > 0)
            {
                int newValue = _timeTrackBar.Value + offset;
                newValue = Math.Max(_timeTrackBar.Minimum, Math.Min(newValue, _timeTrackBar.Maximum));

                if (_timeTrackBar.Value != newValue)
                {
                    _timeTrackBar.Value = newValue;
                    TimeChanged?.Invoke(newValue);
                    UpdateInfoLabel();
                }
            }
        }

        public void UpdateTimeLabel(DateTime time, bool showDate)
        {
            if (showDate)
            {
                _lblTime.Text = $"{time:HH:mm:ss}\n{time:dd.MM.yyyy}";
            }
            else
            {
                _lblTime.Text = time.ToString("HH:mm:ss");
            }
        }

        public void UpdateInfoLabel()
        {
            if (_currentFrames == null || _currentFrames.Count == 0)
            {
                _lblInfo.Text = "";
                return;
            }

            int currentIndex = _timeTrackBar.Value;
            if (currentIndex >= _currentFrames.Count) currentIndex = _currentFrames.Count - 1;
            if (currentIndex < 0) currentIndex = 0;

            int currentFrameNum = currentIndex + 1;
            int totalFrames = _currentFrames.Count;

            DateTime actualFrameDate = _currentFrames[currentIndex].GetTime().Date;
            long totalSize = _frameRepository.GetTotalFileSizeForDate(actualFrameDate);

            _lblInfo.Text = Localization.Format("frame", currentFrameNum, totalFrames, totalSize / 1024.0 / 1024.0);
        }

        private void OnTrackBarScroll(object sender, EventArgs e)
        {
            TimeChanged?.Invoke(_timeTrackBar.Value);
            UpdateInfoLabel();
        }

        private void TimeTrackBar_MouseDown(object sender, MouseEventArgs e)
        {
            _isDragging = true;
            _previewManager.Hide();
            MoveTrackBarToMouse(e.X);
        }

        private void TimeTrackBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_currentFrames == null || _currentFrames.Count == 0) return;
            if (_isDragging)
            {
                MoveTrackBarToMouse(e.X);
                return;
            }
            if (e.Y < -40 || e.Y > 40)
            {
                _previewManager.Hide();
                return;
            }

            int index = CalculateIndexFromMouseX(e.X);
            if (index >= 0 && index < _currentFrames.Count)
            {
                if (index != _lastHoverIndex)
                {
                    _lastHoverIndex = index;
                    _previewManager.Show(_currentFrames[index], _timeTrackBar, e.X);
                }
            }
            else
            {
                _previewManager.Hide();
            }
        }

        private void TimeTrackBar_MouseUp(object sender, MouseEventArgs e) => _isDragging = false;
        private void TimeTrackBar_MouseLeave(object sender, EventArgs e) { _previewManager.Hide(); _lastHoverIndex = -1; }
        private void OnParentFormDeactivate(object sender, EventArgs e) => _previewManager.Hide();
        private void OnParentFormMove(object sender, EventArgs e) => _previewManager.Hide();

        private void MoveTrackBarToMouse(int mouseX)
        {
            int newValue = CalculateIndexFromMouseX(mouseX);
            if (newValue != _timeTrackBar.Value)
            {
                _timeTrackBar.Value = newValue;
                TimeChanged?.Invoke(newValue);
                UpdateInfoLabel();
            }
        }

        private int CalculateIndexFromMouseX(int mouseX)
        {
            if (_timeTrackBar.Width <= 0) return 0;
            double dblValue = ((double)mouseX / (double)_timeTrackBar.Width) * (_timeTrackBar.Maximum - _timeTrackBar.Minimum);
            int val = (int)Math.Round(dblValue);
            return Math.Max(_timeTrackBar.Minimum, Math.Min(val, _timeTrackBar.Maximum));
        }

        public void Dispose()
        {
            if (_timeTrackBar != null)
            {
                _timeTrackBar.Scroll -= OnTrackBarScroll;
                _timeTrackBar.MouseDown -= TimeTrackBar_MouseDown;
                _timeTrackBar.MouseUp -= TimeTrackBar_MouseUp;
                _timeTrackBar.MouseMove -= TimeTrackBar_MouseMove;
                _timeTrackBar.MouseLeave -= TimeTrackBar_MouseLeave;
            }
            var parentForm = _timeTrackBar?.FindForm();
            if (parentForm != null)
            {
                parentForm.Deactivate -= OnParentFormDeactivate;
                parentForm.Move -= OnParentFormMove;
            }

            _previewManager?.Dispose();
        }
    }
}