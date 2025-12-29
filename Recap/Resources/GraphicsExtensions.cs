using System.Drawing;
using System.Drawing.Drawing2D;

namespace Recap
{
    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (GraphicsPath path = GetRoundedRectanglePath(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (GraphicsPath path = GetRoundedRectanglePath(rect, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath GetRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.X + rect.Width - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.X + rect.Width - diameter, rect.Y + rect.Height - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        public static Color GetDominantColor(this Bitmap bmp)
        {
            if (bmp == null) return Color.Gray;

            try
            {
                using (var small = new Bitmap(bmp, new Size(32, 32)))
                {
                    var colorCounts = new System.Collections.Generic.Dictionary<int, int>();
                    int maxCount = 0;
                    int bestColorArgb = Color.Gray.ToArgb();

                    for (int x = 0; x < small.Width; x++)
                    {
                        for (int y = 0; y < small.Height; y++)
                        {
                            Color c = small.GetPixel(x, y);
                            if (c.A < 200) continue;   

                            if (c.GetBrightness() < 0.15f || c.GetBrightness() > 0.85f) continue;

                            int r = (c.R / 20) * 20;
                            int g = (c.G / 20) * 20;
                            int b = (c.B / 20) * 20;
                            
                            int argb = (255 << 24) | (r << 16) | (g << 8) | b;

                            if (!colorCounts.ContainsKey(argb)) colorCounts[argb] = 0;
                            
                            int weight = 1;

                            float saturation = c.GetSaturation();
                            if (saturation > 0.2f) weight += 5;
                            if (saturation > 0.5f) weight += 10;
                            if (saturation > 0.8f) weight += 15;

                            colorCounts[argb] += weight;

                            if (colorCounts[argb] > maxCount)
                            {
                                maxCount = colorCounts[argb];
                                bestColorArgb = argb;
                            }
                        }
                    }

                    if (maxCount == 0) return GetAverageColor(small);

                    return Color.FromArgb(bestColorArgb);
                }
            }
            catch { return Color.Gray; }
        }

        private static Color GetAverageColor(Bitmap bmp)
        {
            long r = 0, g = 0, b = 0;
            int count = 0;
            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 50)
                    {
                        r += c.R; g += c.G; b += c.B;
                        count++;
                    }
                }
            }
            return count == 0 ? Color.Gray : Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
        }
    }
}