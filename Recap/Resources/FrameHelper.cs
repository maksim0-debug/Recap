using System;
using System.Collections.Generic;
using System.Linq;

namespace Recap
{
    public static class FrameHelper
    {
        public static (string ExeName, string WebDomain) ParseAppName(string rawAppName)
        {
            if (string.IsNullOrEmpty(rawAppName)) return ("Unknown", null);

            int separatorIndex = rawAppName.IndexOf('|');
            if (separatorIndex > 0)
            {
                string exe = rawAppName.Substring(0, separatorIndex);
                string domain = rawAppName.Substring(separatorIndex + 1);
                return (exe, domain);
            }

            return (rawAppName, null);
        }

        public static string FormatDuration(long totalMs)
        {
            var ts = TimeSpan.FromMilliseconds(totalMs);

            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}ч {ts.Minutes}м";

            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}м {ts.Seconds}с";

            return $"{ts.Seconds}с";
        }

        public static long[] CalculateFrameDurations(List<FrameIndex> frames)
        {
            if (frames == null || frames.Count == 0) return new long[0];
            
            long[] durations = new long[frames.Count];
            long[] intervals = new long[frames.Count];

            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].IntervalMs > 0)
                {
                    intervals[i] = frames[i].IntervalMs;
                }
                else
                {
                    intervals[i] = DetectInterval(frames, i);
                }
            }

            long lastInterval = 3000;   
            for (int i = 0; i < intervals.Length; i++)
            {
                if (intervals[i] > 0) lastInterval = intervals[i];
                else intervals[i] = lastInterval;
            }
            for (int i = intervals.Length - 1; i >= 0; i--)
            {
                if (intervals[i] > 0) lastInterval = intervals[i];
                else intervals[i] = lastInterval;
            }

            for (int i = 0; i < frames.Count; i++)
            {
                long currentInterval = intervals[i];
                if (currentInterval <= 0) currentInterval = 3000;

                if (i == frames.Count - 1)
                {
                    durations[i] = currentInterval;
                }
                else
                {
                    var f = frames[i];
                    var nextF = frames[i + 1];
                    long diffMs = (nextF.TimestampTicks - f.TimestampTicks) / 10000;

                    if (diffMs > 0 && diffMs <= 90000)
                    {
                        durations[i] = diffMs;
                    }
                    else
                    {
                        durations[i] = currentInterval;
                    }
                }
            }

            return durations;
        }

        private static long DetectInterval(List<FrameIndex> frames, int startIndex)
        {
            int count = Math.Min(10, frames.Count - 1 - startIndex);
            if (count < 2) return 0;

            long[] keys = new long[10];
            int[] values = new int[10];
            int distinctCount = 0;
            int validDiffs = 0;

            for (int j = 0; j < count; j++)
            {
                var f1 = frames[startIndex + j];
                var f2 = frames[startIndex + j + 1];
                long diff = (f2.TimestampTicks - f1.TimestampTicks) / 10000;
                
                if (diff >= 200 && diff <= 30000)
                {
                    validDiffs++;
                    long rounded = (long)Math.Round(diff / 100.0) * 100;
                    
                    int index = -1;
                    for(int k=0; k<distinctCount; k++)
                    {
                        if (keys[k] == rounded)
                        {
                            index = k;
                            break;
                        }
                    }

                    if (index != -1)
                    {
                        values[index]++;
                    }
                    else if (distinctCount < 10)
                    {
                        keys[distinctCount] = rounded;
                        values[distinctCount] = 1;
                        distinctCount++;
                    }
                }
            }

            if (validDiffs == 0) return 0;

            int maxCount = 0;
            long bestKey = 0;

            for(int k=0; k<distinctCount; k++)
            {
                if (values[k] > maxCount)
                {
                    maxCount = values[k];
                    bestKey = keys[k];
                }
            }

            if (maxCount >= Math.Max(1, validDiffs * 0.3))
            {
                return bestKey;
            }

            return 0;
        }
    }
}