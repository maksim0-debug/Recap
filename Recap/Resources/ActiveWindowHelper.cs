using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Recap
{
    public static class ActiveWindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public static IntPtr GetActiveWindowHandle()
        {
            return GetForegroundWindow();
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