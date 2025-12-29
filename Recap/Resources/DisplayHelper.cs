using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Management;
using System.Linq;

namespace Recap
{
    public static class DisplayHelper
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        public static Dictionary<string, string> GetMonitorFriendlyNames()
        {
            var result = new Dictionary<string, string>();
            
            try
            {
                using (var searcher = new ManagementObjectSearcher("ROOT\\WMI", "SELECT * FROM WmiMonitorID"))
                {
                    foreach (ManagementObject queryObj in searcher.Get())
                    {
                        string instanceName = queryObj["InstanceName"] as string;
                        if (string.IsNullOrEmpty(instanceName)) continue;

                        var userFriendlyNameCodes = queryObj["UserFriendlyName"] as ushort[];
                        string friendlyName = null;
                        if (userFriendlyNameCodes != null)
                        {
                            friendlyName = new string(userFriendlyNameCodes.Select(c => (char)c).ToArray()).Trim('\0');
                        }

                        if (string.IsNullOrEmpty(friendlyName))
                        {
                            var manufacturerCodes = queryObj["ManufacturerName"] as ushort[];
                            var productCodes = queryObj["ProductCodeID"] as ushort[];
                            
                            string manufacturer = manufacturerCodes != null ? new string(manufacturerCodes.Select(c => (char)c).ToArray()).Trim('\0') : "Unknown";
                            string product = productCodes != null ? new string(productCodes.Select(c => (char)c).ToArray()).Trim('\0') : "Monitor";
                            friendlyName = $"{manufacturer} {product}";
                        }

                    }
                }
            }
            catch { }

            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);

            try
            {
                for (uint id = 0; EnumDisplayDevices(null, id, ref d, 0); id++)
                {
                    if ((d.StateFlags & 1) != 0)  
                    {
                        string deviceName = d.DeviceName;
                        string friendlyName = d.DeviceString;
                        string deviceID = "";

                        DISPLAY_DEVICE dMonitor = new DISPLAY_DEVICE();
                        dMonitor.cb = Marshal.SizeOf(dMonitor);
                        if (EnumDisplayDevices(d.DeviceName, 0, ref dMonitor, 0))
                        {
                            if (!string.IsNullOrEmpty(dMonitor.DeviceString))
                            {
                                friendlyName = dMonitor.DeviceString;
                            }
                            deviceID = dMonitor.DeviceID;   
                        }

                        if (friendlyName.Contains("Generic") || friendlyName.Contains("PnP"))
                        {
                            if (!string.IsNullOrEmpty(deviceID))
                            {
                                var parts = deviceID.Split('\\');
                                if (parts.Length >= 2)
                                {
                                    string idPart = parts[1];  
                                    friendlyName += $" ({idPart})";
                                }
                            }
                        }

                        if (!result.ContainsKey(deviceName))
                        {
                            result.Add(deviceName, friendlyName);
                        }
                    }
                    d.cb = Marshal.SizeOf(d);
                }
            }
            catch { }

            return result;
        }
    }
}
