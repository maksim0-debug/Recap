using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace Recap
{
    public class AdvancedSettings
    {
        private static AdvancedSettings _instance;
        private static readonly string ConfigPath = Path.Combine(Application.LocalUserAppDataPath, "advanced.config");

        public static AdvancedSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    Load();
                }
                return _instance;
            }
        }

        public static void Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };
                    _instance = JsonSerializer.Deserialize<AdvancedSettings>(json, options);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load advanced settings: {ex.Message}");
                }
            }
            
            if (_instance == null)
            {
                _instance = new AdvancedSettings();
                try 
                {
                   _instance.Save();    
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create initial settings file: {ex.Message}");
                }
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("AdvancedSettings.Save", ex);
                throw;
            }
        }

        [Category("1. Database Performance")]
        [Description("Size of the memory cache in pages. Default: 10000.")]
        [DefaultValue(10000)]
        public int DbCacheSize { get; set; } = 10000;

        [Category("1. Database Performance")]
        [Description("Maximum size of memory-mapped I/O in bytes. Default: 2147483648 (2GB).")]
        [DefaultValue(2147483648)]
        public long DbMmapSize { get; set; } = 2147483648;

        [Category("1. Database Performance")]
        [Description("Number of threads for SQLite operations. Default: 4.")]
        [DefaultValue(4)]
        public int DbThreads { get; set; } = 4;

        [Category("4. OCR")]
        [Description("Use hybrid OCR pipeline (RAM/SSD) to reduce disk wear. Default: true.")]
        [DefaultValue(true)]
        public bool UseHybridOcr { get; set; } = true;

        [Category("4. OCR")]
        [Description("CPU threshold (%) to switch to disk-based buffering. Default: 40.")]
        [DefaultValue(40)]
        public int HighLoadCpuThreshold { get; set; } = 40;

        [Category("1. Database Performance")]
        [Description("Synchronization mode. NORMAL is safe, OFF is faster but risky. Default: NORMAL.")]
        [DefaultValue("NORMAL")]
        public string DbSynchronous { get; set; } = "NORMAL";

        [Category("2. OCR Tuning")]
        [Description("Scale factor for image preprocessing. Higher values improve accuracy for small text but increase CPU usage. Default: 1.5.")]
        [DefaultValue(1.5)]
        public double OcrScaleFactor { get; set; } = 1.5;

        [Category("2. OCR Tuning")]
        [Description("Similarity threshold for duplicate detection (0.0 - 1.0). Higher values mean stricter matching. Default: 0.9.")]
        [DefaultValue(0.9)]
        public double OcrDuplicateThreshold { get; set; } = 0.9;

        [Category("3. Timeline & UI")]
        [Description("Width of the timeline preview thumbnail. Default: 160.")]
        [DefaultValue(160)]
        public int PreviewWidth { get; set; } = 160;

        [Category("3. Timeline & UI")]
        [Description("Height of the timeline preview thumbnail. Default: 90.")]
        [DefaultValue(90)]
        public int PreviewHeight { get; set; } = 90;

        [Category("3. Timeline & UI")]
        [Description("UI update interval in milliseconds. Lower values mean smoother animations but higher CPU usage. Default: 30.")]
        [DefaultValue(30)]
        public int UiUpdateIntervalMs { get; set; } = 30;

        [Category("3. Timeline & UI")]
        [Description("Delay before showing the tooltip in the list view (ms). Default: 350.")]
        [DefaultValue(350)]
        public int TooltipDelayMs { get; set; } = 350;

        [Category("4. Network")]
        [Description("Port for the Browser Tracker HTTP server. Default: 19999.")]
        [DefaultValue(19999)]
        public int BrowserTrackerPort { get; set; } = 19999;

        [Category("4. Network")]
        [Description("User-Agent string for fetching icons. Default: Mozilla/5.0...")]
        [DefaultValue("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36")]
        public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";

        [Category("4. Features")]
        [Description("Enable full-text search indexing. Default: true.")]
        [DefaultValue(true)]
        public bool EnableTextSearch { get; set; } = true;

        [Category("5. Debug & Logging")]
        [Description("Enable debug logging to file. Requires restart to take full effect.")]
        [DefaultValue(false)]
        public bool EnableDebugLogging { get; set; } = false;

        [Category("5. Debug & Logging")]
        [Description("Show which capture method is being used (WGC or GDI+).")]
        [DefaultValue(false)]
        public bool ShowCaptureMethod { get; set; } = false;
    }
}
