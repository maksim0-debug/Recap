using System;

namespace RecapConverter
{
    public struct FrameIndex
    {
        public long TimestampTicks;
        public long DataOffset;
        public int DataLength;
        public string AppName;
        public int IntervalMs;

        public DateTime GetTime() => new DateTime(TimestampTicks);
    }
}
