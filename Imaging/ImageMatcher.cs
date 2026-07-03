using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int DescriptorSize = 128;

        public static double Compare(string queryImagePath, string dbImagePath)
        {
            return CompareImages(queryImagePath, dbImagePath);
        }

        public static double CompareImages(string queryImagePath, string dbImagePath)
        {
            var matcher = new ImageMatcher();
            byte[] a = matcher.ExtractDescriptorBytes(queryImagePath);
            byte[] b = matcher.ExtractDescriptorBytes(dbImagePath);
            return matcher.Compare(a, b);
        }

        public double Compare(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0)
                return 0;

            int len = Math.Min(a.Length, b.Length);
            double diffSum = 0;
            int sameEdge = 0;
            int edgeCount = 0;

            for (int i = 0; i < len; i++)
            {
                int av = a[i];
                int bv = b[i];

                diffSum += Math.Abs(av - bv);

                if (av > 180 || bv > 180)
                {
                    edgeCount++;
                    if (Math.Abs(av - bv) < 50)
                        sameEdge++;
                }
            }

            double pixelScore = 100.0 - ((diffSum / len) / 255.0 * 100.0);
            double edgeScore = edgeCount == 0 ? 0 : sameEdge * 100.0 / edgeCount;

            double final = pixelScore * 0.55 + edgeScore * 0.45;

            if (final < 0) final = 0;
            if (final > 100) final = 100;

            return Math.Round(final, 2);
        }

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return new byte[0];

            try
            {
                using (Bitmap original = new Bitmap(imagePath))
                using (Bitmap resized = ResizeToSquare(original, DescriptorSize))
                using (Bitmap gray = ToGray(resized))
                using (Bitmap edge = EdgeDetect(gray))
                {
                    byte[] data = new byte[DescriptorSize * DescriptorSize];

                    int index = 0;
                    for (int y = 0; y < DescriptorSize; y++)
                    {
                        for (int x = 0; x < DescriptorSize; x++)
                        {
                            Color c = edge.GetPixel(x, y);
                            data[index++] = c.R;
                        }
                    }

                    return data;
                }
            }
            catch
            {
                return new byte[0];
            }
        }

        public Size ReadSize(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return new Size(0, 0);

            try
            {
                using (Bitmap bmp = new Bitmap(imagePath))
                {
                    return new Size(bmp.Width, bmp.Height);
                }
            }
            catch
            {
                return new Size(0, 0);
            }
        }

        public Size ReadSize(byte[] descriptorBytes)
        {
            return new Size(DescriptorSize, DescriptorSize);
        }

        private static Bitmap ResizeToSquare(Bitmap src, int size)
        {
            Bitmap dst = new Bitmap(size, size, PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                double scale = Math.Min(size / (double)src.Width, size / (double)src.Height);
                int w = Math.Max(1, (int)(src.Width * scale));
                int h = Math.Max(1, (int)(src.Height * scale));

                int x = (size - w) / 2;
                int y = (size - h) / 2;

                g.DrawImage(src, x, y, w, h);
            }

            return dst;
        }

        private static Bitmap ToGray(Bitmap src)
        {
            Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);

            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    Color c = src.GetPixel(x, y);
                    int g = (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
                    if (g < 0) g = 0;
                    if (g > 255) g = 255;
                    dst.SetPixel(x, y, Color.FromArgb(g, g, g));
                }
            }

            return dst;
        }

        private static Bitmap EdgeDetect(Bitmap gray)
        {
            Bitmap dst = new Bitmap(gray.Width, gray.Height, PixelFormat.Format24bppRgb);

            for (int y = 1; y < gray.Height - 1; y++)
            {
                for (int x = 1; x < gray.Width - 1; x++)
                {
                    int gx =
                        -GetGray(gray, x - 1, y - 1) + GetGray(gray, x + 1, y - 1) +
                        -2 * GetGray(gray, x - 1, y) + 2 * GetGray(gray, x + 1, y) +
                        -GetGray(gray, x - 1, y + 1) + GetGray(gray, x + 1, y + 1);

                    int gy =
                        -GetGray(gray, x - 1, y - 1) - 2 * GetGray(gray, x, y - 1) - GetGray(gray, x + 1, y - 1) +
                         GetGray(gray, x - 1, y + 1) + 2 * GetGray(gray, x, y + 1) + GetGray(gray, x + 1, y + 1);

                    int v = (int)Math.Sqrt(gx * gx + gy * gy);
                    if (v > 255) v = 255;

                    v = v > 45 ? 255 : 0;
                    dst.SetPixel(x, y, Color.FromArgb(v, v, v));
                }
            }

            return dst;
        }

        private static int GetGray(Bitmap bmp, int x, int y)
        {
            return bmp.GetPixel(x, y).R;
        }
    }
}
