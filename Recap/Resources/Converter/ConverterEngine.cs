using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using Recap; 

namespace RecapConverter
{
    public class ConverterEngine
    {
        public event Action<int, string> ProgressChanged;
        public event Action<string> LogMessage;

        private readonly FrameReader _reader = new FrameReader();

        public void ConvertDay(string schPath, int targetWidth, int targetHeight, int fps, bool useNvenc, int quality)
        {
            string baseName = Path.GetFileNameWithoutExtension(schPath);
            string outputVideo = Path.Combine(Path.GetDirectoryName(schPath), $"{baseName}.mkv");
            string outputCsv = Path.Combine(Path.GetDirectoryName(schPath), $"{baseName}.csv");

            LogMessage?.Invoke(Recap.Localization.Format("convLogProcessing", baseName));

            List<FrameIndex> frames = _reader.LoadIndices(schPath);
            if (frames == null || frames.Count == 0)
            {
                LogMessage?.Invoke(Recap.Localization.Get("convLogNoFrames"));
                return;
            }

            LogMessage?.Invoke(Recap.Localization.Format("convLogStartFfmpeg", frames.Count));

            string inputArgs = $"-y -f rawvideo -pixel_format bgr24 -video_size {targetWidth}x{targetHeight} -framerate {fps} -i - ";
            string encoderArgs;

            if (useNvenc)
            {
                encoderArgs = $"-c:v hevc_nvenc " +
                              $"-preset p7 " +
                              $"-rc constqp " +
                              $"-qp {quality} " +
                              $"-b:v 0 " +
                              $"-pix_fmt yuv420p ";
            }
            else
            {
                encoderArgs = $"-c:v libx265 " +
                              $"-preset medium " +
                              $"-crf {quality} " +
                              $"-pix_fmt yuv420p ";
            }

            string gopArgs = $"-g {fps * 300} ";
            string fullArgs = $"-loglevel warning {inputArgs} {encoderArgs} {gopArgs} \"{outputVideo}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = fullArgs,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var csvWriter = new StreamWriter(outputCsv, false, Encoding.UTF8))
            using (var ffmpeg = new Process())
            {
                ffmpeg.StartInfo = processStartInfo;

                ffmpeg.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        System.Diagnostics.Debug.WriteLine($"FFmpeg: {e.Data}");
                    }
                };

                ffmpeg.Start();
                ffmpeg.BeginErrorReadLine();

                csvWriter.WriteLine("FrameNumber;Ticks;AppName;Site;TimeStr");

                long videoFrameCounter = 0;
                var inputStream = ffmpeg.StandardInput.BaseStream;

                for (int i = 0; i < frames.Count; i++)
                {
                    FrameIndex currentFrame = frames[i];
                    FrameIndex? nextFrame = (i < frames.Count - 1) ? (FrameIndex?)frames[i + 1] : null;

                    byte[] imgBytes = _reader.ReadFrameData(schPath, currentFrame);
                    if (imgBytes == null) continue;

                    byte[] canvasBytes = null;

                    try
                    {
                        using (var ms = new MemoryStream(imgBytes))
                        using (var srcBmp = Image.FromStream(ms))
                        using (var canvas = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb))
                        {
                            using (var g = Graphics.FromImage(canvas))
                            {
                                g.Clear(Color.Black);

                                int x = (targetWidth - srcBmp.Width) / 2;
                                int y = (targetHeight - srcBmp.Height) / 2;

                                if (srcBmp.Width > targetWidth || srcBmp.Height > targetHeight)
                                {
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    float ratio = Math.Min((float)targetWidth / srcBmp.Width, (float)targetHeight / srcBmp.Height);
                                    int newW = (int)(srcBmp.Width * ratio);
                                    int newH = (int)(srcBmp.Height * ratio);
                                    x = (targetWidth - newW) / 2;
                                    y = (targetHeight - newH) / 2;
                                    g.DrawImage(srcBmp, x, y, newW, newH);
                                }
                                else
                                {
                                    g.DrawImage(srcBmp, x, y, srcBmp.Width, srcBmp.Height);
                                }
                            }
                            canvasBytes = GetRawBytes(canvas);
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    int repeatCount = 1;
                    if (nextFrame.HasValue)
                    {
                        TimeSpan diff = nextFrame.Value.GetTime() - currentFrame.GetTime();
                        if (diff.TotalSeconds < 60 && diff.TotalSeconds > 0)
                        {
                            repeatCount = (int)Math.Round(diff.TotalSeconds * fps);
                            if (repeatCount < 1) repeatCount = 1;
                        }
                    }

                    string timeStr = currentFrame.GetTime().ToString("HH:mm:ss");

                    string exeName = currentFrame.AppName;
                    string siteName = "";
                    int pipeIndex = exeName.IndexOf('|');
                    if (pipeIndex > 0)
                    {
                        siteName = exeName.Substring(pipeIndex + 1);
                        exeName = exeName.Substring(0, pipeIndex);
                    }

                    csvWriter.WriteLine($"{videoFrameCounter};{currentFrame.TimestampTicks};{exeName};{siteName};{timeStr}");

                    for (int r = 0; r < repeatCount; r++)
                    {
                        try
                        {
                            inputStream.Write(canvasBytes, 0, canvasBytes.Length);
                            videoFrameCounter++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage?.Invoke(Recap.Localization.Format("convLogWriteErr", ex.Message));
                            i = frames.Count;
                            break;
                        }
                    }

                    if (i % 20 == 0)
                    {
                        int percent = (int)((double)i / frames.Count * 100);
                        ProgressChanged?.Invoke(percent, Recap.Localization.Format("convProgress", i, frames.Count, videoFrameCounter));
                    }
                }

                inputStream.Flush();
                inputStream.Close();
                ffmpeg.WaitForExit();
            }

            LogMessage?.Invoke(Recap.Localization.Format("convLogFinished", Path.GetFileName(outputVideo)));
        }

        private byte[] GetRawBytes(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bmp.Height;
                byte[] rgbValues = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, rgbValues, 0, bytes);
                return rgbValues;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }
}