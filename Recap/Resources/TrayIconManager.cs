using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        
        public event EventHandler ShowRequested;
        public event EventHandler ExitRequested;

        public TrayIconManager(Icon icon, string title)
        {
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("", null, (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty));
            trayMenu.Items.Add("", null, (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty));

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = title,
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            _notifyIcon.DoubleClick += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
            
            UpdateLocalization();
        }

        public void UpdateLocalization()
        {
             if (_notifyIcon.ContextMenuStrip != null && _notifyIcon.ContextMenuStrip.Items.Count >= 2)
            {
                _notifyIcon.ContextMenuStrip.Items[0].Text = Localization.Get("trayShow");
                _notifyIcon.ContextMenuStrip.Items[1].Text = Localization.Get("trayExit");
            }
        }

        public void SetVisible(bool visible)
        {
            _notifyIcon.Visible = visible;
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}
