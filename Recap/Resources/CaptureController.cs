using System;
using System.IO;
using System.Text.RegularExpressions;     
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class CaptureController : IDisposable
    {
        public event Action<FrameIndex> FrameCaptured;
        public event Action DayChanged;

        private readonly ScreenshotService _screenshotService;
        private readonly FrameRepository _frameRepository;
        private AppSettings _settings;
        private readonly Timer _screenshotTimer;

        private readonly BrowserTracker _browserTracker;

        private byte[] _lastScreenshotHash = null;
        private DateTime _lastCaptureDate;

        public bool IsCapturing { get; private set; } = false;

        private readonly OcrDatabase _ocrDb;
        private readonly string _tempOcrPath;
        private readonly OcrService _ocrService;
        private readonly IconManager _iconManager;

        public CaptureController(ScreenshotService screenshotService, FrameRepository frameRepository, AppSettings settings, OcrDatabase ocrDb, OcrService ocrService = null, IconManager iconManager = null)
        {
            _screenshotService = screenshotService;
            _frameRepository = frameRepository;
            _settings = settings;
            _ocrDb = ocrDb;
            _ocrService = ocrService;
            _iconManager = iconManager;

            _screenshotService.Settings = _settings;

            if (!string.IsNullOrEmpty(_settings.StoragePath))
            {
                _tempOcrPath = Path.Combine(_settings.StoragePath, "tempOCR");
                try { if (!Directory.Exists(_tempOcrPath)) Directory.CreateDirectory(_tempOcrPath); } catch { }
            }

            _browserTracker = new BrowserTracker();

            _screenshotTimer = new Timer();
            _screenshotTimer.Interval = _settings.IntervalMs;
            _screenshotTimer.Tick += OnTimerTick;

            _lastCaptureDate = DateTime.MinValue;
        }

        public void Start()
        {
            if (IsCapturing) return;

            _lastScreenshotHash = null;
            _lastCaptureDate = DateTime.Today;
            IsCapturing = true;
            _screenshotTimer.Start();
            DebugLogger.Log("Capture started.");
        }

        public void Stop()
        {
            if (!IsCapturing) return;

            IsCapturing = false;
            _screenshotTimer.Stop();
            DebugLogger.Log("Capture stopped.");
        }

        public void UpdateSettings(AppSettings newSettings)
        {
            _settings = newSettings;
            _screenshotService.Settings = newSettings;
            _screenshotTimer.Interval = _settings.IntervalMs;
        }

        private async void OnTimerTick(object sender, EventArgs e)
        {
            if (!IsCapturing) return;
            _screenshotTimer.Stop();

            DateTime now = DateTime.Now;
            if (_lastCaptureDate.Date < now.Date)
            {
                DayChanged?.Invoke();
                _lastCaptureDate = now;
            }

            string tempGuid = Guid.NewGuid().ToString();
            string tempFile = null;
            if (!string.IsNullOrEmpty(_tempOcrPath))
            {
                tempFile = System.IO.Path.Combine(_tempOcrPath, tempGuid + ".jpg");
            }

            var result = await _screenshotService.TakeScreenshotAsync(_lastScreenshotHash, tempFile);

            if (result.JpegBytes != null)
            {
                _lastScreenshotHash = result.NewHash;
                
                IntPtr hWnd = ActiveWindowHelper.GetActiveWindowHandle();
                IntPtr realHwnd = ActiveWindowHelper.GetRealWindow(hWnd);

                string processName = ActiveWindowHelper.GetProcessNameFromHwnd(realHwnd);
                string finalAppName = processName;
                string procLower = processName.ToLower();

                if (procLower.Contains("chrome") ||
                    procLower.Contains("msedge") ||
                    procLower.Contains("brave") ||
                    procLower.Contains("opera") ||
                    procLower.Contains("yandex"))
                {
                    string domain = _browserTracker.CurrentDomain;
                    if (!string.IsNullOrEmpty(domain))
                    {
                        finalAppName = $"{processName}|{domain}";
                    }
                }
                else if (procLower.Contains("telegram") || procLower.Contains("ayugram") || procLower.Contains("kotatogram"))
                {
                    string title = ActiveWindowHelper.GetWindowTitleFromHwnd(hWnd);

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        string chatName = title;

                        chatName = Regex.Replace(chatName, @"[\d\(\)]", "");

                        chatName = chatName.Trim();
                        if (!string.IsNullOrEmpty(chatName) &&
                            !chatName.Equals("Telegram", StringComparison.OrdinalIgnoreCase) &&
                            !chatName.Equals("AyuGram", StringComparison.OrdinalIgnoreCase))
                        {
                            finalAppName = $"{processName}|{chatName}";
                        }
                    }
                }

                FrameIndex? newFrame = await _frameRepository.SaveFrame(result.JpegBytes, finalAppName, result.IntervalMs);

                if (newFrame.HasValue)
                {
                    _iconManager?.TryFetchIconFromHwnd(realHwnd, finalAppName);

                    if (!string.IsNullOrEmpty(tempFile) && System.IO.File.Exists(tempFile))
                    {
                        try
                        {
                            string finalPath = System.IO.Path.Combine(_tempOcrPath, newFrame.Value.TimestampTicks + ".jpg");
                            System.IO.File.Move(tempFile, finalPath);
                            _ocrDb?.AddFrame(newFrame.Value.TimestampTicks, finalAppName);
                            _ocrService?.SignalNewWork();
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogError("CaptureController.OcrRename", ex);
                            try { System.IO.File.Delete(tempFile); } catch { }
                        }
                    }

                    FrameCaptured?.Invoke(newFrame.Value);
                }
                else
                {
                    if (!string.IsNullOrEmpty(tempFile) && System.IO.File.Exists(tempFile))
                    {
                        try { System.IO.File.Delete(tempFile); } catch { }
                    }
                }
            }

            if (IsCapturing)
            {
                _screenshotTimer.Start();
            }
        }

        public void Dispose()
        {
            _browserTracker?.Dispose();
            _screenshotTimer?.Stop();
            _screenshotTimer?.Dispose();
        }
    }
}