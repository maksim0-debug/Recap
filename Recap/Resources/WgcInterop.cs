using System;
using System.Runtime.InteropServices;
using System.Reflection;

namespace Recap.Resources
{
    public static class WgcInterop
    {
        public static readonly Guid IID_IGraphicsCaptureItemInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        public static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        public static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", CallingConvention = CallingConvention.StdCall)]
public static extern int D3D11CreateDevice(
    IntPtr pAdapter,
    int driverType,
    IntPtr software,
    uint flags,
    IntPtr pFeatureLevels,
    uint featureLevels,
    uint sdkVersion,
    out IntPtr ppDevice,
    out int pFeatureLevel,
    out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        public const int D3D11_SDK_VERSION = 7;
        public const int D3D_DRIVER_TYPE_HARDWARE = 1;
        public const int D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20; 

        public enum D3D11_USAGE
        {
            D3D11_USAGE_DEFAULT = 0,
            D3D11_USAGE_IMMUTABLE = 1,
            D3D11_USAGE_DYNAMIC = 2,
            D3D11_USAGE_STAGING = 3
        }

        public enum D3D11_CPU_ACCESS_FLAG
        {
            D3D11_CPU_ACCESS_READ = 0x20000,
            D3D11_CPU_ACCESS_WRITE = 0x10000
        }

        public enum D3D11_BIND_FLAG
        {
            D3D11_BIND_SHADER_RESOURCE = 0x8,
            D3D11_BIND_RENDER_TARGET = 0x20
        }

        public enum DXGI_FORMAT
        {
            DXGI_FORMAT_B8G8R8A8_UNORM = 87
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_TEXTURE2D_DESC
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public int Format;
            public D3D11_SAMPLE_DESC SampleDesc;
            public int Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_SAMPLE_DESC
        {
            public uint Count;
            public uint Quality;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_MAPPED_SUBRESOURCE
        {
            public IntPtr pData;
            public uint RowPitch;
            public uint DepthPitch;
        }

        public enum D3D11_MAP
        {
            D3D11_MAP_READ = 1,
            D3D11_MAP_WRITE = 2,
            D3D11_MAP_READ_WRITE = 3,
            D3D11_MAP_WRITE_DISCARD = 4,
            D3D11_MAP_WRITE_NO_OVERWRITE = 5
        }

        public static class D3D11Manual
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int CreateTexture2DDelegate(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture2D);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void CopyResourceDelegate(IntPtr context, IntPtr dstResource, IntPtr srcResource);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int MapDelegate(IntPtr context, IntPtr resource, uint subresource, int mapType, int mapFlags, out D3D11_MAPPED_SUBRESOURCE mappedResource);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void UnmapDelegate(IntPtr context, IntPtr resource, uint subresource);
            
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void GetDescDelegate(IntPtr texture, out D3D11_TEXTURE2D_DESC desc);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int ReleaseDelegate(IntPtr unknown);
            
            public static int Release(IntPtr unknown)
            {
                if (unknown == IntPtr.Zero) return 0;
                var vtable = Marshal.ReadIntPtr(unknown);
                var methodPtr = Marshal.ReadIntPtr(vtable, 2 * IntPtr.Size);
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(methodPtr);
                return release(unknown);
            }

            public static IntPtr CreateTexture2D(IntPtr device, D3D11_TEXTURE2D_DESC desc)
            {
                 var vtable = Marshal.ReadIntPtr(device);
                 var methodPtr = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size);
                 var func = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(methodPtr);
                 IntPtr resultPtr;
                 int hr = func(device, ref desc, IntPtr.Zero, out resultPtr);
                 if (hr < 0) throw new COMException("Failed to CreateTexture2D", hr);
                 return resultPtr;
            }

            public static void CopyResource(IntPtr context, IntPtr dest, IntPtr src)
            {
                var vtable = Marshal.ReadIntPtr(context);
                var methodPtr = Marshal.ReadIntPtr(vtable, 47 * IntPtr.Size);
                var func = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(methodPtr);
                func(context, dest, src);
            }

            public static D3D11_MAPPED_SUBRESOURCE Map(IntPtr context, IntPtr resource, uint subresource, D3D11_MAP mapType, int mapFlags)
            {
                var vtable = Marshal.ReadIntPtr(context);
                var methodPtr = Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size);
                var func = Marshal.GetDelegateForFunctionPointer<MapDelegate>(methodPtr);
                D3D11_MAPPED_SUBRESOURCE mapped;
                int hr = func(context, resource, subresource, (int)mapType, mapFlags, out mapped);
                if (hr < 0) throw new COMException("Failed to Map resource", hr);
                return mapped;
            }

            public static void Unmap(IntPtr context, IntPtr resource, uint subresource)
            {
                var vtable = Marshal.ReadIntPtr(context);
                var methodPtr = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                var func = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(methodPtr);
                func(context, resource, subresource);
            }

             public static D3D11_TEXTURE2D_DESC GetDesc(IntPtr texture)
            {
                var vtable = Marshal.ReadIntPtr(texture);
                var methodPtr = Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size);
                var func = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(methodPtr);
                D3D11_TEXTURE2D_DESC desc;
                func(texture, out desc);
                return desc;
            }
        }
    }
}
