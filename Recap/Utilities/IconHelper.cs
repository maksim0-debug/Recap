using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Recap.Utilities
{
    public static class IconHelper
    {
        public static Bitmap ProcessUserImage(string inputPath, int targetSize = 32)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Image file not found", inputPath);

            using (var original = Image.FromFile(inputPath))
            {
                int minDim = Math.Min(original.Width, original.Height);
                int x = (original.Width - minDim) / 2;
                int y = (original.Height - minDim) / 2;

                var cropRect = new Rectangle(x, y, minDim, minDim);

                var result = new Bitmap(targetSize, targetSize);
                
                using (var g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    g.DrawImage(original, new Rectangle(0, 0, targetSize, targetSize), cropRect, GraphicsUnit.Pixel);
                }

                return result;
            }
        }
    }
}
