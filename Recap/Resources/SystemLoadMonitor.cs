using System;
using System.Diagnostics;

namespace Recap
{
    public class SystemLoadMonitor : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly int _threshold;

        public SystemLoadMonitor(int threshold = 40)
        {
            _threshold = threshold;
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();      
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("SystemLoadMonitor.Init", ex);
            }
        }

        public bool IsHighLoad()
        {
            try
            {
                if (_cpuCounter == null) return false;
                
                float load = _cpuCounter.NextValue();
                
                int currentThreshold = AdvancedSettings.Instance.HighLoadCpuThreshold;
                
                return load > currentThreshold;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
        }
    }
}
