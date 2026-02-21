using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Recap.Utilities;

namespace Recap
{
    public class IconManager : IDisposable
    {
        public OcrDatabase Database { get; set; }

        private readonly Dictionary<string, Image> _memoryCache = new Dictionary<string, Image>();
        private readonly HashSet<string> _currentlyLoading = new HashSet<string>();
        private readonly HashSet<string> _failedAttempts = new HashSet<string>();

        private readonly string _iconCachePath;
        private readonly string _thumbCachePath;

        private readonly Image _loadingIcon;
        private readonly Image _errorIcon;
        private readonly Image _allAppsIcon;
        private readonly Image _youTubeIcon;
        public Image QuestionIcon { get; private set; }
        public Image InfoIcon { get; private set; }
        public Image WarningIcon { get; private set; }

        private readonly object _lock = new object();
        private bool _isDisposed = false;

        private enum IconType { Exe, Web, YouTubeThumb, Composite }

        public event Action<string> IconLoaded;

        public bool DisableVideoPreviews { get; set; }

        private readonly HashSet<string> _browserPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "New Tab",
            "Extensions",
            "Extensions Manager",
            "Extension Page",
            "Settings",
            "History",
            "Downloads"
        };

        public IconManager()
        {
            _iconCachePath = Path.Combine(Application.LocalUserAppDataPath, "IconCache");
            _thumbCachePath = Path.Combine(Application.LocalUserAppDataPath, "ThumbCache");

            Directory.CreateDirectory(_iconCachePath);
            Directory.CreateDirectory(_thumbCachePath);

            _loadingIcon = CreateSolidIcon(Color.FromArgb(80, 80, 80));
            _errorIcon = CreateErrorIcon();     
            _allAppsIcon = CreateAllAppsIcon();
            _youTubeIcon = CreateYouTubeFolderIcon();

            QuestionIcon = IconGenerator.GetMessageBoxIcon(MessageBoxIcon.Question, 32);
            InfoIcon = IconGenerator.GetMessageBoxIcon(MessageBoxIcon.Information, 32);
            WarningIcon = IconGenerator.GetMessageBoxIcon(MessageBoxIcon.Warning, 32);
        }

        public Image GetMessageBoxIcon(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Question: return QuestionIcon;
                case MessageBoxIcon.Information: return InfoIcon;
                case MessageBoxIcon.Warning: return WarningIcon;
                case MessageBoxIcon.Error: return _errorIcon;
                default: return null;
            }
        }

        public Image GetQuestionIcon() => QuestionIcon;
        public Image GetInfoIcon() => InfoIcon;
        public Image GetWarningIcon() => WarningIcon;
        public Image GetErrorIcon() => _errorIcon;

        public Image GetIcon(string rawAppName)
        {
            if (rawAppName == Localization.Get("allApps")) return _allAppsIcon;

            if (rawAppName.StartsWith("$$COMPOSITE$$"))
            {
                return GetCompositeIcon(rawAppName);
            }

            var parts = rawAppName.Split('|');
            string exeName = parts[0];
            string lowerExe = exeName.ToLower();

            string cacheKey; 
            IconType type = IconType.Exe;
            string payload = exeName;

            if (lowerExe.Contains("code.exe") || lowerExe.Contains("devenv.exe") || 
                lowerExe.Contains("code") || lowerExe.Contains("visualstudio") || lowerExe.Contains("antigravity")) 
            {
                cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                type = IconType.Exe;
                payload = exeName;
            }
            else if (lowerExe.Contains("telegram") ||
                               lowerExe.Contains("ayugram") ||
                               lowerExe.Contains("kotatogram"))
            {
                cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                type = IconType.Exe;
            }
            else if (parts.Length >= 3 && parts[1].Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            {
                string detailName = parts[2];

                if (detailName.Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    return _youTubeIcon;
                }

                string videoId = ExtractVideoId(detailName);

                if (!string.IsNullOrEmpty(videoId))
                {
                    cacheKey = "yt_" + videoId;
                    type = IconType.YouTubeThumb;
                    payload = videoId;

                    if (DisableVideoPreviews)
                    {
                        lock (_lock)
                        {
                            if (_memoryCache.TryGetValue(cacheKey, out var icon)) return icon;
                        }

                        string thumbPath = Path.Combine(_thumbCachePath, cacheKey + ".jpg");
                        if (!File.Exists(thumbPath))
                        {
                            return _youTubeIcon;
                        }
                    }
                }
                else
                {
                    cacheKey = "web_" + MakeSafeFilename(detailName).GetHashCode();
                    type = IconType.Web;
                    payload = detailName;
                }
            }
            else if (parts.Length >= 2)
            {
                string groupName = parts[1];

                if (groupName.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    return _youTubeIcon;
                }

                if (_browserPages.Contains(groupName))
                {
                    cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                    type = IconType.Exe;
                    payload = exeName;
                }
                else
                {
                    cacheKey = "web_" + MakeSafeFilename(groupName).ToLower();
                    type = IconType.Web;
                    payload = groupName;
                }
            }
            else
            {
                if (exeName == Localization.Get("legacyApp")) return _errorIcon;
                cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                type = IconType.Exe;
            }

            lock (_lock)
            {
                if (_isDisposed) return _errorIcon;
                if (_memoryCache.TryGetValue(cacheKey, out var icon)) return icon;
                if (_failedAttempts.Contains(cacheKey)) return _errorIcon;
                if (_currentlyLoading.Contains(cacheKey)) return _loadingIcon;

                _currentlyLoading.Add(cacheKey);
            }

            Task.Run(() =>
            {
                try
                {
                    switch (type)
                    {
                        case IconType.YouTubeThumb:
                            LoadYouTubeThumbnail(payload, cacheKey, rawAppName);
                            break;
                        case IconType.Web:
                            LoadWebIcon(payload, cacheKey, rawAppName);
                            break;
                        case IconType.Exe:
                            LoadExeIcon(payload, cacheKey, rawAppName);
                            break;
                        case IconType.Composite:
                            LoadCompositeIcon(payload, cacheKey, rawAppName);
                            break;
                    }
                }
                catch
                {
                    FinishLoading(cacheKey, null, rawAppName);
                }
            });

            return _loadingIcon;
        }

        private void LoadYouTubeThumbnail(string videoId, string cacheKey, string originalName)
        {
            string thumbPath = Path.Combine(_thumbCachePath, cacheKey + ".jpg");
            Image loadedImage = null;

            try
            {
                if (File.Exists(thumbPath))
                {
                    using (var temp = new Bitmap(thumbPath)) loadedImage = new Bitmap(temp);
                }
                else
                {
                    string url = $"https://img.youtube.com/vi/{videoId}/default.jpg";

                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", AdvancedSettings.Instance.UserAgent);
                        byte[] data = client.DownloadData(url);
                        using (var ms = new MemoryStream(data))
                        using (var original = new Bitmap(ms))
                        {
                            int w = 32;
                            int h = 24;
                            using (var resized = ResizeHighQuality(original, w, h))
                            {
                                resized.Save(thumbPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                                loadedImage = new Bitmap(resized);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            FinishLoading(cacheKey, loadedImage, originalName);
        }

        private void LoadWebIcon(string domain, string cacheKey, string originalName)
        {
            string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");
            Image loadedIcon = null;

            try
            {
                if (File.Exists(iconPath))
                {
                    using (var temp = new Bitmap(iconPath)) loadedIcon = new Bitmap(temp);
                }
                else
                {
                    string url = $"https://www.google.com/s2/favicons?domain={domain}&sz=32";
                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", AdvancedSettings.Instance.UserAgent);
                        byte[] data = client.DownloadData(url);
                        using (var ms = new MemoryStream(data))
                        using (var bmp = new Bitmap(ms))
                        {
                            bmp.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                            loadedIcon = new Bitmap(bmp);
                        }
                    }
                }
            }
            catch { }

            FinishLoading(cacheKey, loadedIcon, originalName);
        }

        private void LoadExeIcon(string exeName, string cacheKey, string originalName)
        {
            string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");
            Image loadedIcon = null;

            try
            {
                if (File.Exists(iconPath))
                {
                    using (var temp = new Bitmap(iconPath)) loadedIcon = new Bitmap(temp);
                }
                else
                {
                    if (Database != null && Database.CheckHasCustomIcon(originalName))
                    {
                    }
                    else
                    {
                        string exePath = FindExePath(exeName);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            using (var jumbo = IconExtractor.GetWin32JumboIcon(exePath))
                        {
                            if (jumbo != null)
                            {
                                using (var trimmed = TrimTransparent(jumbo))
                                using (var resized = ResizeHighQuality(trimmed, 16, 16))
                                {
                                    resized.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                                    loadedIcon = new Bitmap(resized);
                                }
                            }
                            else
                            {
                                Icon extractedIcon = Icon.ExtractAssociatedIcon(exePath);
                                if (extractedIcon != null)
                                {
                                    using (var iconBmp = extractedIcon.ToBitmap())
                                    using (var resized = ResizeHighQuality(iconBmp, 16, 16))
                                    {
                                        resized.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                                        loadedIcon = new Bitmap(resized);
                                    }
                                    extractedIcon.Dispose();
                                }
                            }
                        }
                    }
                }
            }
            }
            catch { }

            FinishLoading(cacheKey, loadedIcon, originalName);
        }

        public void SetCustomIcon(string appName, string imagePath)
        {
            try
            {
                using (var bitmap = IconHelper.ProcessUserImage(imagePath))
                {
                    string cacheKey = GetCacheKey(appName);
                    string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");

                    bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);

                    lock (_lock)
                    {
                        if (_memoryCache.ContainsKey(cacheKey))
                        {
                            var old = _memoryCache[cacheKey];
                            old?.Dispose();
                        }
                        _memoryCache[cacheKey] = new Bitmap(bitmap);
                        _failedAttempts.Remove(cacheKey);
                    }
                }

                Database?.SetHasCustomIcon(appName, true);

                IconLoaded?.Invoke(appName);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("SetCustomIcon", ex);
            }
        }

        public void ResetCustomIcon(string appName)
        {
            try
            {
                string cacheKey = GetCacheKey(appName);
                string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");

                lock (_lock)
                {
                    if (_memoryCache.ContainsKey(cacheKey))
                    {
                        var old = _memoryCache[cacheKey];
                        _memoryCache.Remove(cacheKey);

                    }
                }

                if (File.Exists(iconPath))
                {
                    File.Delete(iconPath);
                }

                Database?.SetHasCustomIcon(appName, false);
                
                lock (_lock)
                {
                     _failedAttempts.Remove(cacheKey);
                }

                IconLoaded?.Invoke(appName);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("ResetCustomIcon", ex);
            }
        }

        private string GetCacheKey(string rawAppName)
        {
             if (rawAppName == Localization.Get("allApps")) return "all_apps";      

             var parts = rawAppName.Split('|');
             string exeName = parts[0];
             string lowerExe = exeName.ToLower();

             if (lowerExe.Contains("code.exe") || lowerExe.Contains("devenv.exe") || 
                 lowerExe.Contains("code") || lowerExe.Contains("visualstudio") || lowerExe.Contains("antigravity")) 
             {
                 return "exe_" + MakeSafeFilename(exeName).ToLower();
             }
             
             if (lowerExe.Contains("telegram") || lowerExe.Contains("ayugram") || lowerExe.Contains("kotatogram"))
             {
                 return "exe_" + MakeSafeFilename(exeName).ToLower();
             }

             if (parts.Length >= 3 && parts[1].Equals("YouTube", StringComparison.OrdinalIgnoreCase))
             {
                 string detailName = parts[2];
                 if (detailName.Equals("Home", StringComparison.OrdinalIgnoreCase)) return "yt_home";    
                 string videoId = ExtractVideoId(detailName);
                 if (!string.IsNullOrEmpty(videoId)) return "yt_" + videoId;
                 return "web_" + MakeSafeFilename(detailName).GetHashCode();
             }
             
             if (parts.Length >= 2)
             {
                 string groupName = parts[1];
                 if (groupName.Equals("YouTube", StringComparison.OrdinalIgnoreCase)) return "yt_folder";  
                 
                 if (_browserPages.Contains(groupName))
                     return "exe_" + MakeSafeFilename(exeName).ToLower();
                 
                 return "web_" + MakeSafeFilename(groupName).ToLower();
             }

             return "exe_" + MakeSafeFilename(exeName).ToLower();
        }

        public void TryFetchIconFromHwnd(IntPtr hWnd, string rawAppName)
        {
            if (hWnd == IntPtr.Zero || string.IsNullOrEmpty(rawAppName)) return;

            var parts = rawAppName.Split('|');
            string exeName = parts[0];
            string cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();

            lock (_lock)
            {
                if (_memoryCache.ContainsKey(cacheKey)) return;   
            }

            Task.Run(() =>
            {
                try
                {
                    using (Bitmap jumbo = IconExtractor.GetJumboIconFromHwnd(hWnd))
                    {
                        if (jumbo != null)
                        {
                            using (var trimmed = TrimTransparent(jumbo))
                            using (var resized = ResizeHighQuality(trimmed, 16, 16))
                            {
                                string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");
                                resized.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);

                                Image finalIcon = new Bitmap(resized);
                                
                                lock (_lock)
                                {
                                    _memoryCache[cacheKey] = finalIcon;
                                }
                                IconLoaded?.Invoke(rawAppName);
                            }
                        }
                        else
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("TryFetchIconFromHwnd", ex);
                }
            });
        }

        private void FinishLoading(string cacheKey, Image icon, string originalName)
        {
            lock (_lock)
            {
                if (_isDisposed) { icon?.Dispose(); return; }

                if (icon != null)
                {
                    _memoryCache[cacheKey] = icon;
                }
                else
                {
                    _memoryCache[cacheKey] = _errorIcon;
                    _failedAttempts.Add(cacheKey);
                }

                _currentlyLoading.Remove(cacheKey);
            }

            IconLoaded?.Invoke(originalName);
        }

        private string ExtractVideoId(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var regex = new Regex(@"(?:v=|\/)([0-9A-Za-z_-]{11})");
            var match = regex.Match(text);
            if (match.Success) return match.Groups[1].Value;
            return null;
        }

        private string MakeSafeFilename(string text)
        {
            if (string.IsNullOrEmpty(text)) return "unknown";
            if (text.Length > 50) return Math.Abs(text.GetHashCode()).ToString();
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
            return text;
        }

        private Bitmap TrimTransparent(Bitmap source)
        {
            if (source == null) return null;

            System.Drawing.Imaging.BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            
            int top = -1, bottom = -1, left = -1, right = -1;

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int stride = data.Stride;
                    int width = source.Width;
                    int height = source.Height;

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (ptr[y * stride + x * 4 + 3] > 0)
                            {
                                top = y;
                                break;
                            }
                        }
                        if (top != -1) break;
                    }

                    if (top == -1)     
                    {
                        return new Bitmap(1, 1);
                    }

                    for (int y = height - 1; y >= top; y--)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (ptr[y * stride + x * 4 + 3] > 0)
                            {
                                bottom = y;
                                break;
                            }
                        }
                        if (bottom != -1) break;
                    }

                    for (int x = 0; x < width; x++)
                    {
                        for (int y = top; y <= bottom; y++)
                        {
                            if (ptr[y * stride + x * 4 + 3] > 0)
                            {
                                left = x;
                                break;
                            }
                        }
                        if (left != -1) break;
                    }

                    for (int x = width - 1; x >= left; x--)
                    {
                        for (int y = top; y <= bottom; y++)
                        {
                            if (ptr[y * stride + x * 4 + 3] > 0)
                            {
                                right = x;
                                break;
                            }
                        }
                        if (right != -1) break;
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }

            if (left == -1 || right == -1 || top == -1 || bottom == -1) return new Bitmap(source);

            int newWidth = right - left + 1;
            int newHeight = bottom - top + 1;

            Bitmap cropped = new Bitmap(newWidth, newHeight);
            using (Graphics g = Graphics.FromImage(cropped))
            {
                g.DrawImage(source, new Rectangle(0, 0, newWidth, newHeight), new Rectangle(left, top, newWidth, newHeight), GraphicsUnit.Pixel);
            }

            return cropped;
        }

        private Bitmap ResizeHighQuality(Image src, int width, int height)
        {
            var dest = new Bitmap(width, height);
            using (var g = Graphics.FromImage(dest))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, width, height);
            }
            return dest;
        }

        private Bitmap CreateYouTubeFolderIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(255, 0, 0))) g.FillRectangle(brush, 1, 3, 14, 10);
                using (var brush = new SolidBrush(Color.White)) g.FillPolygon(brush, new Point[] { new Point(6, 5), new Point(6, 11), new Point(11, 8) });
            }
            return bmp;
        }

        private Bitmap CreateSolidIcon(Color c)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp)) using (var b = new SolidBrush(c)) g.FillRectangle(b, 0, 0, 16, 16);
            return bmp;
        }

        private Bitmap CreateErrorIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(Color.Gray)) g.FillRectangle(b, 0, 0, 16, 16);

                using (var b = new SolidBrush(Color.White))
                {
                    var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
                    g.DrawString("?", font, b, 1, -1);
                }
            }
            return bmp;
        }

        private Bitmap CreateAllAppsIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(Color.Gray)) g.FillRectangle(b, 0, 0, 16, 16);
                using (var b = new SolidBrush(Color.White)) { g.FillRectangle(b, 2, 2, 5, 5); g.FillRectangle(b, 9, 2, 5, 5); g.FillRectangle(b, 2, 9, 5, 5); g.FillRectangle(b, 9, 9, 5, 5); }
            }
            return bmp;
        }

        private Image GetCompositeIcon(string rawAppName)
        {
            string compositeKey = "comp_" + rawAppName.GetHashCode();       
            
            lock (_lock)
            {
                if (_isDisposed) return _errorIcon;
                if (_memoryCache.TryGetValue(compositeKey, out var icon)) return icon;
                if (_failedAttempts.Contains(compositeKey)) return _errorIcon;
                if (_currentlyLoading.Contains(compositeKey)) return _loadingIcon;

                _currentlyLoading.Add(compositeKey);
            }

            Task.Run(() =>
            {
                try
                {
                    LoadCompositeIcon(rawAppName, compositeKey, rawAppName);
                }
                catch
                {
                    FinishLoading(compositeKey, null, rawAppName);
                }
            });

            return _loadingIcon;
        }

        private void LoadCompositeIcon(string payload, string cacheKey, string originalName)
        {
            string[] items = payload.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var apps = items.Skip(1).Take(4).ToList();
            
            if (apps.Count == 0)
            {
                FinishLoading(cacheKey, null, originalName);
                return;
            }

            var images = new List<Image>();
            foreach (var app in apps)
            {
                var icon = GetIcon(app);
                
                int retries = 0;
                while (icon == _loadingIcon && retries < 10)
                {
                    System.Threading.Thread.Sleep(50);
                    icon = GetIcon(app);
                    retries++;
                }
                
                if (icon == _loadingIcon || icon == null) icon = _errorIcon; 
                images.Add(icon);
            }

            Bitmap canvas = new Bitmap(16, 16);     
            canvas = new Bitmap(32, 32);

            using (var g = Graphics.FromImage(canvas))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                int count = images.Count;
                
                if (count == 1)
                {
                    g.DrawImage(images[0], 0, 0, 32, 32);
                }
                else if (count == 2)
                {
                    g.DrawImage(images[0], 8, 0, 16, 16);
                    g.DrawImage(images[1], 8, 16, 16, 16);
                }
                else if (count == 3)
                {
                    g.DrawImage(images[0], 8, 0, 16, 16);
                    
                    g.DrawImage(images[1], 0, 16, 16, 16);
                    
                    g.DrawImage(images[2], 16, 16, 16, 16);
                }
                else if (count >= 4)
                {
                    g.DrawImage(images[0], 0, 0, 16, 16);
                    g.DrawImage(images[1], 16, 0, 16, 16);
                    g.DrawImage(images[2], 0, 16, 16, 16);
                    g.DrawImage(images[3], 16, 16, 16, 16);
                }
            }

            FinishLoading(cacheKey, canvas, originalName);
        }

        #region WinAPI
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        private string FindExePath(string appName)
        {
            string processName = appName.Replace(".exe", "");
            
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var buffer = new StringBuilder(1024);
                        int size = buffer.Capacity;
                        IntPtr hProcess = OpenProcess(0x1000, false, process.Id);
                        if (hProcess != IntPtr.Zero)
                        {
                            if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                            {
                                string path = buffer.ToString();
                                CloseHandle(hProcess);
                                return path;
                            }
                            CloseHandle(hProcess);
                        }
                    }
                    catch { }
                }
            }

            if (appName.Equals("telegram.exe", StringComparison.OrdinalIgnoreCase))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string tPath = Path.Combine(appData, "Telegram Desktop", "Telegram.exe");
                if (File.Exists(tPath)) return tPath;
            }

            return null;
        }
        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                foreach (var img in _memoryCache.Values) img?.Dispose();
                _memoryCache.Clear();
            }
            _loadingIcon?.Dispose();
            _errorIcon?.Dispose();
            _allAppsIcon?.Dispose();
            _youTubeIcon?.Dispose();
        }
    }
}