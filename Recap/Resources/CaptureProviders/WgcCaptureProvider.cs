using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Windows.Forms;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Recap.Resources.CaptureProviders
{
    public enum CaptureTargetType
    {
        Monitor,
        Window
    }

    public class WgcCaptureProvider : ICaptureProvider
    {
        private IDirect3DDevice _device;
        private IntPtr _d3dDevicePtr;
        private IntPtr _d3dContextPtr;
        private GraphicsCaptureItem _captureItem;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private IntPtr _stagingTexturePtr;
        private Size _currentCaptureSize;
        private IntPtr _currentMonitorHandle;
        private IntPtr _currentWindowHandle;
        private CaptureTargetType _captureType = CaptureTargetType.Monitor;
        private bool _initialized;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        public string Name => "WGC";

        public bool IsAvailable()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var version = Environment.OSVersion.Version;
                return version.Major > 10 || (version.Major == 10 && version.Build >= 17134);
            }
            return false;
        }

        public bool Initialize(Screen screen)
        {
            try
            {
                if (!_initialized)
                {
                    InitializeDxDevice();
                    _initialized = true;
                }

                return SetupSession(screen);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WGC Init Failed: " + ex);
                return false;
            }
        }

        public bool InitializeForWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                if (!_initialized)
                {
                    InitializeDxDevice();
                    _initialized = true;
                }

                _captureType = CaptureTargetType.Window;
                return SetupSessionForWindow(hWnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WGC Window Init Failed: " + ex);
                return false;
            }
        }

        private void InitializeDxDevice()
        {
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
        }

        private bool SetupSession(Screen screen)
        {
            if (screen == null) return false;

            int captureX = screen.Bounds.X;
            int captureY = screen.Bounds.Y;
            int captureWidth = screen.Bounds.Width;
            int captureHeight = screen.Bounds.Height;

            RECT rect = new RECT { Left = captureX, Top = captureY, Right = captureX + captureWidth, Bottom = captureY + captureHeight };
            IntPtr hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTOPRIMARY);

            if (hMonitor == _currentMonitorHandle &&
                _currentCaptureSize.Width == captureWidth &&
                _currentCaptureSize.Height == captureHeight &&
                _session != null)
            {
                return true;
            }

            return RestartSession(hMonitor, new Size(captureWidth, captureHeight));
        }

        private bool RestartSession(IntPtr hMonitor, Size size)
        {
            try
            {
                _session?.Dispose();
                _framePool?.Dispose();

                var activationFactory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
                var interop = (WgcInterop.IGraphicsCaptureItemInterop)activationFactory;

                Guid guidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                IntPtr itemPtr = interop.CreateForMonitor(hMonitor, ref guidItem);

                if (itemPtr == IntPtr.Zero) return false;

                _captureItem = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
                Marshal.Release(itemPtr);

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _device,
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    new Windows.Graphics.SizeInt32 { Width = size.Width, Height = size.Height });

                _session = _framePool.CreateCaptureSession(_captureItem);
                _session.IsCursorCaptureEnabled = true;

                try
                {
                    _session.IsBorderRequired = false;
                }
                catch { }

                _session.StartCapture();

                _currentMonitorHandle = hMonitor;
                _currentWindowHandle = IntPtr.Zero;
                _captureType = CaptureTargetType.Monitor;
                _currentCaptureSize = size;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RestartSession Failed: " + ex);
                return false;
            }
        }

        private bool SetupSessionForWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            RECT rect;
            if (!GetWindowRect(hWnd, out rect))
            {
                return false;
            }

            int captureWidth = rect.Right - rect.Left;
            int captureHeight = rect.Bottom - rect.Top;

            if (captureWidth <= 0 || captureHeight <= 0) return false;

            if (hWnd == _currentWindowHandle &&
                _currentCaptureSize.Width == captureWidth &&
                _currentCaptureSize.Height == captureHeight &&
                _session != null &&
                _captureType == CaptureTargetType.Window)
            {
                return true;
            }

            return RestartSessionForWindow(hWnd, new Size(captureWidth, captureHeight));
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private bool RestartSessionForWindow(IntPtr hWnd, Size size)
        {
            try
            {
                _session?.Dispose();
                _framePool?.Dispose();

                var activationFactory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
                var interop = (WgcInterop.IGraphicsCaptureItemInterop)activationFactory;

                Guid guidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

                IntPtr itemPtr = interop.CreateForWindow(hWnd, ref guidItem);

                if (itemPtr == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("CreateForWindow returned null");
                    return false;
                }

                _captureItem = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
                Marshal.Release(itemPtr);

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _device,
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    new Windows.Graphics.SizeInt32 { Width = size.Width, Height = size.Height });

                _session = _framePool.CreateCaptureSession(_captureItem);
                _session.IsCursorCaptureEnabled = true;

                try
                {
                    _session.IsBorderRequired = false;
                }
                catch { }

                _session.StartCapture();

                _currentWindowHandle = hWnd;
                _currentMonitorHandle = IntPtr.Zero;
                _captureType = CaptureTargetType.Window;
                _currentCaptureSize = size;

                System.Diagnostics.Debug.WriteLine($"WGC: Started window capture session for hWnd {hWnd}, size {size}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RestartSessionForWindow Failed: " + ex);
                return false;
            }
        }

        private void CreateStagingTexture(WgcInterop.D3D11_TEXTURE2D_DESC desc)
        {
            desc.Usage = (int)WgcInterop.D3D11_USAGE.D3D11_USAGE_STAGING;
            desc.CPUAccessFlags = (uint)WgcInterop.D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            desc.BindFlags = 0;
            desc.MiscFlags = 0;
            _stagingTexturePtr = WgcInterop.D3D11Manual.CreateTexture2D(_d3dDevicePtr, desc);
        }

        public CaptureResult Capture(Bitmap targetBitmap = null)
        {
            if (!_initialized || _session == null) return new CaptureResult { Success = false };

            Direct3D11CaptureFrame frame = null;
            for (int i = 0; i < 5; i++)
            {
                if (_framePool == null) return new CaptureResult { Success = false, DeviceLost = true };

                try
                {
                    frame = _framePool.TryGetNextFrame();
                }
                catch
                {
                }

                if (frame != null) break;
                Thread.Sleep(5);
            }

            if (frame == null) return new CaptureResult { Success = false };

            try
            {
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
                            if (_stagingTexturePtr == IntPtr.Zero) return new CaptureResult { Success = false };
                        }
                        else
                        {
                            var stagingDesc = WgcInterop.D3D11Manual.GetDesc(_stagingTexturePtr);
                            if (stagingDesc.Width != desc.Width || stagingDesc.Height != desc.Height)
                            {
                                WgcInterop.D3D11Manual.Release(_stagingTexturePtr);
                                CreateStagingTexture(desc);
                                if (_stagingTexturePtr == IntPtr.Zero) return new CaptureResult { Success = false };
                            }
                        }

                        WgcInterop.D3D11Manual.CopyResource(_d3dContextPtr, _stagingTexturePtr, texturePtr);

                        WgcInterop.D3D11_MAPPED_SUBRESOURCE map = WgcInterop.D3D11Manual.Map(_d3dContextPtr, _stagingTexturePtr, 0, WgcInterop.D3D11_MAP.D3D11_MAP_READ, 0);

                        try
                        {
                            int width = Math.Min((int)desc.Width, _currentCaptureSize.Width);
                            int height = Math.Min((int)desc.Height, _currentCaptureSize.Height);

                            if (targetBitmap == null)
                            {
                                targetBitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                            }
                            BitmapData bmpData = targetBitmap.LockBits(
                                new Rectangle(0, 0, targetBitmap.Width, targetBitmap.Height),
                                ImageLockMode.WriteOnly,
                                PixelFormat.Format32bppRgb);

                            try
                            {
                                int copyHeight = Math.Min(height, targetBitmap.Height);
                                int copyWidth = Math.Min(width, targetBitmap.Width);
                                int bytesPerPixel = 4;
                                int rowBytes = copyWidth * bytesPerPixel;

                                for (int y = 0; y < copyHeight; y++)
                                {
                                    IntPtr srcRow = map.pData + (y * (int)map.RowPitch);
                                    IntPtr destRow = bmpData.Scan0 + (y * bmpData.Stride);
                                    CopyMemory(destRow, srcRow, (uint)rowBytes);
                                }
                            }
                            finally
                            {
                                targetBitmap.UnlockBits(bmpData);
                            }

                            return new CaptureResult { Bitmap = targetBitmap, Success = true };
                        }
                        finally
                        {
                            WgcInterop.D3D11Manual.Unmap(_d3dContextPtr, _stagingTexturePtr, 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        return new CaptureResult { Success = false, DeviceLost = true };
                    }
                    finally
                    {
                        WgcInterop.D3D11Manual.Release(texturePtr);
                    }
                }
            }
            finally
            {
                frame?.Dispose();
            }
        }

        public void Dispose()
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
            _initialized = false;
        }
    }
}
