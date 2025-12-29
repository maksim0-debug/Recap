using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class ScreenshotService : IDisposable
    {
        public AppSettings Settings { get; set; }
        private static readonly ImageCodecInfo JpegEncoder;
        private static readonly EncoderParameters EncoderParams;
        private Bitmap _reusableBitmap;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, uint nFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;
        private const uint PW_CLIENTONLY = 0x1;
        private const uint PW_RENDERFULLCONTENT = 0x2;
        static ScreenshotService()
        {
            JpegEncoder = ImageCodecInfo.GetImageDecoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

            EncoderParams = new EncoderParameters(1);
            EncoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);    
        }

        public void Dispose()
        {
            _reusableBitmap?.Dispose();
            _reusableBitmap = null;
        }

        public Task<(byte[] JpegBytes, byte[] NewHash, int IntervalMs)> TakeScreenshotAsync(byte[] previousHash, string saveFullResPath = null)
        {
            return Task.Run(() =>
            {
                int currentInterval = this.Settings.IntervalMs;
                int blindZone = this.Settings.BlindZone;
                long quality = this.Settings.JpegQuality;

                int captureX = 0, captureY = 0, captureWidth = 0, captureHeight = 0;

                if (this.Settings.MonitorDeviceName == "AllScreens")
                {
                    captureX = SystemInformation.VirtualScreen.X;
                    captureY = SystemInformation.VirtualScreen.Y;
                    captureWidth = SystemInformation.VirtualScreen.Width;
                    captureHeight = SystemInformation.VirtualScreen.Height;
                }
                else
                {
                    Screen screenToCapture = Screen.PrimaryScreen;
                    if (!string.IsNullOrEmpty(this.Settings.MonitorDeviceName))
                    {
                        foreach (var s in Screen.AllScreens)
                        {
                            if (s.DeviceName == this.Settings.MonitorDeviceName)
                            {
                                screenToCapture = s;
                                break;
                            }
                        }
                    }
                    captureX = screenToCapture.Bounds.X;
                    captureY = screenToCapture.Bounds.Y;
                    captureWidth = screenToCapture.Bounds.Width;
                    captureHeight = screenToCapture.Bounds.Height;
                }

                try
                {
                    if (_reusableBitmap == null || _reusableBitmap.Width != captureWidth || _reusableBitmap.Height != captureHeight)
                    {
                        _reusableBitmap?.Dispose();
                        _reusableBitmap = new Bitmap(captureWidth, captureHeight, PixelFormat.Format32bppRgb);
                    }

                    var fullBmp = _reusableBitmap;

                    using (Graphics g = Graphics.FromImage(fullBmp))
                    {
                        IntPtr hdcDest = g.GetHdc();
                        
                        IntPtr hdcSrc = GetDC(IntPtr.Zero);

                        try
                        {
                            BitBlt(hdcDest, 0, 0, fullBmp.Width, fullBmp.Height, hdcSrc, captureX, captureY, SRCCOPY | CAPTUREBLT);
                        }
                        finally
                        {
                            g.ReleaseHdc(hdcDest);
                            ReleaseDC(IntPtr.Zero, hdcSrc);
                        }
                    }

                    try
                    {
                        IntPtr hWnd = GetForegroundWindow();
                        if (hWnd != IntPtr.Zero)
                        {
                            RECT clientRect;
                            if (GetClientRect(hWnd, out clientRect))
                            {
                                int width = clientRect.Right - clientRect.Left;
                                int height = clientRect.Bottom - clientRect.Top;

                                if (width > 0 && height > 0)
                                {
                                    POINT screenPos = new POINT { X = 0, Y = 0 };
                                    ClientToScreen(hWnd, ref screenPos);

                                    Rectangle checkRect = new Rectangle(screenPos.X, screenPos.Y, width, height);
                                    
                                    if (Screen.PrimaryScreen.Bounds.IntersectsWith(checkRect))
                                    {
                                        if (IsAreaBlack(fullBmp, checkRect))
                                        {
                                            var fallbackResult = CaptureWindowFallback(hWnd);
                                            if (fallbackResult != null)
                                            {
                                                using (var winBmp = fallbackResult.Value.Item1)
                                                {
                                                    using (var g = Graphics.FromImage(fullBmp))
                                                    {
                                                        g.DrawImage(winBmp, fallbackResult.Value.Item2);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogError("ScreenshotService.Fallback", ex);
                    }
                    Rectangle hashRect = new Rectangle(0, 0, fullBmp.Width, fullBmp.Height - blindZone);
                    byte[] currentHash = GetDifferenceHash1024(fullBmp, hashRect);

                    if (previousHash != null && previousHash.Length > 0)
                    {
                        if (currentHash.SequenceEqual(previousHash))
                        {
                            return ((byte[])null, currentHash, currentInterval);
                        }

                        if (this.Settings.MotionThreshold > 0)
                        {
                            int difference = GetHammingDistance(previousHash, currentHash);
                            int thresholdBits = (int)(1024 * (this.Settings.MotionThreshold / 100.0));
                            
                            if (difference <= thresholdBits)
                            {
                                return ((byte[])null, currentHash, currentInterval);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(saveFullResPath))
                    {
                        try
                        {
                            var fullQualityParams = new EncoderParameters(1);
                            fullQualityParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
                            fullBmp.Save(saveFullResPath, JpegEncoder, fullQualityParams);
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogError("ScreenshotService.SaveFullRes", ex);
                        }
                    }

                    float ratio = (float)Settings.TargetWidth / fullBmp.Width;
                    int newWidth = Settings.TargetWidth;
                    int newHeight = (int)(fullBmp.Height * ratio);

                    using (var resizedBmp = new Bitmap(newWidth, newHeight))
                    {
                        using (var g = Graphics.FromImage(resizedBmp))
                        {
                            g.InterpolationMode = InterpolationMode.Bilinear;    
                            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                            g.CompositingQuality = CompositingQuality.HighSpeed;
                            g.DrawImage(fullBmp, 0, 0, newWidth, newHeight);
                        }

                        EncoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

                        using (var ms = new MemoryStream())
                        {
                            resizedBmp.Save(ms, JpegEncoder, EncoderParams);
                            return (ms.ToArray(), currentHash, currentInterval);
                        }
                    }
                }
                catch (Exception)
                {
                    return ((byte[])null, previousHash, currentInterval);
                }
            });
        }

        private bool IsAreaBlack(Bitmap bmp, Rectangle rect)
        {
            int centerX = rect.X + rect.Width / 2;
            int centerY = rect.Y + rect.Height / 2;

            if (centerX < 0 || centerX >= bmp.Width || centerY < 0 || centerY >= bmp.Height) return false;

            Color c = bmp.GetPixel(centerX, centerY);
            if (c.R != 0 || c.G != 0 || c.B != 0) return false;

            return true;
        }

        private bool IsBitmapBlack(Bitmap bmp)
        {
            if (bmp.Width == 0 || bmp.Height == 0) return true;
            Color c = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            return c.R == 0 && c.G == 0 && c.B == 0;
        }

        private (Bitmap, Point)? CaptureWindowFallback(IntPtr hWnd)
        {
            RECT rect;
            if (!GetClientRect(hWnd, out rect)) return null;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;

            POINT screenPos = new POINT { X = 0, Y = 0 };
            ClientToScreen(hWnd, ref screenPos);

            Bitmap bmp = new Bitmap(width, height);
            bool success = false;

            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    success = PrintWindow(hWnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            if (success && !IsBitmapBlack(bmp))
            {
                return (bmp, new Point(screenPos.X, screenPos.Y));
            }

            StringBuilder className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            string cls = className.ToString();

            if (cls.Contains("Chrome") || cls.Contains("Edge") || cls.Contains("Electron") || cls.Contains("WwBrowser"))
            {
                IntPtr childWnd = IntPtr.Zero;
                EnumChildWindows(hWnd, (h, p) =>
                {
                    StringBuilder childClass = new StringBuilder(256);
                    GetClassName(h, childClass, childClass.Capacity);
                    if (childClass.ToString() == "Chrome_RenderWidgetHostHWND")
                    {
                        childWnd = h;
                        return false;  
                    }
                    return true;  
                }, IntPtr.Zero);

                if (childWnd != IntPtr.Zero)
                {
                    bmp.Dispose();

                    if (GetClientRect(childWnd, out rect))
                    {
                        width = rect.Right - rect.Left;
                        height = rect.Bottom - rect.Top;
                        if (width > 0 && height > 0)
                        {
                            Bitmap childBmp = new Bitmap(width, height);
                            POINT childPos = new POINT { X = 0, Y = 0 };
                            ClientToScreen(childWnd, ref childPos);

                            using (Graphics g = Graphics.FromImage(childBmp))
                            {
                                IntPtr hdc = g.GetHdc();
                                try
                                {
                                    PrintWindow(childWnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                                }
                                finally
                                {
                                    g.ReleaseHdc(hdc);
                                }
                            }
                            return (childBmp, new Point(childPos.X, childPos.Y));
                        }
                    }
                }
            }

            bmp.Dispose();
            return null;
        }

        private unsafe byte[] GetDifferenceHash1024(Bitmap bmp, Rectangle rect)
        {
            using (var smallBmp = new Bitmap(33, 32))
            {
                using (var g = Graphics.FromImage(smallBmp))
                {
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(bmp, new Rectangle(0, 0, 33, 32), rect, GraphicsUnit.Pixel);
                }

                byte[] hash = new byte[128];      

                BitmapData data = smallBmp.LockBits(new Rectangle(0, 0, 33, 32), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte* ptr = (byte*)data.Scan0;
                    int stride = data.Stride;

                    for (int y = 0; y < 32; y++)
                    {
                        byte* row = ptr + (y * stride);
                        for (int x = 0; x < 32; x++)
                        {
                            byte b1 = row[x * 4];
                            byte g1 = row[x * 4 + 1];
                            byte r1 = row[x * 4 + 2];
                            long bright1 = r1 + g1 + b1;    

                            byte b2 = row[(x + 1) * 4];
                            byte g2 = row[(x + 1) * 4 + 1];
                            byte r2 = row[(x + 1) * 4 + 2];
                            long bright2 = r2 + g2 + b2;

                            if (bright1 > bright2)
                            {
                                int bitIndex = y * 32 + x;
                                int byteIndex = bitIndex / 8;
                                int bitInByte = bitIndex % 8;
                                hash[byteIndex] |= (byte)(1 << bitInByte);
                            }
                        }
                    }
                }
                finally
                {
                    smallBmp.UnlockBits(data);
                }

                return hash;
            }
        }

        private int GetHammingDistance(byte[] hash1, byte[] hash2)
        {
            if (hash1 == null || hash2 == null || hash1.Length != hash2.Length)
                return int.MaxValue;

            int dist = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                byte xor = (byte)(hash1[i] ^ hash2[i]);
                while (xor != 0)
                {
                    dist++;
                    xor &= (byte)(xor - 1);
                }
            }
            return dist;
        }
    }
}