using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

        private CancellationTokenSource _imageLoadCts;

        private readonly Label _lblFormatBadge;
        private readonly ToolTip _badgeToolTip;
        private readonly FrameRepository _frameRepository;
        private readonly AppSettings _settings;
        private readonly OcrDatabase _ocrDb;

        private readonly AppFilterController _appFilterController;
        private readonly TimelineController _timelineController;
        
        private readonly TimelineDataManager _dataManager;
        private readonly MediaPlayerController _mediaController;

        private bool _isLoading = false;
        private List<FrameIndex> _pendingFrames = new List<FrameIndex>();
        private bool _isInitialLoad = true;
        private bool _isGlobalMode = false;

        private DateTime _currentStartDate = DateTime.Today;
        private DateTime _currentEndDate = DateTime.Today;

        private List<string> _selectedAppFilter = null;
        
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
        public event Action<string, bool> OcrBlacklistToggled;

        private ContextMenuStrip _contextMenu;
        private OcrTextForm _ocrTextForm;
        private ToolStripMenuItem _copyImageItem;
        private ToolStripMenuItem _viewTextItem;
        private DarkListBox _lstNotes;

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
            TextBox txtOcrSearch,
            LibVLC libVLC,
            MediaPlayer mediaPlayer)
        {
            _mainPictureBox = mainPictureBox;
            _mainVideoView = mainVideoView;
            _lblFormatBadge = lblFormatBadge;
            _frameRepository = frameRepository;
            _settings = settings;
            _ocrDb = ocrDb;
            _txtOcrSearch = txtOcrSearch;

            _dataManager = new TimelineDataManager(frameRepository, ocrDb, settings);
            _mediaController = new MediaPlayerController(libVLC, mediaPlayer);

            InitializeContextMenu();

            _badgeToolTip = new ToolTip();
            GenerateIcons();

            _uiTimer = new System.Windows.Forms.Timer { Interval = AdvancedSettings.Instance.UiUpdateIntervalMs };
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            _appFilterController = new AppFilterController(lstAppFilter, txtAppSearch, iconManager, _ocrDb, _frameRepository, settings);
            lstAppFilter.ShowFrameCount = settings.ShowFrameCount;   
            _timelineController = new TimelineController(timeTrackBar, lblTime, lblInfo, chkAutoScroll, null, frameRepository, iconManager);

            _appFilterController.FilterChanged += OnAppFilterChanged;
            _appFilterController.AppHidden += OnAppHidden;
            _appFilterController.OcrBlacklistToggled += (appName, add) => OcrBlacklistToggled?.Invoke(appName, add);
            _frameRepository.DataInvalidated += OnDataInvalidated;
            _timelineController.TimeChanged += OnTimeChanged;

            txtAppSearch.KeyDown += OnSearchKeyDown;
            
            if (_txtOcrSearch != null)
            {
                _txtOcrSearch.KeyDown += OnOcrSearchKeyDown;
            }
        }

        public void SetNotesListBox(DarkListBox lstNotes)
        {
            _lstNotes = lstNotes;
        }

        public long GetCurrentTimestamp()
        {
            if (_dataManager.FilteredFrames != null && _currentFrameIndex >= 0 && _currentFrameIndex < _dataManager.FilteredFrames.Count)
            {
                return _dataManager.FilteredFrames[_currentFrameIndex].TimestampTicks;
            }
            return -1;
        }

        public void JumpToTimestamp(long timestamp)
        {
            if (_dataManager.FilteredFrames == null) return;
            
            int index = _dataManager.FilteredFrames.FindIndex(f => f.TimestampTicks == timestamp);
            if (index != -1)
            {
                _wantedFrameIndex = index;
                _currentFrameIndex = -1;   
                _timelineController.CurrentIndex = index;
            }
        }

        public void ReloadNotes()
        {
            if (_lstNotes == null || _ocrDb == null) return;

            List<OcrDatabase.NoteItem> notes;
            if (_isGlobalMode)
            {
                notes = _ocrDb.SearchNotes(""); 
            }
            else
            {
                notes = _ocrDb.GetNotesForPeriod(_currentStartDate, _currentEndDate.AddDays(1));
            }

            _lstNotes.Items.Clear();
            foreach (var note in notes)
            {
                string displayName = note.Title;
                if (_isGlobalMode)
                {
                    displayName = $"{new DateTime(note.Timestamp):dd.MM.yyyy HH:mm} {note.Title}";
                }
                else
                {
                    displayName = $"{new DateTime(note.Timestamp):HH:mm:ss} {note.Title}";
                }

                _lstNotes.Items.Add(new FilterItem 
                { 
                    RawName = note.Title, 
                    DisplayName = displayName, 
                    DurationMs = 0, 
                    FrameCount = 0,
                    Level = 0,
                    HasChildren = false,
                    IsNote = true,
                    NoteTimestamp = note.Timestamp,
                    NoteDescription = note.Description
                });
            }
        }

        private void InitializeContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            _copyImageItem = new ToolStripMenuItem(Localization.Get("copyImage"));
            _copyImageItem.Click += (s, e) => CopyImageToClipboard();
            
            _viewTextItem = new ToolStripMenuItem(Localization.Get("viewText"));
            _viewTextItem.Click += (s, e) => ShowOcrText();

            _contextMenu.Items.Add(_copyImageItem);
            _contextMenu.Items.Add(_viewTextItem);

            _mainPictureBox.ContextMenuStrip = _contextMenu;
        }

        public void UpdateLocalization()
        {
            if (_copyImageItem != null) _copyImageItem.Text = Localization.Get("copyImage");
            if (_viewTextItem != null) _viewTextItem.Text = Localization.Get("viewText");
            if (_ocrTextForm != null && !_ocrTextForm.IsDisposed) _ocrTextForm.UpdateLocalization();
        }

        private void CopyImageToClipboard()
        {
            if (_mainPictureBox.Image != null)
            {
                Clipboard.SetImage(_mainPictureBox.Image);
            }
        }

        private void ShowOcrText()
        {
            if (_dataManager.FilteredFrames == null || _currentFrameIndex < 0 || _currentFrameIndex >= _dataManager.FilteredFrames.Count) return;

            var miniFrame = _dataManager.FilteredFrames[_currentFrameIndex];
            string text = GetOcrTextForFrame(miniFrame.TimestampTicks);
            string info = GetFrameInfo(miniFrame);

            if (_ocrTextForm == null || _ocrTextForm.IsDisposed)
            {
                _ocrTextForm = new OcrTextForm(text, info, this);
                _ocrTextForm.Show();
            }
            else
            {
                _ocrTextForm.UpdateText(text, info);
                _ocrTextForm.BringToFront();
            }
        }

        private string GetOcrTextForFrame(long timestamp)
        {
            try
            {
                string text = _ocrDb.GetOcrText(timestamp);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }

                var compressedData = _ocrDb.GetTextData(timestamp);
                if (compressedData != null)
                {
                    var words = BinaryCoordinatesPacker.Unpack(compressedData);
                    var sb = new StringBuilder();
                    foreach (var word in words)
                    {
                        sb.Append(word.T).Append(" ");
                    }
                    return sb.ToString();
                }
            }
            catch { }
            return "";
        }

        private string GetFrameInfo(MiniFrame frame)
        {
            var time = frame.GetTime();
            string appName = "";
            if (_dataManager.AppMap.TryGetValue(frame.AppId, out string name)) appName = name;
            return $"{time:yyyy-MM-dd HH:mm:ss} - {appName}";
        }

        public void ApplyFilter()
        {
            ApplyAppFilterAndDisplay(false);
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
                if (_dataManager.FilteredFrames != null && _wantedFrameIndex >= 0 && _wantedFrameIndex < _dataManager.FilteredFrames.Count)
                {
                    _currentFrameIndex = _wantedFrameIndex;
                    var miniFrame = _dataManager.FilteredFrames[_currentFrameIndex];

                    _timelineController.UpdateTimeLabel(miniFrame.GetTime(), _isGlobalMode);

                    if (_ocrTextForm != null && !_ocrTextForm.IsDisposed)
                    {
                        string text = GetOcrTextForFrame(miniFrame.TimestampTicks);
                        string info = GetFrameInfo(miniFrame);
                        _ocrTextForm.BeginInvoke((Action)(() => _ocrTextForm.UpdateText(text, info)));
                    }

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
                        SwitchtoImageMode(frame);
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

            if (_mediaController.Player == null || string.IsNullOrEmpty(requiredPath)) return;
            
            _mediaController.LoadVideo(requiredPath);

            double fps = _mediaController.VideoFps;
            if (fps <= 0) fps = 1.0;
            
            double frameNumber = (double)frame.DataOffset;
            long targetTimeMs = (long)((frameNumber / fps) * 1000.0);

            _pendingVideoTimeMs = targetTimeMs;
        }

        private void SwitchtoImageMode(FrameIndex frame)
        {
            if (!_mainPictureBox.Visible)
            {
                _mainPictureBox.Visible = true;
                _mainVideoView.Visible = false;
                UpdateFormatBadge(false);
                
                if (_mediaController.IsPlaying)
                {
                    _mediaController.Stop();
                }
            }

            LoadImageAsync(frame);
        }

        private void ProcessVideoLogic()
        {
            if (!_mainVideoView.Visible) return;

            try
            {
                if (_pendingVideoTimeMs >= 0)
                {
                    _isScrubbing = true;
                    _lastInteractionTime = DateTime.Now;

                    _mediaController.EnsurePlaying();
                    
                    if (_mediaController.IsSeekable)
                    {
                        if (Math.Abs(_mediaController.Time - _pendingVideoTimeMs) > 100)
                            _mediaController.Time = _pendingVideoTimeMs;

                        _pendingVideoTimeMs = -1;
                    }
                }
                else if (_isScrubbing)
                {
                    if ((DateTime.Now - _lastInteractionTime).TotalMilliseconds > 300)
                    {
                        if (_mediaController.IsPlaying) _mediaController.Pause();
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
                if (_currentFrameIndex >= 0 && _currentFrameIndex < _dataManager.FilteredFrames.Count)
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
            _pendingFrames.Clear();
            
            _timelineController.SetFrames(new List<MiniFrame>(), false, true);

            _isInitialLoad = true;
            _pendingVideoTimeMs = -1;
            _isScrubbing = false;
            _wantedFrameIndex = -1;
            _currentFrameIndex = -1;
            _selectedAppFilter = null;
            
            var parentForm = _mainPictureBox.FindForm();
            if (parentForm != null) parentForm.Cursor = Cursors.WaitCursor;

            _isGlobalMode = _settings.GlobalSearch;

            _mainPictureBox.Image?.Dispose();
            _mainPictureBox.Image = null;

            if (startDate.HasValue) _currentStartDate = startDate.Value.Date;
            if (endDate.HasValue) _currentEndDate = endDate.Value.Date;
            else if (startDate.HasValue) _currentEndDate = startDate.Value.Date;

            await _dataManager.LoadFramesAsync(startDate, endDate, forceGlobalSearchText);

            await _appFilterController.SetDataAsync(_dataManager.GetAllLoadedFramesCopy(), _dataManager.AppMap);
            ApplyAppFilterAndDisplay(isLiveUpdate: false);

            if (_lstNotes != null && _lstNotes.Visible)
            {
                ReloadNotes();
            }

            if (parentForm != null) parentForm.Cursor = Cursors.Default;
            _isLoading = false;

            if (_pendingFrames.Count > 0)
            {
                var framesToProcess = _pendingFrames.ToList();
                _pendingFrames.Clear();
                foreach (var pf in framesToProcess)
                {
                    HandleNewFrame(pf);
                }
            }
        }

        private async void ApplyAppFilterAndDisplay(bool isLiveUpdate)
        {
            long currentTimestamp = -1;
            if (!isLiveUpdate && _dataManager.FilteredFrames != null && _currentFrameIndex >= 0 && _currentFrameIndex < _dataManager.FilteredFrames.Count)
            {
                currentTimestamp = _dataManager.FilteredFrames[_currentFrameIndex].TimestampTicks;
            }

            var filter = _selectedAppFilter;
            string ocrText = _txtOcrSearch?.Text?.Trim();

            var parentForm = _mainPictureBox.FindForm();
            if (parentForm != null) parentForm.Cursor = Cursors.WaitCursor;
            _appFilterController.SetEnabled(false);

            await Task.Run(() => 
            {
                _dataManager.ApplyFilter(filter, ocrText);
            });

            if (parentForm != null) parentForm.Cursor = Cursors.Default;
            _appFilterController.SetEnabled(true);

            var filteredFrames = _dataManager.FilteredFrames;

            _timelineController.SetFrames(filteredFrames, isLiveUpdate, _isInitialLoad);
            _timelineController.UpdateInfoLabel();

            if (filteredFrames.Count > 0)
            {
                if (!isLiveUpdate)
                {
                    int targetIndex = filteredFrames.Count - 1;

                    if (currentTimestamp != -1)
                    {
                        int foundIndex = filteredFrames.FindIndex(f => f.TimestampTicks >= currentTimestamp);
                        if (foundIndex != -1) targetIndex = foundIndex;
                    }
                    else if (_isInitialLoad && filteredFrames.Count > 0)
                    {
                        targetIndex = filteredFrames.Count - 1;
                    }

                    var targetFrame = _frameRepository.GetFrameIndex(filteredFrames[targetIndex].TimestampTicks);
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
                     if (_currentFrameIndex == -1 && filteredFrames.Count > 0)
                    {
                        int targetIndex = filteredFrames.Count - 1;
                        _wantedFrameIndex = targetIndex;
                        _timelineController.CurrentIndex = targetIndex;
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

        private void OnAppFilterChanged(List<string> newFilter)
        {
            _selectedAppFilter = newFilter;
            ApplyAppFilterAndDisplay(isLiveUpdate: false);
        }

        private void OnAppHidden(object sender, EventArgs e)
        {
            OnDataInvalidated(sender, e);
        }

        private void OnDataInvalidated(object sender, EventArgs e)
        {
            if (_mainPictureBox.InvokeRequired)
            {
                _mainPictureBox.Invoke(new Action(() => OnDataInvalidated(sender, e)));
                return;
            }

            _dataManager.ClearCache();
            _ = ReloadDataAsync(_currentStartDate, _currentEndDate);
        }

        private void OnTimeChanged(int newIndex) => _wantedFrameIndex = newIndex;

        public void HandleNewFrame(FrameIndex newFrame)
        {
            if (_mainPictureBox.InvokeRequired)
            {
                _mainPictureBox.BeginInvoke(new Action(() => HandleNewFrame(newFrame)));
                return;
            }

            if (_isLoading)
            {
                _pendingFrames.Add(newFrame);
                return;
            }

            int appId = -1;
            appId = _dataManager.GetAppId(newFrame.AppName);
            
            if (appId == -1)
            {
                _dataManager.UpdateAppMap();
                appId = _dataManager.GetAppId(newFrame.AppName);

                if (appId != -1)
                {
                    _appFilterController.RegisterApp(appId, newFrame.AppName);
                }
                else
                {
                    if (_dataManager.AppMap.Count > 0) appId = _dataManager.AppMap.Keys.Max() + 1;
                    else appId = 1;
                    
                    _dataManager.RegisterApp(appId, newFrame.AppName);
                    _appFilterController.RegisterApp(appId, newFrame.AppName);
                }
            }

            var miniFrame = new MiniFrame { TimestampTicks = newFrame.TimestampTicks, AppId = appId, IntervalMs = newFrame.IntervalMs };

            _dataManager.AddFrameToCache(miniFrame);

            if ((_isGlobalMode || (newFrame.GetTime().Date >= _currentStartDate && newFrame.GetTime().Date <= _currentEndDate)) && _dataManager.AllLoadedFrames != null)
            {
                _dataManager.AddFrameToAll(miniFrame);
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

        private void ProcessNewFrame(MiniFrame newFrame)
        {
            if (_isLoading || _dataManager.FilteredFrames == null || _dataManager.AllLoadedFrames == null)
            {
                return; 
            }

            var filter = _selectedAppFilter;
            
            bool matches = (filter == null || filter.Count == 0) || _dataManager.MatchesAppFilter(newFrame, filter);

            if (matches)
            {
                if (_dataManager.FilteredFrames != _dataManager.AllLoadedFrames)
                {
                    if (!_dataManager.FilteredFrames.Any(x => x.TimestampTicks == newFrame.TimestampTicks))
                    {
                        _dataManager.FilteredFrames.Add(newFrame);
                    }
                }

                _timelineController.SetFrames(_dataManager.FilteredFrames, true, false);
                _timelineController.UpdateInfoLabel();

                if (_currentFrameIndex == -1 && _wantedFrameIndex == -1 && _dataManager.FilteredFrames.Count > 0)
                {
                    int newIndex = _dataManager.FilteredFrames.Count - 1;
                    _wantedFrameIndex = newIndex;
                    _timelineController.CurrentIndex = newIndex;
                }
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

        public void GetState(out List<MiniFrame> frames, out List<string> filter, out int index)
        {
            frames = _dataManager.AllLoadedFrames;
            filter = _selectedAppFilter;
            index = _currentFrameIndex;
        }

        public void RestoreState(List<MiniFrame> frames, List<string> filter, int index)
        {
            _dataManager.RestoreState(frames);
            _selectedAppFilter = filter;
            _isInitialLoad = true;
            
            _appFilterController.SetDataAsync(_dataManager.GetAllLoadedFramesCopy(), _dataManager.AppMap);
            
            ApplyAppFilterAndDisplay(isLiveUpdate: false);
            var filteredFrames = _dataManager.FilteredFrames;

            if (index >= 0 && index < filteredFrames.Count)
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
            FrameChanged = null;
            CurrentDateChanged = null;
            OcrBlacklistToggled = null;

            _uiTimer?.Stop(); _uiTimer?.Dispose();
            _imageLoadCts?.Cancel(); _imageLoadCts?.Dispose();

            var txtAppSearch = _appFilterController.GetType().GetField("_txtAppSearch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_appFilterController) as TextBox;
            if (txtAppSearch != null) txtAppSearch.KeyDown -= OnSearchKeyDown;

            if (_txtOcrSearch != null) _txtOcrSearch.KeyDown -= OnOcrSearchKeyDown;

            if (_appFilterController != null) 
            { 
                _appFilterController.FilterChanged -= OnAppFilterChanged; 
                _appFilterController.AppHidden -= OnAppHidden;
                _appFilterController.Dispose(); 
            }
            if (_frameRepository != null)
            {
                _frameRepository.DataInvalidated -= OnDataInvalidated;
            }

            if (_timelineController != null) { _timelineController.TimeChanged -= OnTimeChanged; _timelineController.Dispose(); }
            
            _mediaController?.Dispose();

            if (_lblFormatBadge != null) _lblFormatBadge.Image = null;

            _iconVideo?.Dispose(); _iconImage?.Dispose();
            _contextMenu?.Dispose();
            if (_ocrTextForm != null && !_ocrTextForm.IsDisposed) _ocrTextForm.Close();
        }
    }
}
