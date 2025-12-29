using System;
using System.Linq;
using System.Windows.Forms;
using LibVLCSharp.Shared; 

namespace Recap
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.ThreadException += (s, e) =>
            {
                DebugLogger.LogError("Application.ThreadException", e.Exception);
                MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\nSee log for details: {DebugLogger.GetLogFilePath()}", "Recap Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                DebugLogger.LogError("AppDomain.UnhandledException", ex);
                MessageBox.Show($"A critical error occurred: {ex?.Message}\nSee log for details: {DebugLogger.GetLogFilePath()}", "Recap Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                DebugLogger.Log("Application starting...");
                Core.Initialize();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool autoStart = args.Contains("/autostart");

                Application.Run(new MainForm(autoStart));
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("Main", ex);
                MessageBox.Show($"Fatal startup error: {ex.Message}", "Recap Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}