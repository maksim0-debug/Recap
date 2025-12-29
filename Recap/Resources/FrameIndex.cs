using System;

namespace Recap
{
    public struct FrameIndex
    {
        public long TimestampTicks;
        public long DataOffset;          
        public int DataLength;          
        public string AppName;
        public int IntervalMs;

        public DateTime GetTime() => new DateTime(TimestampTicks);

        public bool IsVideoFrame => DataLength == -1;
    }

    public struct MiniFrame
    {
        public long TimestampTicks;
        public int AppId;
        public int IntervalMs;

        public DateTime GetTime() => new DateTime(TimestampTicks);
    }
}