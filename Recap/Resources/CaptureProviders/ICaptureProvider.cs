using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap.Resources.CaptureProviders
{
    public class CaptureResult : IDisposable
    {
        public Bitmap Bitmap { get; set; }
        public bool Success { get; set; }
        public bool DeviceLost { get; set; }          

        public void Dispose()
        {
        }
    }

    public interface ICaptureProvider : IDisposable
    {
        string Name { get; }
        bool IsAvailable();

        bool Initialize(Screen screen);

        bool InitializeForWindow(IntPtr hWnd);

        CaptureResult Capture(Bitmap targetBitmap = null);
    }
}
