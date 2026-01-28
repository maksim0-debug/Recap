using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Recap
{
    public static class IconGenerator
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
        public static Icon GenerateAppIcon()
        {
            Bitmap bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                int circleSize = 200;
                int circleX = (256 - circleSize) / 2;
                int circleY = (256 - circleSize) / 2;
                using (SolidBrush circleBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                {
                    g.FillEllipse(circleBrush, circleX, circleY, circleSize, circleSize);
                }

                int frameWidth = 90;
                int frameHeight = 70;
                int baseX = 60;
                int baseY = 95;

                Rectangle frame3 = new Rectangle(baseX - 15, baseY - 15, frameWidth, frameHeight);
                using (SolidBrush frameBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                using (Pen framePen = new Pen(Color.FromArgb(100, 100, 100), 3))
                {
                    g.FillRoundedRectangle(frameBrush, frame3, 8);
                    g.DrawRoundedRectangle(framePen, frame3, 8);
                }

                Rectangle frame2 = new Rectangle(baseX - 7, baseY - 7, frameWidth, frameHeight);
                using (SolidBrush frameBrush = new SolidBrush(Color.FromArgb(140, 140, 140)))
                using (Pen framePen = new Pen(Color.FromArgb(160, 160, 160), 3))
                {
                    g.FillRoundedRectangle(frameBrush, frame2, 8);
                    g.DrawRoundedRectangle(framePen, frame2, 8);
                }

                Rectangle frame1 = new Rectangle(baseX, baseY, frameWidth, frameHeight);
                using (SolidBrush frameBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
                using (Pen framePen = new Pen(Color.FromArgb(220, 220, 220), 3))
                {
                    g.FillRoundedRectangle(frameBrush, frame1, 8);
                    g.DrawRoundedRectangle(framePen, frame1, 8);
                }

                Rectangle innerFrame = new Rectangle(baseX + 8, baseY + 12, frameWidth - 16, frameHeight - 30);
                using (SolidBrush innerBrush = new SolidBrush(Color.FromArgb(230, 230, 230)))
                {
                    g.FillRectangle(innerBrush, innerFrame);
                }

                int buttonSize = 50;
                int buttonX = baseX + frameWidth + 10;
                int buttonY = baseY + frameHeight - 20;

                using (SolidBrush buttonBrush = new SolidBrush(Color.FromArgb(160, 160, 160)))
                {
                    g.FillEllipse(buttonBrush, buttonX, buttonY, buttonSize, buttonSize);
                }

                using (Pen buttonPen = new Pen(Color.FromArgb(180, 180, 180), 2))
                {
                    g.DrawEllipse(buttonPen, buttonX, buttonY, buttonSize, buttonSize);
                }

                Point[] triangle = new Point[]
                {
                new Point(buttonX + 15, buttonY + 10),
                new Point(buttonX + 15, buttonY + 40),
                new Point(buttonX + 40, buttonY + 25)
                };
                using (SolidBrush triangleBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
                {
                    g.FillPolygon(triangleBrush, triangle);
                }

                using (Pen dottedPen = new Pen(Color.FromArgb(140, 140, 140), 2))
                {
                    dottedPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                    g.DrawLine(dottedPen, baseX + frameWidth, baseY + frameHeight - 5, buttonX, buttonY + buttonSize / 2);
                }
            }

            IntPtr hIcon = IntPtr.Zero;
            Icon newIcon = null;
            try
            {
                hIcon = bmp.GetHicon();
                using (Icon tempIcon = Icon.FromHandle(hIcon))
                {
                    newIcon = (Icon)tempIcon.Clone();
                }
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                {
                    DestroyIcon(hIcon);
                }
                bmp.Dispose();
            }

            return newIcon;
        }

        public static Bitmap GetMessageBoxIcon(MessageBoxIcon icon, int size = 48)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                Color circleColor = Color.FromArgb(120, 120, 120);
                string symbol = "?";

                switch (icon)
                {
                    case MessageBoxIcon.Question:
                        circleColor = Color.FromArgb(0, 120, 215);
                        symbol = "?";
                        break;
                    case MessageBoxIcon.Information:
                        circleColor = Color.FromArgb(0, 120, 215);
                        symbol = "i";
                        break;
                    case MessageBoxIcon.Warning:
                        circleColor = Color.FromArgb(255, 185, 0);
                        symbol = "!";
                        break;
                    case MessageBoxIcon.Error:
                        circleColor = Color.FromArgb(232, 17, 35);
                        symbol = "×";
                        break;
                }

                using (SolidBrush b = new SolidBrush(circleColor))
                {
                    g.FillEllipse(b, 2, 2, size - 4, size - 4);
                }

                using (SolidBrush b = new SolidBrush(Color.White))
                {
                    float fontSize = size * 0.6f;
                    using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold))
                    {
                        var stringFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        g.DrawString(symbol, font, b, new RectangleF(0, 0, size, size), stringFormat);
                    }
                }
            }
            return bmp;
        }
    }
}