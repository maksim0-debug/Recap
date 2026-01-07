using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Xml.Linq;
using System.Linq;

namespace Recap
{
    public static class IconExtractor
    {
        public static Bitmap GetJumboIconFromHwnd(IntPtr hwnd)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);

            IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (int)processId);
            if (hProcess == IntPtr.Zero) return null;

            try
            {
                uint length = 0;
                int result = NativeMethods.GetPackageFullName(hProcess, ref length, null);

                if (result != NativeMethods.APPMODEL_ERROR_NO_PACKAGE && length > 0)
                {
                    StringBuilder sb = new StringBuilder((int)length);
                    NativeMethods.GetPackageFullName(hProcess, ref length, sb);
                    string packageFullName = sb.ToString();

                    return GetUwpJumboIcon(packageFullName);
                }
                else
                {
                    int capacity = 1024;
                    StringBuilder sb = new StringBuilder(capacity);
                    int size = capacity;
                    if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return GetWin32JumboIcon(sb.ToString());
                    }
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
            return null;
        }

        public static Bitmap GetWin32JumboIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            NativeMethods.SHFILEINFO shinfo = new NativeMethods.SHFILEINFO();
            NativeMethods.SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo),
                NativeMethods.SHGFI_SYSICONINDEX | NativeMethods.SHGFI_USEFILEATTRIBUTES);

            Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            NativeMethods.IImageList iml;
            int hRes = NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref iidImageList, out iml);

            if (hRes == 0 && iml != null)
            {
                IntPtr hIcon = IntPtr.Zero;
                iml.GetIcon(shinfo.iIcon, 1, out hIcon);    

                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using (var icon = Icon.FromHandle(hIcon))
                        {
                            return icon.ToBitmap();
                        }
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(hIcon);
                    }
                }
            }
            return null;
        }

        private static Bitmap GetUwpJumboIcon(string packageFullName)
        {
            try
            {
                uint pathLen = 0;
                NativeMethods.GetPackagePathByFullName(packageFullName, ref pathLen, null);
                if (pathLen == 0) return null;

                StringBuilder pathSb = new StringBuilder((int)pathLen);
                NativeMethods.GetPackagePathByFullName(packageFullName, ref pathLen, pathSb);
                string installPath = pathSb.ToString();

                string manifestPath = Path.Combine(installPath, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) return null;

                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                XDocument doc;
                try { doc = XDocument.Load(manifestPath); } catch { return null; }

                string identityName = doc.Root?.Element(ns + "Identity")?.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(identityName)) return null;

                XElement visualElements = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "VisualElements");
                
                if (visualElements == null) return null;

                string logoPath = visualElements.Attribute("Square150x150Logo")?.Value 
                               ?? visualElements.Attribute("Square44x44Logo")?.Value;

                if (string.IsNullOrEmpty(logoPath)) return null;

                string resourceUri = $"@{{{packageFullName}?ms-resource://{identityName}/Files/{logoPath}}}";

                StringBuilder outBuff = new StringBuilder(1024);
                int result = NativeMethods.SHLoadIndirectString(resourceUri, outBuff, outBuff.Capacity, IntPtr.Zero);

                if (result == 0)  
                {
                    string finalFilePath = outBuff.ToString();
                    if (File.Exists(finalFilePath))
                    {
                         using (var stream = new FileStream(finalFilePath, FileMode.Open, FileAccess.Read))
                         {
                             return new Bitmap(stream);
                         }
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
