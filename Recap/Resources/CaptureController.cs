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
        private SystemLoadMonitor _systemLoadMonitor;
        private readonly System.Collections.Generic.HashSet<string> _pathSavedApps = new System.Collections.Generic.HashSet<string>();

        public CaptureController(ScreenshotService screenshotService, FrameRepository frameRepository, AppSettings settings, OcrDatabase ocrDb, OcrService ocrService = null, IconManager iconManager = null)
        {
            _screenshotService = screenshotService;
            _frameRepository = frameRepository;
            _settings = settings;
            _ocrDb = ocrDb;
            _ocrService = ocrService;
            _iconManager = iconManager;
            _systemLoadMonitor = new SystemLoadMonitor();

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

            try
            {
                DateTime now = DateTime.Now;
            if (_lastCaptureDate.Date < now.Date)
            {
                DayChanged?.Invoke();
                _lastCaptureDate = now;
            }

            bool useHybrid = AdvancedSettings.Instance.UseHybridOcr;
            bool isHighLoad = _systemLoadMonitor.IsHighLoad();
            bool saveToDisk = !useHybrid || isHighLoad;

            var result = await _screenshotService.TakeScreenshotAsync(_lastScreenshotHash, null, false);

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
                    procLower.Contains("opera"))
                {
                    string domain = _browserTracker.CurrentDomain;
                    if (!string.IsNullOrEmpty(domain))
                    {
                        string title = ActiveWindowHelper.GetWindowTitleFromHwnd(hWnd);
                        title = CleanupBrowserSuffixes(title);

                        if (domain.Contains("kick.com"))
                        {
                            string clean = CleanupTitle(title, new[] { " - Kick", " | Kick" });
                            if (!string.IsNullOrEmpty(clean))
                            {
                                finalAppName = $"{processName}|kick.com|{clean}";
                            }
                            else
                            {
                                finalAppName = $"{processName}|kick.com|Stream"; 
                            }
                        }
                        else if (domain.Contains("aistudio.google.com"))
                        {
                            string clean = CleanupTitle(title, new[] { " - Google AI Studio", " | Google AI Studio" });
                            if (!string.IsNullOrEmpty(clean))
                            {
                                finalAppName = $"{processName}|aistudio.google.com|{clean}";
                            }
                            else 
                            {
                                finalAppName = $"{processName}|aistudio.google.com|Prompt";
                            }
                        }
                        else
                        {
                            finalAppName = $"{processName}|{domain}";
                        }
                    }
                }
                else if (procLower.Contains("code"))
                {
                    string title = ActiveWindowHelper.GetWindowTitleFromHwnd(hWnd);
                    if (title.StartsWith("● ")) title = title.Substring(2);

                    string project = CleanupTitle(title, new[] { " - Visual Studio Code" });
                    
                    if (!string.IsNullOrEmpty(project))
                    {
                        int lastDash = project.LastIndexOf(" - ");
                        if (lastDash >= 0 && lastDash < project.Length - 3)
                        {
                            string projName = project.Substring(lastDash + 3);
                            string fileName = project.Substring(0, lastDash);
                            finalAppName = $"{processName}|{projName}|{fileName}";
                        }
                        else
                        {
                            finalAppName = $"{processName}|{project}";
                        }
                    }
                }
                else if (procLower.Contains("devenv"))
                {
                    string title = ActiveWindowHelper.GetWindowTitleFromHwnd(hWnd);
                    string solution = CleanupTitle(title, new[] { " - Microsoft Visual Studio", " - Visual Studio" });
                    
                    if (!string.IsNullOrEmpty(solution))
                    {
                         int lastDash = solution.LastIndexOf(" - ");
                        if (lastDash >= 0 && lastDash < solution.Length - 3)
                        {
                            string solName = solution.Substring(0, lastDash);
                            string fileName = solution.Substring(lastDash + 3);
                            
                            fileName = fileName.TrimEnd('*');
                            fileName = Regex.Replace(fileName, @"\s\([^\)]+\)$", "");
                            finalAppName = $"{processName}|{solName}|{fileName}";
                        }
                        else
                        {
                            solution = solution.TrimEnd('*');
                            solution = Regex.Replace(solution, @"\s\([^\)]+\)$", "");
                            finalAppName = $"{processName}|{solution}";
                        }
                    }
                }
                else if (procLower.Contains("antigravity"))
                {
                    string title = ActiveWindowHelper.GetWindowTitleFromHwnd(hWnd);
                    if (title.StartsWith("● ")) title = title.Substring(2);

                    string[] parts = title.Split(new[] { " - Antigravity - " }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        string projName = parts[0].Trim();
                        string fileName = parts[1].Trim();
                        finalAppName = $"{processName}|{projName}|{fileName}";
                    }
                    else
                    {
                        string clean = CleanupTitle(title, new[] { " - Antigravity" });
                        finalAppName = $"{processName}|{clean}";
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

                bool isBlacklisted = false;
                if (_settings.OcrBlacklist != null && _settings.OcrBlacklist.Count > 0)
                {
                    string procClean = processName.ToLower().Replace(".exe", "");
                    
                    foreach (string item in _settings.OcrBlacklist)
                    {
                        string itemClean = item.ToLower().Replace(".exe", "");
                        
                        if (procClean == itemClean)
                        {
                            isBlacklisted = true;
                            break;
                        }
                    }
                }

                FrameIndex? newFrame = await _frameRepository.SaveFrame(result.JpegBytes, finalAppName, result.IntervalMs);

                if (newFrame.HasValue)
                {
                    if (!string.IsNullOrEmpty(processName) &&
                        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !_pathSavedApps.Contains(processName))
                    {
                        string exePath = ActiveWindowHelper.GetProcessPathFromHwnd(realHwnd);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            _ocrDb?.UpdateExecutablePath(processName, exePath);
                        }

                        _pathSavedApps.Add(processName);
                    }

                    _iconManager?.TryFetchIconFromHwnd(realHwnd, finalAppName);

                    if (finalAppName.Contains("|"))
                    {
                        var parts = finalAppName.Split('|');
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                        {
                            _iconManager?.TryFetchIconFromHwnd(realHwnd, parts[0]);
                        }
                    }

                    if (_ocrService != null && _settings.EnableOCR && !isBlacklisted)
                    {
                        try 
                        {
                            System.Drawing.Bitmap original = _screenshotService.GetLastCapturedBitmap();
                            if (original != null)
                            {
                                System.Drawing.Bitmap snapshot = (System.Drawing.Bitmap)original.Clone();

                                if (saveToDisk && !string.IsNullOrEmpty(_tempOcrPath))
                                {
                                    string finalPath = System.IO.Path.Combine(_tempOcrPath, newFrame.Value.TimestampTicks + ".jpg");
                                    snapshot.Save(finalPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                                    snapshot.Dispose();
                                    
                                    _ocrDb?.AddFrame(newFrame.Value.TimestampTicks, finalAppName);
                                    _ocrService.SignalNewWork();
                                }
                                else
                                {
                                    _ocrDb?.AddFrame(newFrame.Value.TimestampTicks, finalAppName);
                                    _ocrService.EnqueueImage(snapshot, newFrame.Value.TimestampTicks);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogError("CaptureController.SnapshotWithClone", ex);
                        }
                    }

                    FrameCaptured?.Invoke(newFrame.Value);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("CaptureController.OnTimerTick", ex);
            }
            finally
            {
                if (IsCapturing)
                {
                    _screenshotTimer.Start();
                }
            }
        }

        private string CleanupBrowserSuffixes(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string[] browserSuffixes = new[] {
                " - Google Chrome",
                " - Microsoft Edge",
                " - Opera",
                " - Brave",
                " - Mozilla Firefox"
            };

            foreach (var suffix in browserSuffixes)
            {
                if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return title.Substring(0, title.Length - suffix.Length);
                }
            }
            return title;
        }

        private string CleanupTitle(string title, string[] suffixesToRemove)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            string current = title;
            foreach (var suffix in suffixesToRemove)
            {
                if (current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    current = current.Substring(0, current.Length - suffix.Length);
                }
            }
            return current.Trim();
        }

        public void Dispose()
        {
            _systemLoadMonitor?.Dispose();
            _browserTracker?.Dispose();
            _screenshotTimer?.Stop();
            _screenshotTimer?.Dispose();
        }
    }
}