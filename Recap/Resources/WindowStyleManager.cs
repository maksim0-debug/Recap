using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Recap.Resources
{
    public class WindowStyleManager : IDisposable
    {
        #region WinAPI Constants

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;

        private const uint WS_CAPTION = 0x00C00000;        
        private const uint WS_THICKFRAME = 0x00040000;     
        private const uint WS_SYSMENU = 0x00080000;        
        private const uint WS_MINIMIZEBOX = 0x00020000;
        private const uint WS_MAXIMIZEBOX = 0x00010000;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_VISIBLE = 0x10000000;

        private const uint WS_EX_DLGMODALFRAME = 0x00000001;
        private const uint WS_EX_CLIENTEDGE = 0x00000200;
        private const uint WS_EX_STATICEDGE = 0x00020000;

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;

        #endregion

        #region WinAPI Imports

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        private readonly Dictionary<IntPtr, int> _originalStyles = new Dictionary<IntPtr, int>();
        private readonly Dictionary<IntPtr, int> _originalExStyles = new Dictionary<IntPtr, int>();
        private readonly Dictionary<IntPtr, RECT> _originalRects = new Dictionary<IntPtr, RECT>();

        private readonly HashSet<string> _gameProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "javaw",         
            "java",          
            "minecraft"       
        };

        public IntPtr FindMinecraftWindow()
        {
            return FindGameWindow("javaw");
        }

        public IntPtr FindGameWindow(string processName)
        {
            IntPtr foundWindow = IntPtr.Zero;

            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var proc in processes)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero && IsWindowVisible(proc.MainWindowHandle))
                    {
                        foundWindow = proc.MainWindowHandle;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("WindowStyleManager.FindGameWindow", ex);
            }

            return foundWindow;
        }

        public bool IsGameWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                var proc = Process.GetProcessById((int)pid);
                return _gameProcessNames.Contains(proc.ProcessName);
            }
            catch
            {
                return false;
            }
        }

        public bool ApplyBorderlessStyle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                int style = GetWindowLong(hWnd, GWL_STYLE);
                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

                if (!_originalStyles.ContainsKey(hWnd))
                {
                    _originalStyles[hWnd] = style;
                    _originalExStyles[hWnd] = exStyle;

                    RECT rect;
                    if (GetWindowRect(hWnd, out rect))
                    {
                        _originalRects[hWnd] = rect;
                    }
                }

                uint newStyle = (uint)style;
                newStyle &= ~WS_CAPTION;         
                newStyle &= ~WS_THICKFRAME;      
                newStyle &= ~WS_SYSMENU;         
                newStyle &= ~WS_MINIMIZEBOX;
                newStyle &= ~WS_MAXIMIZEBOX;

                uint newExStyle = (uint)exStyle;
                newExStyle &= ~WS_EX_DLGMODALFRAME;
                newExStyle &= ~WS_EX_CLIENTEDGE;
                newExStyle &= ~WS_EX_STATICEDGE;

                SetWindowLong(hWnd, GWL_STYLE, (int)newStyle);
                SetWindowLong(hWnd, GWL_EXSTYLE, (int)newExStyle);

                SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                DebugLogger.Log($"WindowStyleManager: Applied borderless style to hWnd {hWnd}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("WindowStyleManager.ApplyBorderlessStyle", ex);
                return false;
            }
        }

        public bool StretchToFullscreen(IntPtr hWnd, Screen screen = null)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                screen = screen ?? Screen.FromHandle(hWnd) ?? Screen.PrimaryScreen;

                var bounds = screen.Bounds;

                SetWindowPos(hWnd, HWND_TOP,
                    bounds.X, bounds.Y,
                    bounds.Width, bounds.Height,
                    SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

                DebugLogger.Log($"WindowStyleManager: Stretched window to {bounds}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("WindowStyleManager.StretchToFullscreen", ex);
                return false;
            }
        }

        public bool ApplyBorderlessFullscreen(IntPtr hWnd, Screen screen = null)
        {
            if (!ApplyBorderlessStyle(hWnd)) return false;
            return StretchToFullscreen(hWnd, screen);
        }

        public bool RestoreWindowStyle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                if (_originalStyles.TryGetValue(hWnd, out int style))
                {
                    SetWindowLong(hWnd, GWL_STYLE, style);
                }

                if (_originalExStyles.TryGetValue(hWnd, out int exStyle))
                {
                    SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
                }

                if (_originalRects.TryGetValue(hWnd, out RECT rect))
                {
                    SetWindowPos(hWnd, HWND_TOP,
                        rect.Left, rect.Top,
                        rect.Right - rect.Left, rect.Bottom - rect.Top,
                        SWP_NOZORDER | SWP_FRAMECHANGED);
                }

                _originalStyles.Remove(hWnd);
                _originalExStyles.Remove(hWnd);
                _originalRects.Remove(hWnd);

                DebugLogger.Log($"WindowStyleManager: Restored original style for hWnd {hWnd}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("WindowStyleManager.RestoreWindowStyle", ex);
                return false;
            }
        }

        public void RestoreAllWindows()
        {
            var handles = new List<IntPtr>(_originalStyles.Keys);
            foreach (var hWnd in handles)
            {
                RestoreWindowStyle(hWnd);
            }
        }

        public void Dispose()
        {
            RestoreAllWindows();
        }
    }
}
