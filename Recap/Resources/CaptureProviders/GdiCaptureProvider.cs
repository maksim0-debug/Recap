using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Recap.Resources.CaptureProviders
{
    public class GdiCaptureProvider : ICaptureProvider
    {
        private Rectangle _captureBounds;
        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        public string Name => "GDI+";

        public bool IsAvailable() => true;

        public bool Initialize(Screen screen)
        {
            if (screen == null)
            {
                _captureBounds = SystemInformation.VirtualScreen;
            }
            else
            {
                _captureBounds = screen.Bounds;
            }
            return true;
        }

        public bool InitializeForWindow(IntPtr hWnd)
        {
            return false;
        }

        public void SetBounds(Rectangle bounds)
        {
            _captureBounds = bounds;
        }

        public CaptureResult Capture(Bitmap targetBitmap = null)
        {
            if (_captureBounds.Width <= 0 || _captureBounds.Height <= 0)
                return new CaptureResult { Success = false };

            int width = _captureBounds.Width;
            int height = _captureBounds.Height;
            int x = _captureBounds.X;
            int y = _captureBounds.Y;

            if (targetBitmap == null)
            {
                targetBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            }

            using (Graphics g = Graphics.FromImage(targetBitmap))
            {
                IntPtr hdcDest = g.GetHdc();
                IntPtr hdcSrc = GetDC(IntPtr.Zero);
                try
                {
                    bool result = BitBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, SRCCOPY | CAPTUREBLT);
                    return new CaptureResult { Bitmap = targetBitmap, Success = result };
                }
                catch
                {
                    return new CaptureResult { Success = false };
                }
                finally
                {
                    g.ReleaseHdc(hdcDest);
                    ReleaseDC(IntPtr.Zero, hdcSrc);
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
