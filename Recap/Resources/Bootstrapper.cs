using System;
using System.IO;
using System.Threading.Tasks;

namespace Recap
{
    public class Bootstrapper
    {
        public MainForm CreateMainForm(bool autoStart)
        {
            var settingsManager = new SettingsManager();
            var screenshotService = new ScreenshotService();
            var iconManager = new IconManager();
            
            var settings = settingsManager.Load();
            screenshotService.Settings = settings;
            iconManager.DisableVideoPreviews = settings.DisableVideoPreviews;

            FrameRepository frameRepository = new FrameRepository(settings.StoragePath);
            OcrDatabase ocrDb = null;
            OcrService ocrService = null;

            if (!string.IsNullOrEmpty(settings.StoragePath))
            {
                try
                {
                    ocrDb = new OcrDatabase(settings.StoragePath);
                    iconManager.Database = ocrDb;
                    string tempOcrPath = Path.Combine(settings.StoragePath, "tempOCR");
                    ocrService = new OcrService(tempOcrPath, ocrDb);
                    ocrService.EnableOCR = settings.EnableOCR;
                    ocrService.EnableTextHighlighting = settings.EnableTextHighlighting;
                    ocrService.Start();

                    frameRepository.SetOcrDatabase(ocrDb);
                    
                    Task.Run(async () =>
                    {
                        try
                        {
                            await frameRepository.SyncAllDaysToDbAsync();
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogError("Bootstrapper.SyncAllDays", ex);
                        }
                    });
                }
                catch (Exception ex) 
                { 
                    DebugLogger.LogError("Bootstrapper.InitOCR", ex); 
                }
            }

            return new MainForm(
                autoStart,
                settingsManager,
                settings,
                screenshotService,
                iconManager,
                frameRepository,
                ocrDb,
                ocrService
            );
        }
    }
}
