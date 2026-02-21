namespace Recap
{
    public enum CaptureMode
    {
        Auto,
        ForceDXGI,
        ForceWGC,
        ForceGDI
    }

    public class AppSettings
    {
        public string StoragePath { get; set; }
        public long JpegQuality { get; set; }
        public int IntervalMs { get; set; }
        public int BlindZone { get; set; }
        public int TargetWidth { get; set; }
        public string Language { get; set; }
        public bool StartWithWindows { get; set; }

        public bool GlobalSearch { get; set; }

        public bool ShowFrameCount { get; set; }

        public bool EnableOCR { get; set; }

        public bool EnableTextHighlighting { get; set; }

        public bool DisableVideoPreviews { get; set; }

        public string MonitorDeviceName { get; set; }

        public string MonitorDeviceId { get; set; }

        public int MotionThreshold { get; set; }

        public string ConverterLastPath { get; set; }

        public bool SuppressExtensionWarning { get; set; }

        public bool UseWindowsGraphicsCapture { get; set; } = true;

        public System.Collections.Specialized.StringCollection OcrBlacklist { get; set; } = new System.Collections.Specialized.StringCollection();

        public CaptureMode CaptureMode { get; set; } = CaptureMode.Auto;

        public AppSettings Clone()
        {
            return (AppSettings)this.MemberwiseClone();
        }
    }
}    