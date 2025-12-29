using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace Recap
{
    public class FullscreenForm : Form, IMessageFilter
    {
        private readonly MainForm _mainForm;
        private readonly PictureBox _pictureBox;
        private readonly VideoView _videoView;
        private readonly bool _isVideoMode;
        private readonly MediaPlayer _mediaPlayer;

        private long _transferTime = -1;
        private bool _isClosing = false;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private const int VK_ESCAPE = 0x1B;
        private const int VK_SPACE = 0x20;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;
        private const int VK_A = 0x41;
        private const int VK_D = 0x44;
        private const int VK_NUMPAD4 = 0x64;
        private const int VK_NUMPAD6 = 0x66;

        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static FullscreenForm _instance;

        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        public FullscreenForm(MainForm mainForm)
        {
            DebugLogger.Log("FullscreenForm: Constructor.");
            _mainForm = mainForm;
            _instance = this;

            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.Black;
            this.KeyPreview = true;

            Application.AddMessageFilter(this);
            _hookID = SetHook(_proc);

            _isVideoMode = _mainForm.MainVideoView.Visible;

            if (_isVideoMode)
            {
                _mediaPlayer = _mainForm.MainMediaPlayer;
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.EnableKeyInput = false;
                    _mediaPlayer.EnableMouseInput = false;
                    _transferTime = _mediaPlayer.Time;
                    _mediaPlayer.Stop();
                }

                if (_mainForm.MainVideoView != null)
                    _mainForm.MainVideoView.MediaPlayer = null;

                _videoView = new VideoView
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Black,
                    MediaPlayer = _mediaPlayer,
                    TabStop = false
                };
                this.Controls.Add(_videoView);
            }
            else
            {
                _pictureBox = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = _mainForm.MainPictureBox.Image != null ? new Bitmap(_mainForm.MainPictureBox.Image) : null
                };
                this.Controls.Add(_pictureBox);
                _mainForm.FrameChanged += OnExternalFrameChanged;
            }

            this.FormClosed += OnFormClosed;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (Form.ActiveForm == _instance && _instance != null && !_instance._isClosing)
                {
                    bool handled = false;
                    if (vkCode == VK_ESCAPE) { _instance.CloseSafely(); handled = true; }
                    else if (vkCode == VK_LEFT || vkCode == VK_A || vkCode == VK_NUMPAD4) { _instance.Navigate(-1); handled = true; }
                    else if (vkCode == VK_RIGHT || vkCode == VK_D || vkCode == VK_NUMPAD6) { _instance.Navigate(1); handled = true; }
                    else if (vkCode == VK_SPACE) { _instance.Navigate(1); handled = true; }
                    if (handled) return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void Navigate(int dir) => this.BeginInvoke((Action)(() => _mainForm.NavigateFrames(dir)));

        private void OnExternalFrameChanged(Image newImage)
        {
            if (_isVideoMode || _pictureBox == null || this.IsDisposed || _isClosing) return;
            if (this.InvokeRequired) this.BeginInvoke((Action)(() => UpdateImageSafe(newImage)));
            else UpdateImageSafe(newImage);
        }

        private void UpdateImageSafe(Image newImage)
        {
            if (this.IsDisposed || _pictureBox == null || _isClosing) return;
            var old = _pictureBox.Image;
            _pictureBox.Image = newImage != null ? new Bitmap(newImage) : null;
            old?.Dispose();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.Activate();
            this.Focus();

            if (_isVideoMode && _mediaPlayer != null)
            {
                Task.Run(async () =>
                {
                    DebugLogger.Log("Fullscreen: Init Video.");
                    bool wasMute = _mediaPlayer.Mute;

                    _mediaPlayer.Mute = true;

                    _mediaPlayer.Play();

                    int retries = 0;
                    while ((!_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0) && retries < 100)
                    {
                        if (_isClosing) return;
                        await Task.Delay(20);
                        retries++;
                    }

                    if (_isClosing) return;

                    if (_transferTime > 0)
                    {
                        _mediaPlayer.Time = _transferTime;
                    }

                    this.Invoke((Action)(() => _mainForm.NavigateFrames(0)));

                    if (_transferTime > 1000)
                    {
                        int seekWait = 0;
                        while (Math.Abs(_mediaPlayer.Time - _transferTime) > 3000 && seekWait < 60)
                        {
                            if (_isClosing) return;
                            if (seekWait % 15 == 0) _mediaPlayer.Time = _transferTime;
                            await Task.Delay(25);
                            seekWait++;
                        }

                        await Task.Delay(500);

                        _mediaPlayer.NextFrame();
                    }
                    else
                    {
                        await Task.Delay(150);
                    }

                    if (_isClosing) return;

                    this.Invoke((Action)(() =>
                    {
                        if (_isClosing) return;

                        if (_mediaPlayer.IsPlaying)
                            _mediaPlayer.Pause();

                        Task.Delay(100).ContinueWith(t =>
                        {
                            if (!_isClosing && _mediaPlayer != null) _mediaPlayer.Mute = wasMute;
                        });

                        this.Activate();
                    }));
                });
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (_isClosing) return false;
            if (m.Msg == WM_LBUTTONDBLCLK) { CloseSafely(); return true; }
            if (m.Msg == WM_MOUSEWHEEL)
            {
                long wParam = m.WParam.ToInt64();
                int delta = (short)((wParam >> 16) & 0xFFFF);
                this.BeginInvoke((Action)(() =>
                {
                    int step = 5;
                    if (delta > 0) _mainForm.NavigateFrames(step);
                    else _mainForm.NavigateFrames(-step);
                }));
                return true;
            }
            return false;
        }

        private void CloseSafely()
        {
            if (_isClosing) return;
            _isClosing = true;
            this.BeginInvoke((Action)(() =>
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }));
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            _isClosing = true;
            if (_hookID != IntPtr.Zero) { UnhookWindowsHookEx(_hookID); _hookID = IntPtr.Zero; }
            Application.RemoveMessageFilter(this);
            _instance = null;

            if (!_isVideoMode) _mainForm.FrameChanged -= OnExternalFrameChanged;

            if (_isVideoMode && _mediaPlayer != null)
            {
                _transferTime = _mediaPlayer.Time;
                _mediaPlayer.EnableKeyInput = true;
                _mediaPlayer.EnableMouseInput = true;
                _mediaPlayer.Stop();
                if (_videoView != null) _videoView.MediaPlayer = null;

                if (!_mainForm.IsDisposed && _mainForm.MainVideoView != null)
                {
                    _mainForm.MainVideoView.MediaPlayer = _mediaPlayer;
#pragma warning disable CS4014
                    _mainForm.BeginInvoke((Action)(async () =>
                    {
                        bool wasMute = _mediaPlayer.Mute;
                        _mediaPlayer.Mute = true;
                        _mediaPlayer.Play();

                        int retries = 0;
                        while (!_mediaPlayer.IsSeekable && retries < 50) await Task.Delay(20);

                        if (_transferTime > 0) _mediaPlayer.Time = _transferTime;
                        _mainForm.NavigateFrames(0);

                        if (_transferTime > 1000)
                        {
                            int seekWait = 0;
                            while (Math.Abs(_mediaPlayer.Time - _transferTime) > 3000 && seekWait < 60)
                            {
                                await Task.Delay(25);
                                seekWait++;
                            }
                            await Task.Delay(200);      
                        }
                        else
                        {
                            await Task.Delay(100);
                        }

                        _mediaPlayer.Pause();
                        Task.Delay(100).ContinueWith(t => { if (_mediaPlayer != null) _mediaPlayer.Mute = wasMute; });
                    }));
#pragma warning restore CS4014
                }
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }
    }
}