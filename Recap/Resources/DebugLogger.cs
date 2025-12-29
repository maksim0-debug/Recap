using System;
using System.IO;

namespace Recap
{
    public static class DebugLogger
    {
        private static readonly bool LoggingEnabled = AdvancedSettings.Instance.EnableDebugLogging;

        private static readonly object _logLock = new object();
        private static string _logFilePath;

        static DebugLogger()
        {
            if (!LoggingEnabled) return;

            string tempPath = Path.GetTempPath();
            _logFilePath = Path.Combine(tempPath, $"Recap_Debug_{DateTime.Now:yyyy-MM-dd}.log");
        }

        public static void Log(string message)
        {
            if (!LoggingEnabled) return;

            try
            {
                lock (_logLock)
                {
                    string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    System.Diagnostics.Debug.WriteLine(logMessage);
                    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void LogError(string method, Exception ex)
        {
            if (!LoggingEnabled) return;

            Log($"ERROR in {method}: {ex.GetType().Name} - {ex.Message}\nStackTrace: {ex.StackTrace}");
        }

        public static string GetLogFilePath() => LoggingEnabled ? _logFilePath : "Logging is disabled.";
    }
}