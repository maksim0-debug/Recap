using System;
using System.Runtime.InteropServices;

namespace Recap.Resources.CaptureProviders.Interop
{
    public static class DxgiInterop
    {
        public const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
        public const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        public const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);
        public const int S_OK = 0;

        [ComImport]
        [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIFactory1
        {
            void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
            void EnumAdapters(uint Adapter, [MarshalAs(UnmanagedType.Interface)] out object ppAdapter);
            void MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
            void GetWindowAssociation(out IntPtr pWindowHandle);
            void CreateSwapChain(IntPtr pDevice, ref object pDesc, [MarshalAs(UnmanagedType.Interface)] out object ppSwapChain);
            void CreateSoftwareAdapter(IntPtr Module, [MarshalAs(UnmanagedType.Interface)] out object ppAdapter);
            void EnumAdapters1(uint Adapter, [MarshalAs(UnmanagedType.Interface)] out object ppAdapter);
            bool IsCurrent();
        }

        [ComImport]
        [Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIAdapter
        {
            void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
            void EnumOutputs(uint Output, out IDXGIOutput ppOutput);
            void GetDesc(out DXGI_ADAPTER_DESC pDesc);
            void CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
        }

        [ComImport]
        [Guid("29038f61-3839-4b94-9969-13e1920213b5")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIAdapter1
        {
            void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
            void EnumOutputs(uint Output, out IDXGIOutput ppOutput);
            void GetDesc(out DXGI_ADAPTER_DESC pDesc);
            void CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
            void GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public IntPtr DedicatedVideoMemory;
            public IntPtr DedicatedSystemMemory;
            public IntPtr SharedSystemMemory;
            public IntPtr AdapterLuidLow;
            public IntPtr AdapterLuidHigh;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public IntPtr DedicatedVideoMemory;
            public IntPtr DedicatedSystemMemory;
            public IntPtr SharedSystemMemory;
            public IntPtr AdapterLuidLow;
            public IntPtr AdapterLuidHigh;
            public uint Flags;
        }

        [ComImport]
        [Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIOutput
        {
            void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
            void GetDesc(out DXGI_OUTPUT_DESC pDesc);
            void GetDisplayModeList(int EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
            void FindClosestMatchingMode(ref object pModeToMatch, out object pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
            void WaitForVBlank();
            void TakeOwnership([MarshalAs(UnmanagedType.IUnknown)] object pDevice, bool Exclusive);
            void ReleaseOwnership();
            void GetGammaControlCapabilities(out object pGammaCaps);
            void SetGammaControl(ref object pArray);
            void GetGammaControl(out object pArray);
            void SetDisplaySurface([MarshalAs(UnmanagedType.IUnknown)] object pScanoutSurface);
            void GetDisplaySurfaceData([MarshalAs(UnmanagedType.IUnknown)] object pDestination);
            void GetFrameStatistics(out object pStats);
        }

        [ComImport]
        [Guid("00cddea8-939b-4b83-a340-a685226666cc")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIOutput1 : IDXGIOutput
        {
             new void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
             new void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
             new void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
             new void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
             new void GetDesc(out DXGI_OUTPUT_DESC pDesc);
             new void GetDisplayModeList(int EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
             new void FindClosestMatchingMode(ref object pModeToMatch, out object pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
             new void WaitForVBlank();
             new void TakeOwnership([MarshalAs(UnmanagedType.IUnknown)] object pDevice, bool Exclusive);
             new void ReleaseOwnership();
             new void GetGammaControlCapabilities(out object pGammaCaps);
             new void SetGammaControl(ref object pArray);
             new void GetGammaControl(out object pArray);
             new void SetDisplaySurface([MarshalAs(UnmanagedType.IUnknown)] object pScanoutSurface);
             new void GetDisplaySurfaceData([MarshalAs(UnmanagedType.IUnknown)] object pDestination);
             new void GetFrameStatistics(out object pStats);
            void GetDisplayModeList1(int EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
            void FindClosestMatchingMode1(ref object pModeToMatch, out object pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
            void GetDisplaySurfaceData1([MarshalAs(UnmanagedType.IUnknown)] object pDestination);
            [PreserveSig]
            int DuplicateOutput([MarshalAs(UnmanagedType.IUnknown)] object pDevice, out IDXGIOutputDuplication ppOutputDuplication);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_OUTPUT_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            public RECT DesktopCoordinates;
            public bool AttachedToDesktop;
            public int Rotation;
            public IntPtr Monitor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [ComImport]
        [Guid("191cfac3-a341-470d-b26e-a864f428319c")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIOutputDuplication
        {
            void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);

            void GetDesc(out DXGI_OUTDUPL_DESC pDesc);
            [PreserveSig]
            int AcquireNextFrame(uint TimeoutInMilliseconds, out DXGI_OUTDUPL_FRAME_INFO pFrameInfo, out IDXGIResource ppDesktopResource);
            void GetFrameDirtyRects(uint DirtyRectsBufferSize, IntPtr pDirtyRectsBuffer, out uint pDirtyRectsBufferSizeRequired);
            void GetFrameMoveRects(uint MoveRectsBufferSize, IntPtr pMoveRectsBuffer, out uint pMoveRectsBufferSizeRequired);
            void GetFramePointerShape(uint PointerShapeBufferSize, IntPtr pPointerShapeBuffer, out uint pPointerShapeBufferSizeRequired, out object pPointerShapeInfo);
            void MapDesktopSurface(out object pLockedRect);
            void UnMapDesktopSurface();
            [PreserveSig]
            int ReleaseFrame();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_OUTDUPL_DESC
        {
            public DXGI_MODE_DESC ModeDesc;
            public int Rotation;
            public int DesktopImageInSystemMemory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_MODE_DESC
        {
            public int Width;
            public int Height;
            public DXGI_RATIONAL RefreshRate;
            public int Format;
            public int ScanlineOrdering;
            public int Scaling;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_OUTDUPL_FRAME_INFO
        {
            public long LastPresentTime;
            public long LastMouseUpdateTime;
            public uint AccumulatedFrames;
            [MarshalAs(UnmanagedType.Bool)] public bool RectsCoalesced;
            [MarshalAs(UnmanagedType.Bool)] public bool ProtectedContentMaskedOut;
            public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
            public uint TotalMetadataBufferSize;
            public uint PointerShapeBufferSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_OUTDUPL_POINTER_POSITION
        {
            public POINT Position;
            public int Visible;  
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [ComImport]
        [Guid("035f3ab4-482e-4e50-b41f-8a7f8bd8960b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIResource
        {
            void SetPrivateData(ref Guid Name, int DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref int pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
            void GetDevice(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
            void GetEvictionPriority(out uint pEvictionPriority);
            void SetEvictionPriority(uint EvictionPriority);
            void GetUsage(out int pUsage);
            void GetSharedHandle(out IntPtr pSharedHandle);
        }

        [DllImport("dxgi.dll")]
        public static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", CallingConvention = CallingConvention.StdCall)]
        public static extern int D3D11CreateDevice(
             IntPtr pAdapter,
             int DriverType,
             IntPtr Software,
             uint Flags,
             IntPtr pFeatureLevels,
             uint FeatureLevels,
             uint SDKVersion,
             [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice,
             out int pFeatureLevel,
             [MarshalAs(UnmanagedType.IUnknown)] out object ppImmediateContext);
    }
}
