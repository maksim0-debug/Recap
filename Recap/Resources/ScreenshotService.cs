using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Recap.Resources;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Recap
{
    public class ScreenshotService : IDisposable
    {
        public AppSettings Settings { get; set; }
        private static readonly ImageCodecInfo JpegEncoder;
        private static readonly EncoderParameters EncoderParams;
        private Bitmap _reusableBitmap;
        private Bitmap _reusableSmallBitmap;

        private bool _wgcInitialized;
        private IDirect3DDevice _device;
        private IntPtr _d3dDevicePtr;
        private IntPtr _d3dContextPtr;
        private GraphicsCaptureItem _captureItem;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private IntPtr _stagingTexturePtr;
        private Size _currentCaptureSize;
        private IntPtr _currentMonitorHandle;
        private bool _wgcFailed;

        public string LastUsedCaptureMethod { get; private set; } = "None";

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

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

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
        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        static ScreenshotService()
        {
            JpegEncoder = ImageCodecInfo.GetImageDecoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

            EncoderParams = new EncoderParameters(1);
            EncoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
        }

        public Bitmap GetLastCapturedBitmap()
        {
            return _reusableBitmap;
        }

        public void Dispose()
        {
            DisposeWgc();
            _reusableBitmap?.Dispose();
            _reusableBitmap = null;
            _reusableSmallBitmap?.Dispose();
            _reusableSmallBitmap = null;
        }

        private void DisposeWgc()
        {
            try
            {
                _session?.Dispose();
                _framePool?.Dispose();
                
                if (_stagingTexturePtr != IntPtr.Zero)
                {
                    WgcInterop.D3D11Manual.Release(_stagingTexturePtr);
                    _stagingTexturePtr = IntPtr.Zero;
                }
                
                if (_d3dDevicePtr != IntPtr.Zero)
                {
                    WgcInterop.D3D11Manual.Release(_d3dDevicePtr);
                    _d3dDevicePtr = IntPtr.Zero;
                }
                
                (_device as IDisposable)?.Dispose();
            }
            catch { }
            
            _session = null;
            _framePool = null;
            _captureItem = null;
            _device = null;
            _wgcInitialized = false;
        }

        public Task<(byte[] JpegBytes, byte[] NewHash, int IntervalMs, Bitmap FullBitmap)> TakeScreenshotAsync(byte[] previousHash, string saveFullResPath = null, bool returnFullBitmap = false)
        {
            return Task.Run(() =>
            {
                int currentInterval = this.Settings.IntervalMs;
                int blindZone = this.Settings.BlindZone;
                long quality = this.Settings.JpegQuality;

                int captureX = 0, captureY = 0, captureWidth = 0, captureHeight = 0;
                Screen screenToCapture = null;

                if (this.Settings.MonitorDeviceName == "AllScreens")
                {
                    captureX = SystemInformation.VirtualScreen.X;
                    captureY = SystemInformation.VirtualScreen.Y;
                    captureWidth = SystemInformation.VirtualScreen.Width;
                    captureHeight = SystemInformation.VirtualScreen.Height;
                }
                else
                {
                    var deviceIds = !string.IsNullOrEmpty(this.Settings.MonitorDeviceId) ? DisplayHelper.GetMonitorDeviceIds() : null;

                    if (!string.IsNullOrEmpty(this.Settings.MonitorDeviceName))
                    {
                        foreach (var s in Screen.AllScreens)
                        {
                            if (s.DeviceName == this.Settings.MonitorDeviceName)
                            {
                                if (deviceIds != null && deviceIds.ContainsKey(s.DeviceName))
                                {
                                    if (deviceIds[s.DeviceName] == this.Settings.MonitorDeviceId)
                                        screenToCapture = s;
                                }
                                else
                                {
                                    screenToCapture = s;
                                }
                                break;
                            }
                        }
                    }

                    if (screenToCapture == null && !string.IsNullOrEmpty(this.Settings.MonitorDeviceId) && deviceIds != null)
                    {
                        foreach (var s in Screen.AllScreens)
                        {
                            if (deviceIds.ContainsKey(s.DeviceName) && deviceIds[s.DeviceName] == this.Settings.MonitorDeviceId)
                            {
                                screenToCapture = s;
                                this.Settings.MonitorDeviceName = s.DeviceName;
                                break;
                            }
                        }
                    }

                    if (screenToCapture == null || screenToCapture.Bounds.Width <= 0 || screenToCapture.Bounds.Height <= 0)
                    {
                        screenToCapture = Screen.PrimaryScreen;
                    }

                    if (screenToCapture == null || screenToCapture.Bounds.Width <= 0)
                    {
                        if (Screen.AllScreens.Length > 0)
                            screenToCapture = Screen.AllScreens[0];
                        else
                            return ((byte[])null, previousHash, currentInterval, null);
                    }

                    if (screenToCapture != null && !string.IsNullOrEmpty(this.Settings.MonitorDeviceId))
                    {
                         if (deviceIds != null && deviceIds.ContainsKey(screenToCapture.DeviceName) && deviceIds[screenToCapture.DeviceName] == this.Settings.MonitorDeviceId)
                         {
                             this.Settings.MonitorDeviceName = screenToCapture.DeviceName;
                         }
                    }

                    captureX = screenToCapture.Bounds.X;
                    captureY = screenToCapture.Bounds.Y;
                    captureWidth = screenToCapture.Bounds.Width;
                    captureHeight = screenToCapture.Bounds.Height;
                }

                try
                {
                    if (captureWidth <= 0 || captureHeight <= 0)
                        return ((byte[])null, previousHash, currentInterval, null);

                    if (_reusableBitmap == null || _reusableBitmap.Width != captureWidth || _reusableBitmap.Height != captureHeight)
                    {
                        _reusableBitmap?.Dispose();
                        _reusableBitmap = new Bitmap(captureWidth, captureHeight, PixelFormat.Format32bppRgb);
                    }

                    var fullBmp = _reusableBitmap;
                    bool captured = false;

                    if (this.Settings.UseWindowsGraphicsCapture && !_wgcFailed && this.Settings.MonitorDeviceName != "AllScreens")
                    {
                        try 
                        {
                            captured = CaptureWithWgc(captureX, captureY, captureWidth, captureHeight, fullBmp);
                            if (captured)
                            {
                                LastUsedCaptureMethod = "WGC";
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogError("ScreenshotService.WgcCapture", ex);
                            _wgcFailed = true;
                            DisposeWgc();
                        }
                    }

                    if (!captured)
                    {
                        LastUsedCaptureMethod = "GDI+";
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
                            return ((byte[])null, currentHash, currentInterval, null);
                        }

                        if (this.Settings.MotionThreshold > 0)
                        {
                            int difference = GetHammingDistance(previousHash, currentHash);
                            int thresholdBits = (int)(1024 * (this.Settings.MotionThreshold / 100.0));
                            if (difference <= thresholdBits)
                            {
                                return ((byte[])null, currentHash, currentInterval, null);
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

                    Bitmap returnedBmp = null;
                    if (returnFullBitmap)
                    {
                        returnedBmp = (Bitmap)fullBmp.Clone();
                    }

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
                            return (ms.ToArray(), currentHash, currentInterval, returnedBmp);
                        }
                    }
                }
                catch (Exception)
                {
                    return ((byte[])null, previousHash, currentInterval, null);
                }
            });
        }

        private bool CaptureWithWgc(int captureX, int captureY, int captureWidth, int captureHeight, Bitmap fullBmp)
        {
            try
            {
                InitializeWgcIfNeeded();

                RECT rect = new RECT { Left = captureX, Top = captureY, Right = captureX + captureWidth, Bottom = captureY + captureHeight };
                IntPtr hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTOPRIMARY);

                if (hMonitor != _currentMonitorHandle || _session == null)
                {
                    RestartSession(hMonitor, new Size(captureWidth, captureHeight));
                }
                else if (_currentCaptureSize.Width != captureWidth || _currentCaptureSize.Height != captureHeight)
                {
                    RestartSession(hMonitor, new Size(captureWidth, captureHeight));
                }

                if (_framePool == null) return false;

                Direct3D11CaptureFrame frame = null;
                for (int i = 0; i < 5; i++)    
                {
                    frame = _framePool.TryGetNextFrame();
                    if (frame != null) break;
                    
                    System.Threading.Thread.Sleep(5); 
                }

                if (frame == null) 
                {
                    return false; 
                }

                using (frame)
                using (var surface = frame.Surface)
                {
                     var interopAccess = (WgcInterop.IDirect3DDxgiInterfaceAccess)surface;
                     var iid = WgcInterop.IID_ID3D11Texture2D;
                     IntPtr texturePtr = interopAccess.GetInterface(ref iid);
                     
                     try
                     {
                         WgcInterop.D3D11_TEXTURE2D_DESC desc = WgcInterop.D3D11Manual.GetDesc(texturePtr);
                         
                         if (_stagingTexturePtr == IntPtr.Zero)
                         {
                             CreateStagingTexture(desc);
                         }
                         else
                         {
                             var stagingDesc = WgcInterop.D3D11Manual.GetDesc(_stagingTexturePtr);
                             if (stagingDesc.Width != desc.Width || stagingDesc.Height != desc.Height)
                             {
                                 WgcInterop.D3D11Manual.Release(_stagingTexturePtr);
                                 CreateStagingTexture(desc);
                             }
                         }

                         WgcInterop.D3D11Manual.CopyResource(_d3dContextPtr, _stagingTexturePtr, texturePtr);
                         
                         WgcInterop.D3D11_MAPPED_SUBRESOURCE map = WgcInterop.D3D11Manual.Map(_d3dContextPtr, _stagingTexturePtr, 0, WgcInterop.D3D11_MAP.D3D11_MAP_READ, 0);
                         
                         try
                         {
                             BitmapData bmpData = fullBmp.LockBits(
                                 new Rectangle(0, 0, fullBmp.Width, fullBmp.Height), 
                                 ImageLockMode.WriteOnly, 
                                 PixelFormat.Format32bppRgb); 

                             try
                             {
                                 int height = Math.Min((int)desc.Height, fullBmp.Height);
                                 int width = Math.Min((int)desc.Width, fullBmp.Width);
                                 int bytesPerPixel = 4;  
                                 int copyWidth = width * bytesPerPixel;

                                 for (int y = 0; y < height; y++)
                                 {
                                     IntPtr srcRow = map.pData + (y * (int)map.RowPitch);
                                     IntPtr destRow = bmpData.Scan0 + (y * bmpData.Stride);
                                     CopyMemory(destRow, srcRow, (uint)copyWidth);
                                 }
                             }
                             finally
                             {
                                 fullBmp.UnlockBits(bmpData);
                             }
                         }
                         finally
                         {
                             WgcInterop.D3D11Manual.Unmap(_d3dContextPtr, _stagingTexturePtr, 0);
                         }
                     }
                     finally
                     {
                         WgcInterop.D3D11Manual.Release(texturePtr);
                     }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WGC Capture Error: {ex.Message}");
                throw;        
            }
            return true;
        }

        private void InitializeWgcIfNeeded()
        {
            if (_wgcInitialized) return;

            int hr = WgcInterop.D3D11CreateDevice(
                IntPtr.Zero, 
                WgcInterop.D3D_DRIVER_TYPE_HARDWARE, 
                IntPtr.Zero, 
                WgcInterop.D3D11_CREATE_DEVICE_BGRA_SUPPORT, 
                IntPtr.Zero,
                0, 
                WgcInterop.D3D11_SDK_VERSION, 
                out _d3dDevicePtr, 
                out int featureLevel, 
                out _d3dContextPtr);

            if (hr < 0 || _d3dDevicePtr == IntPtr.Zero) 
                throw new Exception("Failed to create D3D11 device");
            
            IntPtr dxgiDevice = IntPtr.Zero;
            Guid iidIDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.QueryInterface(_d3dDevicePtr, ref iidIDXGIDevice, out dxgiDevice);

             try
             {
                 IntPtr inspectableDevice;
                 uint result = WgcInterop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectableDevice);
                 if (result != 0) throw new Exception("Failed to create WinRT D3D Device");
                 _device = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectableDevice);
                 Marshal.Release(inspectableDevice);
             }
             finally
             {
                 if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
             }

            _wgcInitialized = true;
        }

        private void RestartSession(IntPtr hMonitor, Size size)
        {
            _session?.Dispose();
            _framePool?.Dispose();
            
            var activationFactory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
            var interop = (WgcInterop.IGraphicsCaptureItemInterop)activationFactory;
             
            Guid guidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            IntPtr itemPtr = interop.CreateForMonitor(hMonitor, ref guidItem);
            
            if (itemPtr == IntPtr.Zero) throw new Exception("Failed to create GraphicsCaptureItem for monitor.");

            _captureItem = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
            Marshal.Release(itemPtr);

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device, 
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 
                1, 
                new Windows.Graphics.SizeInt32 { Width = size.Width, Height = size.Height });

            _session = _framePool.CreateCaptureSession(_captureItem);
            _session.IsCursorCaptureEnabled = true;    
            _session.StartCapture();

             try 
    {
        _session.IsBorderRequired = false;    
    }
    catch {            }

    _session.StartCapture();

    _currentMonitorHandle = hMonitor;
    _currentCaptureSize = size;
}

        private void CreateStagingTexture(WgcInterop.D3D11_TEXTURE2D_DESC desc)
        {
            desc.Usage = (int)WgcInterop.D3D11_USAGE.D3D11_USAGE_STAGING;
            desc.CPUAccessFlags = (uint)WgcInterop.D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            desc.BindFlags = 0;
            desc.MiscFlags = 0;
            _stagingTexturePtr = WgcInterop.D3D11Manual.CreateTexture2D(_d3dDevicePtr, desc);
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
            if (_reusableSmallBitmap == null)
            {
                _reusableSmallBitmap = new Bitmap(33, 32, PixelFormat.Format32bppRgb);
            }

            var smallBmp = _reusableSmallBitmap;

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
