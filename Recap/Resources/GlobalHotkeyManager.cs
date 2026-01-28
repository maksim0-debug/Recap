using System;
using System.Windows.Forms;

namespace Recap
{
    public class GlobalHotkeyManager : IMessageFilter
    {
        public event Action<int> NavigationRequested;
        private readonly Form _targetForm;

        public GlobalHotkeyManager(Form targetForm)
        {
            _targetForm = targetForm;
            Application.AddMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == 0x020A && Form.ActiveForm == _targetForm)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);

                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    int frames = (delta > 0 ? 100 : -100);
                    NavigationRequested?.Invoke(frames);
                    return true;
                }
                else if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                {
                    int frames = (delta > 0 ? 10 : -10);
                    NavigationRequested?.Invoke(frames);
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            Application.RemoveMessageFilter(this);
        }
    }
}
