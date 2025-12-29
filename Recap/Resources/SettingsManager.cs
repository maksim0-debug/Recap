using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace Recap
{
    public class SettingsManager
    {
        private const string RegistryPath = "SOFTWARE\\Recap";
        private const string RunRegistryPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "Recap";

        private static readonly string DefaultStoragePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Recap");

        public AppSettings Load()
        {
            var settings = new AppSettings
            {
                StoragePath = DefaultStoragePath,
                JpegQuality = 45L,
                IntervalMs = 3000,
                BlindZone = 30,
                TargetWidth = 1280,
                Language = "en",
                StartWithWindows = false,
                GlobalSearch = false,    
                ShowFrameCount = false,    
                EnableOCR = true,    
                EnableTextHighlighting = true,    
                DisableVideoPreviews = false,    
                MotionThreshold = 1    
            };

            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false))
                {
                    if (rk != null)
                    {
                        settings.StoragePath = (rk.GetValue("StoragePath") as string) ?? settings.StoragePath;
                        settings.Language = (rk.GetValue("Language") as string) ?? settings.Language;

                        int qualityIndex = Convert.ToInt32(rk.GetValue("QualityIndex", 1));
                        settings.JpegQuality = GetQualityValueFromIndex(qualityIndex);
                        settings.IntervalMs = Convert.ToInt32(rk.GetValue("Interval", settings.IntervalMs));
                        settings.BlindZone = Convert.ToInt32(rk.GetValue("BlindZone", settings.BlindZone));
                        settings.TargetWidth = Convert.ToInt32(rk.GetValue("TargetWidth", settings.TargetWidth));

                        settings.GlobalSearch = Convert.ToBoolean(rk.GetValue("GlobalSearch", settings.GlobalSearch));
                        settings.ShowFrameCount = Convert.ToBoolean(rk.GetValue("ShowFrameCount", settings.ShowFrameCount));
                        settings.EnableOCR = Convert.ToBoolean(rk.GetValue("EnableOCR", settings.EnableOCR));
                        settings.EnableTextHighlighting = Convert.ToBoolean(rk.GetValue("EnableTextHighlighting", settings.EnableTextHighlighting));
                        settings.DisableVideoPreviews = Convert.ToBoolean(rk.GetValue("DisableVideoPreviews", settings.DisableVideoPreviews));
                        settings.MotionThreshold = Convert.ToInt32(rk.GetValue("MotionThreshold", settings.MotionThreshold));
                        settings.MonitorDeviceName = (rk.GetValue("MonitorDeviceName") as string);
                        settings.ConverterLastPath = (rk.GetValue("ConverterLastPath") as string);
                    }
                }

                using (RegistryKey rkRun = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false))
                {
                    settings.StartWithWindows = (rkRun?.GetValue(AppName) != null);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("SettingsManager.Load", ex);
            }

            return settings;
        }

        public void Save(AppSettings settings)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    rk.SetValue("StoragePath", settings.StoragePath ?? "", RegistryValueKind.String);
                    rk.SetValue("QualityIndex", GetQualityIndexFromValue(settings.JpegQuality), RegistryValueKind.DWord);
                    rk.SetValue("Interval", settings.IntervalMs, RegistryValueKind.DWord);
                    rk.SetValue("BlindZone", settings.BlindZone, RegistryValueKind.DWord);
                    rk.SetValue("TargetWidth", settings.TargetWidth, RegistryValueKind.DWord);
                    rk.SetValue("Language", settings.Language ?? "en", RegistryValueKind.String);
                    
                    rk.SetValue("GlobalSearch", settings.GlobalSearch, RegistryValueKind.DWord);     
                    rk.SetValue("ShowFrameCount", settings.ShowFrameCount, RegistryValueKind.DWord);
                    rk.SetValue("EnableOCR", settings.EnableOCR, RegistryValueKind.DWord);
                    rk.SetValue("EnableTextHighlighting", settings.EnableTextHighlighting, RegistryValueKind.DWord);
                    rk.SetValue("DisableVideoPreviews", settings.DisableVideoPreviews, RegistryValueKind.DWord);
                    rk.SetValue("MotionThreshold", settings.MotionThreshold, RegistryValueKind.DWord);
                    rk.SetValue("MonitorDeviceName", settings.MonitorDeviceName ?? "", RegistryValueKind.String);
                }

                using (RegistryKey rkRun = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
                {
                    if (settings.StartWithWindows)
                    {
                        string exePath = $"\"{Application.ExecutablePath}\" /autostart";
                        rkRun.SetValue(AppName, exePath, RegistryValueKind.String);
                    }
                    else
                    {
                        rkRun.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("SettingsManager.Save", ex);
            }
        }

        public void SaveConverterPath(string path)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    rk.SetValue("ConverterLastPath", path ?? "", RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("SettingsManager.SaveConverterPath", ex);
            }
        }

        private int GetQualityIndexFromValue(long value)
        {
            if (value <= 25) return 0;
            if (value <= 45) return 1;
            if (value <= 75) return 2;
            return 3;
        }

        private long GetQualityValueFromIndex(int index)
        {
            switch (index)
            {
                case 0: return 25L;
                case 1: return 45L;
                case 2: return 75L;
                case 3: return 95L;
                default: return 45L;
            }
        }
    }
}    