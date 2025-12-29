using System.Windows.Forms;

namespace Recap
{
    public class UIStateManager
    {
        private readonly Button _btnStart;
        private readonly Button _btnStop;
        private readonly Button _btnBrowse;
        private readonly TextBox _txtStoragePath;
        private readonly Button _btnSettings;
        private readonly Label _lblStatus;

        public UIStateManager(Button btnStart, Button btnStop, Button btnBrowse, TextBox txtStoragePath, Button btnSettings, Label lblStatus)
        {
            _btnStart = btnStart;
            _btnStop = btnStop;
            _btnBrowse = btnBrowse;
            _txtStoragePath = txtStoragePath;
            _btnSettings = btnSettings;
            _lblStatus = lblStatus;
        }

        public void SetState(bool isCapturing)
        {
            _btnStart.Enabled = !isCapturing;
            _btnStop.Enabled = isCapturing;
            _btnBrowse.Enabled = !isCapturing;
            _txtStoragePath.Enabled = !isCapturing;
            _btnSettings.Enabled = true;   

            _lblStatus.Text = isCapturing ? Localization.Get("statusWorking") : Localization.Get("statusStopped");
            _lblStatus.ForeColor = isCapturing
                ? System.Drawing.Color.FromArgb(16, 124, 16)   
                : System.Drawing.Color.FromArgb(196, 43, 28);   
        }

        public void SetControlsEnabled(bool isEnabled, params Control[] controls)
        {
            foreach (var control in controls)
            {
                if (control != null)
                {
                    control.Enabled = isEnabled;
                }
            }
        }
    }
}