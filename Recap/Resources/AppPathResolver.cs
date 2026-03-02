using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Recap
{
    public class AppPathResolver
    {
        private readonly OcrDatabase _db;

        public AppPathResolver(OcrDatabase db)
        {
            _db = db;
        }

        public async Task<string> ResolveAsync(string appName)
        {
            return await Task.Run(() => Resolve(appName));
        }

        private string Resolve(string appName)
        {
            string[] candidates = new[]
            {
                _db.GetExecutablePath(appName),
                GetPathFromLiveProcess(appName),
                GetPathFromRegistry(appName)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                if (i > 0)
                {
                    Task.Run(() =>
                    {
                        try { _db.UpdateExecutablePath(appName, path); } catch { }
                    });
                }

                return path;
            }

            return null;
        }

        private string GetPathFromLiveProcess(string appName)
        {
            try
            {
                string processName = appName;
                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    processName = processName.Substring(0, processName.Length - 4);
                }

                Process[] processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        string path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path))
                        {
                            return path;
                        }
                    }
                    catch (Win32Exception)
                    {
                        // Access denied
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return null;
        }

        private string GetPathFromRegistry(string appName)
        {
            try
            {
                string exeName = appName;
                if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeName += ".exe";
                }

                string subKey = $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        string val = key.GetValue("") as string;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        string val = key.GetValue("") as string;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return null;
        }
    }
}
