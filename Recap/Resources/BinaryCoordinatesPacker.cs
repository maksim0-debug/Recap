using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Recap
{
    public static class BinaryCoordinatesPacker
    {
        private const ushort MAX_UINT16 = 65535;

        public static byte[] Pack(List<OcrService.WordData> words)
        {
            if (words == null || words.Count == 0) return null;

            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var gzip = new GZipStream(ms, CompressionLevel.Optimal))
                    using (var writer = new BinaryWriter(gzip))
                    {
                        writer.Write((ushort)Math.Min(words.Count, MAX_UINT16));

                        foreach (var word in words)
                        {
                            if (string.IsNullOrEmpty(word.T)) continue;

                            byte[] textBytes = Encoding.UTF8.GetBytes(word.T);
                            
                            if (textBytes.Length > 255) 
                            {
                                Array.Resize(ref textBytes, 255);
                            }

                            writer.Write((byte)textBytes.Length);
                            writer.Write(textBytes);

                            writer.Write(FloatToUInt16(word.X));
                            writer.Write(FloatToUInt16(word.Y));
                            writer.Write(FloatToUInt16(word.W));
                            writer.Write(FloatToUInt16(word.H));
                        }
                    }
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("BinaryCoordinatesPacker.Pack", ex);
                return null;
            }
        }

        public static List<OcrService.WordData> Unpack(byte[] compressedData)
        {
            var result = new List<OcrService.WordData>();
            if (compressedData == null || compressedData.Length == 0) return result;

            try
            {
                using (var ms = new MemoryStream(compressedData))
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (var reader = new BinaryReader(gzip))
                {
                    ushort count = reader.ReadUInt16();

                    for (int i = 0; i < count; i++)
                    {
                        byte textLength = reader.ReadByte();
                        
                        byte[] textBytes = reader.ReadBytes(textLength);
                        string text = Encoding.UTF8.GetString(textBytes);

                        ushort xRaw = reader.ReadUInt16();
                        ushort yRaw = reader.ReadUInt16();
                        ushort wRaw = reader.ReadUInt16();
                        ushort hRaw = reader.ReadUInt16();

                        result.Add(new OcrService.WordData
                        {
                            T = text,
                            X = UInt16ToFloat(xRaw),
                            Y = UInt16ToFloat(yRaw),
                            W = UInt16ToFloat(wRaw),
                            H = UInt16ToFloat(hRaw)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("BinaryCoordinatesPacker.Unpack", ex);
            }

            return result;
        }

        private static ushort FloatToUInt16(float value)
        {
            if (value < 0f) value = 0f;
            if (value > 1f) value = 1f;

            return (ushort)(value * MAX_UINT16);
        }

        private static float UInt16ToFloat(ushort value)
        {
            return value / (float)MAX_UINT16;
        }
    }
}
