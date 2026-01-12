using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

using System.IO.Compression;
using System.Text;
using System.Threading;

namespace Recap
{
    public class OcrService
    {
        [Serializable]
        public class WordData
        {
            public string T { get; set; }  
            public float X { get; set; }     
            public float Y { get; set; }     
            public float W { get; set; }     
            public float H { get; set; }     
        }

        private readonly string _tempPath;
        private readonly OcrDatabase _db;
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _processCpuCounter;
        private bool _isRunning;
        private OcrEngine _ocrEngine;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly ConcurrentQueue<(Bitmap Image, long Timestamp)> _memoryQueue = new ConcurrentQueue<(Bitmap, long)>();

        public bool EnableOCR { get; set; } = true;
        public bool EnableTextHighlighting { get; set; } = true;

        public OcrService(string tempPath, OcrDatabase db)
        {
            _tempPath = tempPath;
            _db = db;
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            
            try
            {
                string processName = Process.GetCurrentProcess().ProcessName;
                _processCpuCounter = new PerformanceCounter("Process", "% Processor Time", processName);
            }
            catch { } 
            
            if (!Directory.Exists(_tempPath))
            {
                Directory.CreateDirectory(_tempPath);
            }
            
            InitializeOcr();
        }

        public void EnqueueImage(Bitmap image, long timestamp)
        {
            if (_memoryQueue.Count > 10)
            {
                try
                {
                    string filePath = Path.Combine(_tempPath, timestamp + ".jpg");
                    image.Save(filePath, ImageFormat.Jpeg);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("OcrService.Spill", ex);
                }
                finally
                {
                    image.Dispose();
                }
            }
            else
            {
                _memoryQueue.Enqueue((image, timestamp));
            }
            SignalNewWork();
        }

        private void InitializeOcr()
        {
            try 
            {
                var available = OcrEngine.AvailableRecognizerLanguages;
                string logMsg = $"Available OCR Languages: {string.Join(", ", available.Select(l => l.LanguageTag))}\r\n";

                if (_ocrEngine == null)
                {
                    try {
                        var ukLang = new Language("uk-UA");
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(ukLang);
                        if (_ocrEngine != null) logMsg += "Selected: Windows OCR (uk-UA)\r\n";
                    } catch {}
                }
                
                if (_ocrEngine == null)
                {
                    var uk = available.FirstOrDefault(l => l.LanguageTag.ToLower().Contains("uk"));
                    if (uk != null) {
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(uk);
                        if (_ocrEngine != null) logMsg += $"Selected: Windows OCR ({uk.LanguageTag})\r\n";
                    }
                }

                if (_ocrEngine == null)
                {
                    var ru = available.FirstOrDefault(l => l.LanguageTag.ToLower().Contains("ru"));
                    if (ru != null) {
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(ru);
                        if (_ocrEngine != null) logMsg += $"Selected: Windows OCR ({ru.LanguageTag}) - Fallback\r\n";
                    }
                }

                if (_ocrEngine == null)
                {
                    try {
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
                    } catch {}
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("OcrService.Init", ex);
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            Task.Run(ProcessQueueLoop);
        }

        public void Stop()
        {
            _isRunning = false;
            _signal.Release();
        }

        public void SignalNewWork()
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }

        private string _lastText = "";

        private async Task ProcessQueueLoop()
        {
            int loopCount = 0;
            while (_isRunning)
            {
                try
                {
                    if (!EnableOCR)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    if (_memoryQueue.TryDequeue(out var queueItem))
                    {
                        using (var bmp = queueItem.Image)
                        {
                            var result = await RecognizeBitmapAsync(bmp);
                            ProcessOcrResult(queueItem.Timestamp, result);
                        }
                        continue; 
                    }

                    loopCount++;
                    
                    if (loopCount % 300 == 0)
                    {
                        await RescueOrphanedFiles();
                    }

                    float totalCpu = _cpuCounter.NextValue();
                    float otherCpu = totalCpu;

                    if (_processCpuCounter != null)
                    {
                        float myCpu = _processCpuCounter.NextValue() / Environment.ProcessorCount;
                        otherCpu = totalCpu - myCpu;
                        if (otherCpu < 0) otherCpu = 0;
                    }
                    
                    if (otherCpu > 40)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    var unprocessed = _db.GetUnprocessedFrames();
                    if (unprocessed.Count == 0)
                    {
                        await _signal.WaitAsync(2000);
                        continue;
                    }

                    foreach (var timestamp in unprocessed)
                    {
                        if (_memoryQueue.Count > 0) break;    

                        if (!_isRunning) break;

                        float currentTotal = _cpuCounter.NextValue();
                        float currentOther = currentTotal;
                        if (_processCpuCounter != null)
                        {
                            float currentMy = _processCpuCounter.NextValue() / Environment.ProcessorCount;
                            currentOther = currentTotal - currentMy;
                            if (currentOther < 0) currentOther = 0;
                        }

                        if (currentOther > 40)
                        {
                            await Task.Delay(1000);
                            break;      
                        }

                        string filePath = Path.Combine(_tempPath, timestamp + ".jpg");
                        if (File.Exists(filePath))
                        {
                            var result = await PerformOcrAsync(filePath);
                            ProcessOcrResult(timestamp, result);
                            try { File.Delete(filePath); } catch { }
                        }
                        else
                        {
                            _db.MarkAsProcessed(timestamp, "", null, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("OcrService.Loop", ex);
                    await Task.Delay(5000);
                }
            }
        }

        private void ProcessOcrResult(long timestamp, (string Text, byte[] Data) result)
        {
            bool enableSearch = AdvancedSettings.Instance.EnableTextSearch;
            bool enableCoords = this.EnableTextHighlighting;

            if (!enableSearch && !enableCoords)
            {
                _db.MarkAsProcessed(timestamp, "", null, false);
                return;
            }

            bool isDuplicate = false;
            if (EnableOCR)
            {
                double similarity = CalculateSimilarity(_lastText, result.Text);
                if (similarity > AdvancedSettings.Instance.OcrDuplicateThreshold)    
                {
                    isDuplicate = true;
                }
                else
                {
                    _lastText = result.Text;
                }
            }

            byte[] dataToSave = (enableCoords && !isDuplicate) ? result.Data : null;
            string textToSave = enableSearch ? result.Text : "";
            
            _db.MarkAsProcessed(timestamp, textToSave, dataToSave, isDuplicate);
        }

        private async Task<(string Text, byte[] Data)> PerformOcrAsync(string imagePath)
        {
            if (_ocrEngine == null) return ("", null);

            try
            {
                using (var originalBitmap = new Bitmap(imagePath))
                {
                    return await RecognizeBitmapAsync(originalBitmap);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("OcrService.PerformOcr", ex);
                return ("", null);
            }
        }

        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            
            if (Math.Abs(s1.Length - s2.Length) > Math.Max(s1.Length, s2.Length) * 0.1) return 0.0;

            int dist = LevenshteinDistance(s1, s2);
            int maxLen = Math.Max(s1.Length, s2.Length);
            if (maxLen == 0) return 1.0;
            
            return 1.0 - (double)dist / maxLen;
        }

        private int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private async Task<(string Text, byte[] Data)> RecognizeBitmapAsync(Bitmap originalBitmap)
        {
            if (_ocrEngine == null) return ("", null);
            try
            {
                double scale = AdvancedSettings.Instance.OcrScaleFactor;
                int newWidth = (int)(originalBitmap.Width * scale);
                int newHeight = (int)(originalBitmap.Height * scale);

                using (var scaledBitmap = new Bitmap(newWidth, newHeight))
                {
                    using (var g = Graphics.FromImage(scaledBitmap))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;

                        g.DrawImage(originalBitmap, 0, 0, newWidth, newHeight);
                    }

                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        using (var ms = new MemoryStream()) 
                        {
                            scaledBitmap.Save(ms, ImageFormat.Png);
                            ms.Position = 0;   
                            
                            await ms.CopyToAsync(stream.AsStreamForWrite());
                            await stream.FlushAsync();
                        }
                        stream.Seek(0);
                        
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        using (var softwareBitmap = await decoder.GetSoftwareBitmapAsync())
                        {
                            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
                            
                            var wordsList = new List<WordData>();
                            foreach (var line in ocrResult.Lines)
                            {
                                foreach (var word in line.Words)
                                {
                                    wordsList.Add(new WordData
                                    {
                                        T = word.Text,
                                        X = (float)word.BoundingRect.X / newWidth,
                                        Y = (float)word.BoundingRect.Y / newHeight,
                                        W = (float)word.BoundingRect.Width / newWidth,
                                        H = (float)word.BoundingRect.Height / newHeight
                                    });
                                }
                            }

                            byte[] compressedData = null;
                            if (wordsList.Count > 0)
                            {
                                try 
                                {
                                    compressedData = BinaryCoordinatesPacker.Pack(wordsList);
                                }
                                catch (Exception ex)
                                {
                                    DebugLogger.LogError("OcrService.PackCoordinates", ex);
                                }
                            }

                            return (ocrResult.Text, compressedData);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("OcrService.RecognizeBitmap", ex);
                return ("", null);
            }
        }

        private async Task RescueOrphanedFiles()
        {
            try
            {
                var files = Directory.GetFiles(_tempPath, "*.jpg");
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (long.TryParse(fileName, out long timestamp))
                    {
                        _db.AddFrame(timestamp, "Unknown (Rescued)");
                    }
                }
            }
            catch {       }
        }
    }
}