using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Recap
{
    public static class ActiveWindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        public static IntPtr GetActiveWindowHandle()
        {
            return GetForegroundWindow();
        }

        public static IntPtr GetRealWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                var p = System.Diagnostics.Process.GetProcessById((int)pid);

                if (p.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    IntPtr foundChild = IntPtr.Zero;
                    uint hostPid = pid;

                    EnumChildWindows(hwnd, (childHwnd, lParam) =>
                    {
                        GetWindowThreadProcessId(childHwnd, out uint childPid);
                        if (childPid != hostPid)
                        {
                            foundChild = childHwnd;
                            return false;   
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (foundChild != IntPtr.Zero)
                    {
                        return foundChild;
                    }
                }
            }
            catch { }

            return hwnd;
        }


        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        public static string GetActiveWindowProcessName()
        {
            return GetProcessNameFromHwnd(GetForegroundWindow());
        }

        public static string GetProcessNameFromHwnd(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return "Unknown.exe";

                GetWindowThreadProcessId(hwnd, out uint pid);
                System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid);
                return p.ProcessName + ".exe";
            }
            catch
            {
                return "Unknown.exe";
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        public static string GetProcessPathFromHwnd(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return null;

                GetWindowThreadProcessId(hwnd, out uint pid);
                
                const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero) return null;

                try
                {
                    uint capacity = 1024;
                    StringBuilder sb = new StringBuilder((int)capacity);
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch { }
            return null;
        }

        public static string GetActiveWindowTitle()
        {
            return GetWindowTitleFromHwnd(GetForegroundWindow());
        }

        public static string GetWindowTitleFromHwnd(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return "";

                int length = GetWindowTextLength(hwnd);
                if (length == 0) return "";

                StringBuilder sb = new StringBuilder(length + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}