using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Recap.Resources.CaptureProviders.Interop;

namespace Recap.Resources.CaptureProviders
{
    public class DxgiCaptureProvider : ICaptureProvider
    {
        private object _device;
        private object _context;
        private DxgiInterop.IDXGIOutputDuplication _duplication;
        private Screen _screen;
        private IntPtr _stagingTexture;      
        private Size _textureSize;

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        public string Name => "DXGI";

        private void Log(string message)
        {
            try
            {
                string msg = $"[DXGI] {DateTime.Now:HH:mm:ss.fff}: {message}";
                System.Diagnostics.Trace.WriteLine(msg);
                Console.WriteLine(msg);
            }
            catch { }
        }

        public bool IsAvailable()
        {
            return Environment.OSVersion.Version.Major > 6 || (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor >= 2);
        }

        public bool Initialize(Screen screen)
        {
            if (_duplication != null && _screen != null && _screen.DeviceName == screen.DeviceName)
            {
                return true;
            }

            Log($"Initializing for screen: {screen.DeviceName}");
            _screen = screen;
            try
            {
                InitializeDxgi();
                Log("Initialization successful");
                return true;
            }
            catch (Exception ex)
            {
                Log($"DXGI Init Error: {ex}");
                Cleanup();
                return false;
            }
        }

        public bool InitializeForWindow(IntPtr hWnd)
        {
            return false;
        }

        private void InitializeDxgi()
        {
            Cleanup();

            Log("Starting InitializeDxgi...");

            int hr = DxgiInterop.D3D11CreateDevice(
                IntPtr.Zero,
                1,  
                IntPtr.Zero,
                0,      
                IntPtr.Zero,
                0,
                7,  
                out _device,
                out int featureLevel,
                out _context
            );

            if (hr < 0)
            {
                Log($"D3D11CreateDevice failed. HR=0x{hr:X}");
                throw new Exception($"D3D11CreateDevice failed HR=0x{hr:X}");
            }
            Log($"D3D11CreateDevice success. FeatureLevel={featureLevel}");

            Guid factoryGuid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            DxgiInterop.IDXGIFactory1 factory;
            int factoryHr = DxgiInterop.CreateDXGIFactory1(ref factoryGuid, out factory);

            if (factoryHr < 0)
            {
                string msg = $"CreateDXGIFactory1 failed. HR=0x{factoryHr:X}";
                Log(msg);
                throw new Exception(msg);
            }

            DxgiInterop.IDXGIAdapter adapter = null;
            DxgiInterop.IDXGIOutput1 output1 = null;

            try
            {
                uint i = 0;
                while (true)
                {
                    try
                    {
                        object adapterObj;
                        factory.EnumAdapters(i, out adapterObj);
                        adapter = (DxgiInterop.IDXGIAdapter)adapterObj;
                        i++;
                    }
                    catch (Exception ex)
                    {
                        if (i == 0) Log($"EnumAdapters at index 0 failed! {ex}");
                        break;
                    }

                    DxgiInterop.DXGI_ADAPTER_DESC adapterDesc;
                    adapter.GetDesc(out adapterDesc);
                    Log($"Checking Adapter {i - 1}: {adapterDesc.Description}");

                    uint j = 0;
                    while (true)
                    {
                        DxgiInterop.IDXGIOutput output;
                        try
                        {
                            adapter.EnumOutputs(j, out output);
                            j++;
                        }
                        catch
                        {
                            break;    
                        }

                        DxgiInterop.DXGI_OUTPUT_DESC desc;
                        output.GetDesc(out desc);

                        string logMsg = $"  -> Output {j - 1}: DeviceName='{desc.DeviceName}' (Bounds: {desc.DesktopCoordinates.Left},{desc.DesktopCoordinates.Top} {desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left}x{desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top}) vs Target: '{_screen?.DeviceName}'";
                        Log(logMsg);

                        if (desc.DeviceName == _screen.DeviceName)
                        {
                            output1 = output as DxgiInterop.IDXGIOutput1;
                            if (output1 == null)
                            {
                                Log("  -> Output is not IDXGIOutput1");
                                Marshal.ReleaseComObject(output);
                                continue;
                            }

                            Log("  -> Found matching output!");

                            Marshal.ReleaseComObject(_device);
                            Marshal.ReleaseComObject(_context);
                            _device = null;
                            _context = null;

                            IntPtr adapterPtr = Marshal.GetIUnknownForObject(adapter);
                            try
                            {
                                Log("  -> Creating D3D11 Device on specific adapter...");
                                hr = DxgiInterop.D3D11CreateDevice(
                                   adapterPtr,
                                   0,     
                                   IntPtr.Zero,
                                   0x20,   
                                   IntPtr.Zero,
                                   0,
                                   7,
                                   out _device,
                                   out featureLevel,
                                   out _context
                               );
                            }
                            finally
                            {
                                Marshal.Release(adapterPtr);
                            }

                            if (hr < 0)
                            {
                                Log($"  -> Failed to create D3D11 device on adapter. HR=0x{hr:X}");
                                throw new Exception("Failed to create D3D11 device on specific adapter");
                            }
                            Log($"  -> D3D11 Device created. Duplicating output...");

                            hr = output1.DuplicateOutput(_device, out _duplication);
                            if (hr < 0)
                            {
                                Log($"  -> DuplicateOutput failed. HR=0x{hr:X} ({(uint)hr})");
                                if (hr == DxgiInterop.DXGI_ERROR_ACCESS_LOST) Log("     -> Access Lost (Mode change in progress?)");
                                if (hr == DxgiInterop.DXGI_ERROR_UNSUPPORTED) Log("     -> Unsupported (Maybe running on wrong GPU?)");
                                if ((uint)hr == 0x887A0004) Log("     -> DXGI_ERROR_DEVICE_REMOVED");

                                throw new COMException("DuplicateOutput failed", hr);
                            }

                            Log("  -> DuplicateOutput success!");
                            return;  
                        }
                        Marshal.ReleaseComObject(output);
                    }
                    Marshal.ReleaseComObject(adapter);
                }
            }
            finally
            {
                if (factory != null) Marshal.ReleaseComObject(factory);
            }

            Log("Screen not found in DXGI outputs");
            throw new Exception("Screen not found in DXGI outputs");
        }

        public CaptureResult Capture(Bitmap targetBitmap = null)
        {
            if (_duplication == null)
            {
                Log("Capture: _duplication is null");
                return new CaptureResult { Success = false };
            }

            DxgiInterop.DXGI_OUTDUPL_FRAME_INFO frameInfo;
            DxgiInterop.IDXGIResource desktopResource = null;

            try
            {
                int hr = _duplication.AcquireNextFrame(100, out frameInfo, out desktopResource);

                if (hr == DxgiInterop.DXGI_ERROR_WAIT_TIMEOUT)
                {
                    return new CaptureResult { Bitmap = targetBitmap, Success = true };
                }
                if (hr == DxgiInterop.DXGI_ERROR_ACCESS_LOST)
                {
                    Log("Capture: DXGI_ERROR_ACCESS_LOST");
                    Cleanup();
                    return new CaptureResult { Success = false, DeviceLost = true };
                }
                if (hr < 0)
                {
                    Log($"Capture: AcquireNextFrame failed. HR=0x{hr:X}");
                    return new CaptureResult { Success = false };
                }

                if (desktopResource == null)
                {
                    Log("Capture: desktopResource is null despite S_OK");
                    _duplication.ReleaseFrame();
                    return new CaptureResult { Success = false };
                }

                using (var texture = ProcessFrame(desktopResource))
                {
                    IntPtr unk = Marshal.GetIUnknownForObject(desktopResource);
                    IntPtr resourcePtr = IntPtr.Zero;
                    Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                    try
                    {
                        int qiResult = Marshal.QueryInterface(unk, ref IID_ID3D11Texture2D, out resourcePtr);
                        if (qiResult != 0 || resourcePtr == IntPtr.Zero)
                        {
                            Log($"[Error] QI failed. Result: {qiResult}");
                            throw new COMException("Failed to QueryInterface for ID3D11Texture2D", qiResult);
                        }
                    }
                    finally
                    {
                        Marshal.Release(unk);
                    }

                    WgcInterop.D3D11_TEXTURE2D_DESC desc = WgcInterop.D3D11Manual.GetDesc(resourcePtr);
                    if (_stagingTexture == IntPtr.Zero || _textureSize.Width != desc.Width || _textureSize.Height != desc.Height)
                    {
                        if (_stagingTexture != IntPtr.Zero) WgcInterop.D3D11Manual.Release(_stagingTexture);

                        WgcInterop.D3D11_TEXTURE2D_DESC stagingDesc = desc;
                        stagingDesc.Usage = (int)WgcInterop.D3D11_USAGE.D3D11_USAGE_STAGING;
                        stagingDesc.CPUAccessFlags = (uint)WgcInterop.D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
                        stagingDesc.BindFlags = 0;
                        stagingDesc.MiscFlags = 0;

                        IntPtr devicePtr = GetD3D11DevicePtr();
                        _stagingTexture = WgcInterop.D3D11Manual.CreateTexture2D(devicePtr, stagingDesc);
                        Marshal.Release(devicePtr);

                        Log($"Created Staging Texture: {_stagingTexture} ({desc.Width}x{desc.Height})");

                        _textureSize = new Size((int)desc.Width, (int)desc.Height);
                    }

                    IntPtr contextPtr = GetD3D11ContextPtr();
                    WgcInterop.D3D11Manual.CopyResource(contextPtr, _stagingTexture, resourcePtr);
                    Marshal.Release(resourcePtr);

                    var map = WgcInterop.D3D11Manual.Map(contextPtr, _stagingTexture, 0, WgcInterop.D3D11_MAP.D3D11_MAP_READ, 0);
                    Marshal.Release(contextPtr);

                    try
                    {
                        if (targetBitmap == null)
                            targetBitmap = new Bitmap((int)desc.Width, (int)desc.Height, PixelFormat.Format32bppRgb);

                        BitmapData bmpData = targetBitmap.LockBits(
                           new Rectangle(0, 0, targetBitmap.Width, targetBitmap.Height),
                           ImageLockMode.WriteOnly,
                           PixelFormat.Format32bppRgb);

                        try
                        {
                            int height = Math.Min((int)desc.Height, targetBitmap.Height);
                            int width = Math.Min((int)desc.Width, targetBitmap.Width);
                            int bytesPerPixel = 4;
                            int rowBytes = width * bytesPerPixel;

                            for (int y = 0; y < height; y++)
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
                        contextPtr = GetD3D11ContextPtr();
                        WgcInterop.D3D11Manual.Unmap(contextPtr, _stagingTexture, 0);
                        Marshal.Release(contextPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Capture Exception: {ex}");
                return new CaptureResult { Success = false };
            }
            finally
            {
                if (_duplication != null)
                {
                    try { _duplication.ReleaseFrame(); } catch { }
                }
                if (desktopResource != null) Marshal.ReleaseComObject(desktopResource);
            }
        }

        private IDisposable ProcessFrame(DxgiInterop.IDXGIResource resource)
        {
            return null;     
        }

        private void Cleanup()
        {
            if (_stagingTexture != IntPtr.Zero)
            {
                WgcInterop.D3D11Manual.Release(_stagingTexture);
                _stagingTexture = IntPtr.Zero;
            }
            if (_duplication != null)
            {
                Marshal.ReleaseComObject(_duplication);
                _duplication = null;
            }
            if (_context != null)
            {
                Marshal.ReleaseComObject(_context);
                _context = null;
            }
            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
                _device = null;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2DDelegateDebug(IntPtr device, ref WgcInterop.D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture2D);

        private static readonly Guid IID_ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");
        private static readonly Guid IID_ID3D11DeviceContext = new Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da");

        private IntPtr GetD3D11DevicePtr()
        {
            IntPtr unk = Marshal.GetIUnknownForObject(_device);
            IntPtr devicePtr = IntPtr.Zero;
            try
            {
                Guid iid = IID_ID3D11Device;
                int hr = Marshal.QueryInterface(unk, ref iid, out devicePtr);
                if (hr != 0)
                    throw new COMException($"QueryInterface for ID3D11Device failed", hr);
                return devicePtr;
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        private IntPtr GetD3D11ContextPtr()
        {
            IntPtr unk = Marshal.GetIUnknownForObject(_context);
            IntPtr contextPtr = IntPtr.Zero;
            try
            {
                Guid iid = IID_ID3D11DeviceContext;
                int hr = Marshal.QueryInterface(unk, ref iid, out contextPtr);
                if (hr != 0)
                    throw new COMException($"QueryInterface for ID3D11DeviceContext failed", hr);
                return contextPtr;
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        private IntPtr CreateTexture2DDebug(IntPtr device, WgcInterop.D3D11_TEXTURE2D_DESC desc)
        {
            var vtable = Marshal.ReadIntPtr(device);
            var methodPtr = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size);
            var func = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegateDebug>(methodPtr);
            IntPtr resultPtr = IntPtr.Zero;
            int hr = func(device, ref desc, IntPtr.Zero, out resultPtr);
            Console.WriteLine($"DEBUG: CreateTexture2D HR=0x{hr:X8}, Ptr={resultPtr}");
            if (hr < 0) throw new COMException("Failed to CreateTexture2D", hr);
            return resultPtr;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
