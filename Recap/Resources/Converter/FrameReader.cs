using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RecapConverter
{
    public class FrameReader
    {
        private const int IoBufferSize = 65536;

        public List<FrameIndex> LoadIndices(string schPath)
        {
            if (File.Exists(schPath))
            {
                return LoadFromDataFile(schPath);
            }

            return new List<FrameIndex>();
        }

        public byte[] ReadFrameData(string schPath, FrameIndex frame)
        {
            try
            {
                using (var stream = new FileStream(schPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, IoBufferSize))
                {
                    if (frame.DataOffset + frame.DataLength > stream.Length) return null;

                    stream.Position = frame.DataOffset;
                    byte[] buffer = new byte[frame.DataLength];
                    int read = stream.Read(buffer, 0, frame.DataLength);
                    if (read != frame.DataLength) return null;
                    return buffer;
                }
            }
            catch
            {
                return null;
            }
        }


        private List<FrameIndex> LoadFromDataFile(string schPath)
        {
            var frames = new List<FrameIndex>();
            using (var stream = new FileStream(schPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                while (stream.Position < stream.Length)
                {
                    try
                    {
                        if (stream.Position + 12 > stream.Length) break;

                        long ticks = reader.ReadInt64();
                        int nameLen = reader.ReadInt32();

                        if (nameLen < 0 || nameLen > 10000) break;

                        byte[] nameBytes = reader.ReadBytes(nameLen);
                        string appName = Encoding.UTF8.GetString(nameBytes);

                        int dataLength = reader.ReadInt32();

                        if (dataLength < 0 || stream.Position + dataLength > stream.Length) break;

                        frames.Add(new FrameIndex
                        {
                            TimestampTicks = ticks,
                            DataOffset = stream.Position,
                            DataLength = dataLength,
                            AppName = appName
                        });

                        stream.Seek(dataLength, SeekOrigin.Current);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            return frames;
        }
    }
}
