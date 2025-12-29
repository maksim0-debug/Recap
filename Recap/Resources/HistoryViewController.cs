using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class HistoryViewController : IDisposable
    {
        public event Action<Image> FrameChanged;

        private readonly PictureBox _mainPictureBox;
        private readonly VideoView _mainVideoView;

        private string _activeVideoPath = null;
        private double _videoFps = 1.0;

        private CancellationTokenSource _imageLoadCts;

        private readonly Label _lblFormatBadge;
        private readonly ToolTip _badgeToolTip;
        private readonly FrameRepository _frameRepository;
        private readonly AppSettings _settings;
        private readonly OcrDatabase _ocrDb;

        private readonly AppFilterController _appFilterController;
        private readonly TimelineController _timelineController;

        private bool _isLoading = false;
        private bool _isInitialLoad = true;
        private bool _isGlobalMode = false;

        private DateTime _currentStartDate = DateTime.Today;
        private DateTime _currentEndDate = DateTime.Today;

        private List<MiniFrame> _allLoadedFrames = new List<MiniFrame>();
        private List<MiniFrame> _filteredFrames = new List<MiniFrame>();

        private string _selectedAppFilter = null;
        private readonly Dictionary<DateTime, List<MiniFrame>> _dayCache = new Dictionary<DateTime, List<MiniFrame>>();
        private readonly object _cacheLock = new object();
        private Dictionary<int, string> _appMap = new Dictionary<int, string>();

        private System.Windows.Forms.Timer _uiTimer;
        private TextBox _txtOcrSearch;

        private int _wantedFrameIndex = -1;
        private int _currentFrameIndex = -1;

        private long _pendingVideoTimeMs = -1;
        private DateTime _lastInteractionTime = DateTime.MinValue;
        private bool _isScrubbing = false;

        private Bitmap _iconVideo;
        private Bitmap _iconImage;
        
        private DateTime _lastNotifiedDate = DateTime.MinValue;
        public event Action<DateTime> CurrentDateChanged;

        public HistoryViewController(
            PictureBox mainPictureBox,
            VideoView mainVideoView,
            TrackBar timeTrackBar,
            DarkListBox lstAppFilter,
            TextBox txtAppSearch,
            Label lblTime,
            Label lblInfo,
            CheckBox chkAutoScroll,
            Label lblFormatBadge,
            FrameRepository frameRepository,
            IconManager iconManager,
            AppSettings settings,
            OcrDatabase ocrDb,
            TextBox txtOcrSearch)
        {
            _mainPictureBox = mainPictureBox;
            _mainVideoView = mainVideoView;
            _lblFormatBadge = lblFormatBadge;
            _frameRepository = frameRepository;
            _settings = settings;
            _ocrDb = ocrDb;
            _txtOcrSearch = txtOcrSearch;

            _badgeToolTip = new ToolTip();
            GenerateIcons();

            _uiTimer = new System.Windows.Forms.Timer { Interval = AdvancedSettings.Instance.UiUpdateIntervalMs };
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            _appFilterController = new AppFilterController(lstAppFilter, txtAppSearch, iconManager);
            lstAppFilter.ShowFrameCount = settings.ShowFrameCount;   
            _timelineController = new TimelineController(timeTrackBar, lblTime, lblInfo, chkAutoScroll, null, frameRepository, iconManager);

            _appFilterController.FilterChanged += OnAppFilterChanged;
            _timelineController.TimeChanged += OnTimeChanged;

            txtAppSearch.KeyDown += OnSearchKeyDown;
            
            if (_txtOcrSearch != null)
            {
                _txtOcrSearch.KeyDown += OnOcrSearchKeyDown;
                _txtOcrSearch.TextChanged += OnOcrSearchTextChanged;
            }
        }

        private void OnOcrSearchTextChanged(object sender, EventArgs e)
        {
            ApplyAppFilterAndDisplay(false);
        }

        private void OnOcrSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyAppFilterAndDisplay(false);
                e.SuppressKeyPress = true;   
            }
        }


        private async void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && _settings.GlobalSearch)
            {
                e.SuppressKeyPress = true;
                var txt = (sender as TextBox).Text;
                await ReloadDataAsync(forceGlobalSearchText: txt);
            }
        }

        private void OnUiTimerTick(object sender, EventArgs e)
        {
            if (_wantedFrameIndex != -1 && _wantedFrameIndex != _currentFrameIndex)
            {
                if (_filteredFrames != null && _wantedFrameIndex >= 0 && _wantedFrameIndex < _filteredFrames.Count)
                {
                    _currentFrameIndex = _wantedFrameIndex;
                    var miniFrame = _filteredFrames[_currentFrameIndex];

                    _timelineController.UpdateTimeLabel(miniFrame.GetTime(), _isGlobalMode);

                    DateTime frameDate = miniFrame.GetTime().Date;
                    if (_lastNotifiedDate != frameDate)
                    {
                        _lastNotifiedDate = frameDate;
                        CurrentDateChanged?.Invoke(frameDate);
                    }

                    if (_isGlobalMode)
                    {
                        if (_currentStartDate.Date != frameDate)
                        {
                            _currentStartDate = frameDate;
                            _currentEndDate = frameDate;
                        }
                    }

                    
                    var frame = _frameRepository.GetFrameIndex(miniFrame.TimestampTicks);
                    
                    if (frame.TimestampTicks == 0) return;

                    bool isVideoFrame = frame.IsVideoFrame;

                    if (isVideoFrame)
                    {
                        SwitchToVideoMode(frame);
                    }
                    else
                    {
                        SwitchToImageMode(frame);
                    }
                }
            }

            ProcessVideoLogic();
        }

        private void SwitchToVideoMode(FrameIndex frame)
        {
            if (!_mainVideoView.Visible)
            {
                _mainVideoView.Visible = true;
                _mainPictureBox.Visible = false;
                UpdateFormatBadge(true);
            }

            DateTime date = frame.GetTime().Date;
            string requiredPath = _frameRepository.GetVideoPathForDate(date);

            var mainForm = _mainPictureBox.FindForm() as MainForm;
            if (mainForm?.MainMediaPlayer == null || string.IsNullOrEmpty(requiredPath)) return;

            if (_activeVideoPath != requiredPath)
            {
                _activeVideoPath = requiredPath;

                Task.Run(async () => {
                    try
                    {
                        var analysis = await FFMpegCore.FFProbe.AnalyseAsync(requiredPath);
                        _videoFps = analysis.PrimaryVideoStream?.FrameRate ?? 1.0;
                    }
                    catch { _videoFps = 1.0; }
                });

                mainForm.MainMediaPlayer.Media = new Media(mainForm.LibVLC, requiredPath, FromType.FromPath);
                mainForm.MainMediaPlayer.AspectRatio = null;       
                _mainVideoView.MediaPlayer = mainForm.MainMediaPlayer;
                mainForm.MainMediaPlayer.Play();
            }

            if (_videoFps <= 0) _videoFps = 1.0;
            double frameNumber = (double)frame.DataOffset;
            long targetTimeMs = (long)((frameNumber / _videoFps) * 1000.0);

            _pendingVideoTimeMs = targetTimeMs;
        }

        private void SwitchToImageMode(FrameIndex frame)
        {
            if (!_mainPictureBox.Visible)
            {
                _mainPictureBox.Visible = true;
                _mainVideoView.Visible = false;
                UpdateFormatBadge(false);

                var mainForm = _mainPictureBox.FindForm() as MainForm;
                if (mainForm?.MainMediaPlayer != null && mainForm.MainMediaPlayer.IsPlaying)
                {
                    mainForm.MainMediaPlayer.Stop();
                }
            }

            LoadImageAsync(frame);
        }

        private void ProcessVideoLogic()
        {
            if (!_mainVideoView.Visible) return;

            var mainForm = _mainPictureBox.FindForm() as MainForm;
            var player = mainForm?.MainMediaPlayer;
            if (player == null || player.NativeReference == IntPtr.Zero) return;

            try
            {
                if (_pendingVideoTimeMs >= 0)
                {
                    _isScrubbing = true;
                    _lastInteractionTime = DateTime.Now;

                    if (!player.IsPlaying && player.State != VLCState.Buffering)
                    {
                        player.Mute = true;
                        player.Play();
                    }
                    if (player.IsSeekable)
                    {
                        if (Math.Abs(player.Time - _pendingVideoTimeMs) > 100)
                            player.Time = _pendingVideoTimeMs;

                        _pendingVideoTimeMs = -1;
                    }
                }
                else if (_isScrubbing)
                {
                    if ((DateTime.Now - _lastInteractionTime).TotalMilliseconds > 300)
                    {
                        if (player.IsPlaying) player.Pause();
                        _isScrubbing = false;
                    }
                }
            }
            catch { _pendingVideoTimeMs = -1; }
        }

        private void LoadImageAsync(FrameIndex frame)
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts = new CancellationTokenSource();
            var token = _imageLoadCts.Token;

            string searchText = _txtOcrSearch.Text.Trim();

            Task.Run(() =>
            {
                if (token.IsCancellationRequested) return;
                byte[] imgBytes = _frameRepository.GetFrameData(frame);
                if (token.IsCancellationRequested) return;

                if (imgBytes != null && imgBytes.Length > 0)
                {
                    try
                    {
                        using (var ms = new MemoryStream(imgBytes))
                        {
                            var bmp = new Bitmap(ms);
                            var finalImage = new Bitmap(bmp);

                            if (!string.IsNullOrEmpty(searchText) && _settings.EnableTextHighlighting)
                            {
                                try
                                {
                                    var compressedData = _ocrDb.GetTextData(frame.TimestampTicks);
                                    if (compressedData != null)
                                    {
                                        var words = BinaryCoordinatesPacker.Unpack(compressedData);

                                        using (var g = Graphics.FromImage(finalImage))
                                        {
                                            foreach (var word in words)
                                            {
                                                if (word.T.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    var rect = new Rectangle(
                                                        (int)(word.X * finalImage.Width),
                                                        (int)(word.Y * finalImage.Height),
                                                        (int)(word.W * finalImage.Width),
                                                        (int)(word.H * finalImage.Height)
                                                    );
                                                    
                                                    using (var brush = new SolidBrush(Color.FromArgb(80, 255, 255, 0)))
                                                    {
                                                        g.FillRectangle(brush, rect);
                                                    }
                                                    using (var pen = new Pen(Color.Red, 2))
                                                    {
                                                        g.DrawRectangle(pen, rect);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch {      }
                            }

                            if (token.IsCancellationRequested) { finalImage.Dispose(); return; }

                            _mainPictureBox.BeginInvoke((Action)(() =>
                            {
                                if (!token.IsCancellationRequested && !_mainPictureBox.IsDisposed)
                                {
                                    var old = _mainPictureBox.Image;
                                    _mainPictureBox.Image = finalImage;
                                    old?.Dispose();
                                    FrameChanged?.Invoke(finalImage);
                                }
                                else finalImage.Dispose();
                            }));
                        }
                    }
                    catch { }
                }
            }, token);
        }

        public void NavigateFrames(int offset)
        {
            if (offset == 0)
            {
                _pendingVideoTimeMs = -1;
                _isScrubbing = false;
                if (_currentFrameIndex >= 0 && _currentFrameIndex < _filteredFrames.Count)
                    _wantedFrameIndex = _currentFrameIndex;
            }
            else
            {
                _timelineController.Navigate(offset);
            }
        }

        public async Task LoadFramesForDate(DateTime date) => await ReloadDataAsync(date, date);
        public async Task LoadFramesForRange(DateTime start, DateTime end) => await ReloadDataAsync(start, end);

        private async Task ReloadDataAsync(DateTime? startDate = null, DateTime? endDate = null, string forceGlobalSearchText = "")
        {
            if (_isLoading) return;
            _isLoading = true;

            _allLoadedFrames = null;
            _filteredFrames = null;
            _timelineController.SetFrames(new List<MiniFrame>(), false, true);

            _isInitialLoad = true;
            _pendingVideoTimeMs = -1;
            _isScrubbing = false;
            _wantedFrameIndex = -1;
            _currentFrameIndex = -1;
            _selectedAppFilter = null;
            _activeVideoPath = null;

            var parentForm = _mainPictureBox.FindForm();
            if (parentForm != null) parentForm.Cursor = Cursors.WaitCursor;

            _isGlobalMode = _settings.GlobalSearch;

            _mainPictureBox.Image?.Dispose();
            _mainPictureBox.Image = null;

            await Task.Run(() =>
            {
                _appMap = _frameRepository.GetAppMap();

                if (_isGlobalMode)
                {
                    
                    var fullFrames = _frameRepository.GlobalSearch(forceGlobalSearchText);
                    _allLoadedFrames = ConvertToMiniFrames(fullFrames);
                }
                else
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    _allLoadedFrames = new List<MiniFrame>();

                    if (startDate.HasValue) _currentStartDate = startDate.Value.Date;
                    if (endDate.HasValue) _currentEndDate = endDate.Value.Date;
                    else if (startDate.HasValue) _currentEndDate = startDate.Value.Date;

                    DateTime start = _currentStartDate;
                    DateTime end = _currentEndDate;

                    for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                    {
                        List<MiniFrame> frames = null;

                        lock (_cacheLock)
                        {
                            if (_dayCache.ContainsKey(date))
                            {
                                frames = _dayCache[date];
                            }
                        }

                        if (frames == null)
                        {
                            frames = _frameRepository.LoadMiniFramesForDateFast(date);
                            lock (_cacheLock)
                            {
                                if (!_dayCache.ContainsKey(date))
                                {
                                    _dayCache[date] = frames;
                                }
                                else
                                {
                                    frames = _dayCache[date];
                                }
                            }
                        }

                        if (frames != null)
                        {
                            _allLoadedFrames.AddRange(frames);
                        }
                    }
                }
            });

            await _appFilterController.SetDataAsync(_allLoadedFrames, _appMap);
            ApplyAppFilterAndDisplay(isLiveUpdate: false);

            if (parentForm != null) parentForm.Cursor = Cursors.Default;
            _isLoading = false;
        }

        private List<MiniFrame> ConvertToMiniFrames(List<FrameIndex> fullFrames)
        {
            var mini = new List<MiniFrame>(fullFrames.Count);
            var nameToId = _appMap.ToDictionary(x => x.Value, x => x.Key);
            
            foreach (var f in fullFrames)
            {
                int id = -1;
                if (f.AppName != null && nameToId.TryGetValue(f.AppName, out int val)) id = val;
                mini.Add(new MiniFrame { TimestampTicks = f.TimestampTicks, AppId = id, IntervalMs = f.IntervalMs });
            }
            return mini;
        }

        private void ApplyAppFilterAndDisplay(bool isLiveUpdate)
        {
            long currentTimestamp = -1;
            if (!isLiveUpdate && _filteredFrames != null && _currentFrameIndex >= 0 && _currentFrameIndex < _filteredFrames.Count)
            {
                currentTimestamp = _filteredFrames[_currentFrameIndex].TimestampTicks;
            }

            string filter = _selectedAppFilter;
            string ocrText = _txtOcrSearch?.Text?.Trim();

            List<MiniFrame> appFiltered;

            if (string.IsNullOrEmpty(filter))
            {
                appFiltered = _allLoadedFrames;
            }
            else
            {
                string filterPipe = filter + "|";
                string dbStyleKey = null;
                string prefix = null;
                bool isVideo = filter.Contains("|YouTube|");
                bool isFolder = filter.EndsWith("|YouTube");

                if (isVideo) dbStyleKey = filter.Replace("|YouTube|", "|");
                if (isFolder) prefix = filter.Replace("|YouTube", "|youtube.com");

                appFiltered = _allLoadedFrames.AsParallel().AsOrdered().Where(f => 
                {
                    string appName = "";
                    if (_appMap.TryGetValue(f.AppId, out string name)) appName = name;
                    
                    if (appName == filter) return true;
                    if (appName.StartsWith(filterPipe)) return true;

                    if (isVideo && appName == dbStyleKey) return true;

                    if (isFolder && appName.StartsWith(prefix)) return true;

                    return false;
                }).ToList();
            }

            if (!string.IsNullOrEmpty(ocrText) && _ocrDb != null)
            {
                try
                {
                    string dbFilter = null;
                    if (!string.IsNullOrEmpty(filter) && !filter.Contains("|YouTube"))
                    {
                        dbFilter = filter;
                    }

                    var matchingFrames = _ocrDb.Search(ocrText, dbFilter);
                    
                    if (matchingFrames != null && matchingFrames.Count > 0)
                    {
                        var matchingTicks = new HashSet<long>(matchingFrames.Select(f => f.TimestampTicks));
                        _filteredFrames = appFiltered.Where(f => matchingTicks.Contains(f.TimestampTicks)).ToList();
                        
                        DebugLogger.Log($"OCR Search: '{ocrText}' found {_filteredFrames.Count} matches");
                    }
                    else
                    {
                        _filteredFrames = new List<MiniFrame>();
                        DebugLogger.Log($"OCR Search: '{ocrText}' - no matches found");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("HistoryViewController.OcrSearch", ex);
                    _filteredFrames = appFiltered;    
                }
            }
            else
            {
                _filteredFrames = appFiltered;
            }

            _timelineController.SetFrames(_filteredFrames, isLiveUpdate, _isInitialLoad);
            _timelineController.UpdateInfoLabel();

            if (_filteredFrames.Count > 0)
            {
                if (!isLiveUpdate)
                {
                    int targetIndex = _filteredFrames.Count - 1;

                    if (currentTimestamp != -1)
                    {
                        int foundIndex = _filteredFrames.FindIndex(f => f.TimestampTicks >= currentTimestamp);
                        if (foundIndex != -1) targetIndex = foundIndex;
                    }

                    var targetFrame = _frameRepository.GetFrameIndex(_filteredFrames[targetIndex].TimestampTicks);
                    if (targetIndex > 0 && targetFrame.IsVideoFrame)
                    {
                        targetIndex--;
                    }

                    _wantedFrameIndex = targetIndex;
                    _currentFrameIndex = -1;
                    _timelineController.CurrentIndex = targetIndex;
                }
                else
                {
                    if (_currentFrameIndex == -1 && _filteredFrames.Count == 1)
                    {
                        _wantedFrameIndex = 0;
                    }
                }
            }
            else
            {
                _mainPictureBox.Image?.Dispose();
                _mainPictureBox.Image = null;
                _mainVideoView.Visible = false;
            }

            _isInitialLoad = false;
        }

        private bool MatchesAppFilter(MiniFrame f, string filter)
        {
            string appName = "";
            if (_appMap.TryGetValue(f.AppId, out string name)) appName = name;

            if (appName == filter) return true;
            if (appName.StartsWith(filter + "|")) return true;

            if (filter.Contains("|YouTube|"))
            {
                string dbStyleKey = filter.Replace("|YouTube|", "|");
                if (appName == dbStyleKey) return true;
            }

            if (filter.EndsWith("|YouTube"))
            {
                string prefix = filter.Replace("|YouTube", "|youtube.com");
                if (appName.StartsWith(prefix)) return true;
            }

            return false;
        }

        private void OnAppFilterChanged(string newFilter)
        {
            if (_selectedAppFilter == newFilter) return;

            _selectedAppFilter = newFilter;
            ApplyAppFilterAndDisplay(isLiveUpdate: false);
        }

        private void OnTimeChanged(int newIndex) => _wantedFrameIndex = newIndex;

        public void HandleNewFrame(FrameIndex newFrame)
        {
            if (_mainPictureBox.InvokeRequired)
            {
                _mainPictureBox.BeginInvoke(new Action(() => HandleNewFrame(newFrame)));
                return;
            }

            int appId = -1;
            foreach(var kvp in _appMap) { if (kvp.Value == newFrame.AppName) { appId = kvp.Key; break; } }
            
            if (appId == -1)
            {
                _appMap = _frameRepository.GetAppMap();
                foreach(var kvp in _appMap) { if (kvp.Value == newFrame.AppName) { appId = kvp.Key; break; } }
            }

            var miniFrame = new MiniFrame { TimestampTicks = newFrame.TimestampTicks, AppId = appId, IntervalMs = newFrame.IntervalMs };

            lock (_cacheLock)
            {
                if (_dayCache.ContainsKey(DateTime.Today))
                {
                    if (!_dayCache[DateTime.Today].Any(x => x.TimestampTicks == miniFrame.TimestampTicks))
                    {
                        _dayCache[DateTime.Today].Add(miniFrame);
                    }
                }
            }

            if ((_isGlobalMode || (newFrame.GetTime().Date >= _currentStartDate && newFrame.GetTime().Date <= _currentEndDate)) && _allLoadedFrames != null)
            {
                if (!_allLoadedFrames.Any(x => x.TimestampTicks == miniFrame.TimestampTicks))
                {
                    _allLoadedFrames.Add(miniFrame);

                    _appFilterController.AddFrame(miniFrame);

                    if (string.IsNullOrEmpty(_txtOcrSearch?.Text?.Trim()))
                    {
                        ProcessNewFrame(miniFrame);
                    }
                    else
                    {
                        ApplyAppFilterAndDisplay(isLiveUpdate: true);
                    }
                }
            }
        }

        private void ProcessNewFrame(MiniFrame newFrame)
        {
            string filter = _selectedAppFilter;
            
            bool matches = string.IsNullOrEmpty(filter) || MatchesAppFilter(newFrame, filter);

            if (matches)
            {
                if (_filteredFrames != _allLoadedFrames)
                {
                    _filteredFrames.Add(newFrame);
                }

                _timelineController.SetFrames(_filteredFrames, true, false);
                _timelineController.UpdateInfoLabel();
            }
        }



        private void UpdateFormatBadge(bool isVideo)
        {
            if (_lblFormatBadge == null) return;
            _lblFormatBadge.Visible = true;
            _lblFormatBadge.Text = "";
            _lblFormatBadge.AutoSize = false;
            _lblFormatBadge.Size = new Size(24, 24);
            _lblFormatBadge.BackColor = Color.Transparent;
            if (isVideo)
            {
                _lblFormatBadge.Image = _iconVideo;
                _badgeToolTip.SetToolTip(_lblFormatBadge, "Видео");
            }
            else
            {
                _lblFormatBadge.Image = _iconImage;
                _badgeToolTip.SetToolTip(_lblFormatBadge, "Изображение");
            }
        }

        private void GenerateIcons()
        {
            _iconVideo = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(_iconVideo))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(220, 100, 0))) g.FillEllipse(brush, 1, 1, 22, 22);
                g.FillPolygon(Brushes.White, new Point[] { new Point(9, 7), new Point(9, 17), new Point(17, 12) });
            }
            _iconImage = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(_iconImage))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bgBrush = new SolidBrush(Color.FromArgb(0, 120, 215))) g.FillRectangle(bgBrush, 0, 0, 24, 24);
                using (var whiteBrush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(whiteBrush, 15, 5, 4, 4);
                    g.FillPolygon(whiteBrush, new Point[] { new Point(2, 20), new Point(9, 9), new Point(16, 20) });
                    g.FillPolygon(whiteBrush, new Point[] { new Point(13, 20), new Point(18, 13), new Point(23, 20) });
                }
            }
        }

        public void GetState(out List<MiniFrame> frames, out string filter, out int index)
        {
            frames = _allLoadedFrames;
            filter = _selectedAppFilter;
            index = _currentFrameIndex;
        }

        public void RestoreState(List<MiniFrame> frames, string filter, int index)
        {
            _allLoadedFrames = frames ?? new List<MiniFrame>();
            _selectedAppFilter = filter;
            _isInitialLoad = true;

            if (_appMap.Count == 0) _appMap = _frameRepository.GetAppMap();

            _appFilterController.SetDataAsync(_allLoadedFrames, _appMap);
            ApplyAppFilterAndDisplay(isLiveUpdate: false);

            if (index >= 0 && index < _filteredFrames.Count)
            {
                _wantedFrameIndex = index;
                _currentFrameIndex = -1;
                _timelineController.CurrentIndex = index;
            }
        }

        public async Task ActivateAsync() { }
        public void UpdateSettings(AppSettings newSettings, FrameRepository newFrameRepository) { }

        public void Dispose()
        {
            _uiTimer?.Stop(); _uiTimer?.Dispose();
            _imageLoadCts?.Cancel(); _imageLoadCts?.Dispose();

            var txtAppSearch = _appFilterController.GetType().GetField("_txtAppSearch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_appFilterController) as TextBox;
            if (txtAppSearch != null) txtAppSearch.KeyDown -= OnSearchKeyDown;

            if (_appFilterController != null) { _appFilterController.FilterChanged -= OnAppFilterChanged; _appFilterController.Dispose(); }
            if (_timelineController != null) { _timelineController.TimeChanged -= OnTimeChanged; _timelineController.Dispose(); }

            if (_lblFormatBadge != null) _lblFormatBadge.Image = null;

            _iconVideo?.Dispose(); _iconImage?.Dispose();
        }
    }
}