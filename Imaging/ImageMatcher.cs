using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int Size128 = 128;
        private const int EdgeBytes = Size128 * Size128;

        public static double Compare(string queryImagePath, string dbImagePath)
        {
            return CompareImages(queryImagePath, dbImagePath);
        }

        public static double CompareImages(string queryImagePath, string dbImagePath)
        {
            ImageMatcher m = new ImageMatcher();
            return m.Compare(m.ExtractDescriptorBytes(queryImagePath), m.ExtractDescriptorBytes(dbImagePath));
        }

        public double Compare(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length < EdgeBytes || b.Length < EdgeBytes)
                return 0;

            double best = 0;

            for (int rot = 0; rot < 4; rot++)
            {
                double edge = EdgeScore(a, b, rot);
                double proj = ProjectionScore(a, b, rot);
                double dens = DensityScore(a, b);

                double score = edge * 0.65 + proj * 0.25 + dens * 0.10;

                if (score > best)
                    best = score;
            }

            if (best < 0) best = 0;
            if (best > 100) best = 100;

            return Math.Round(best, 2);
        }

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return new byte[0];

            try
            {
                using (Bitmap original = new Bitmap(imagePath))
                using (Bitmap normalized = NormalizeObject(original, Size128))
                using (Bitmap gray = ToGray(normalized))
                using (Bitmap edge = EdgeDetect(gray))
                {
                    byte[] data = new byte[EdgeBytes + 256 + 1];

                    int index = 0;
                    int white = 0;

                    for (int y = 0; y < Size128; y++)
                    {
                        for (int x = 0; x < Size128; x++)
                        {
                            byte v = edge.GetPixel(x, y).R > 0 ? (byte)255 : (byte)0;
                            data[index++] = v;
                            if (v > 0) white++;
                        }
                    }

                    for (int y = 0; y < Size128; y++)
                    {
                        int count = 0;
                        for (int x = 0; x < Size128; x++)
                            if (data[y * Size128 + x] > 0) count++;

                        data[index++] = (byte)Math.Min(255, count * 2);
                    }

                    for (int x = 0; x < Size128; x++)
                    {
                        int count = 0;
                        for (int y = 0; y < Size128; y++)
                            if (data[y * Size128 + x] > 0) count++;

                        data[index++] = (byte)Math.Min(255, count * 2);
                    }

                    data[index] = (byte)Math.Min(255, white * 255 / EdgeBytes);
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
            try
            {
                if (!File.Exists(imagePath)) return new Size(0, 0);
                using (Bitmap bmp = new Bitmap(imagePath))
                    return new Size(bmp.Width, bmp.Height);
            }
            catch
            {
                return new Size(0, 0);
            }
        }

        public Size ReadSize(byte[] descriptorBytes)
        {
            return new Size(Size128, Size128);
        }

        private static double EdgeScore(byte[] a, byte[] b, int rot)
        {
            int inter = 0;
            int union = 0;

            for (int y = 0; y < Size128; y++)
            {
                for (int x = 0; x < Size128; x++)
                {
                    int ai = y * Size128 + x;
                    int bi = RotIndex(x, y, rot);

                    bool av = a[ai] > 0;
                    bool bv = b[bi] > 0;

                    if (av && bv) inter++;
                    if (av || bv) union++;
                }
            }

            if (union == 0) return 0;

            double s = inter * 100.0 / union;
            return Clamp(s * 2.1);
        }

        private static double ProjectionScore(byte[] a, byte[] b, int rot)
        {
            int offset = EdgeBytes;
            double diff = 0;

            for (int i = 0; i < 256; i++)
            {
                int bi = i;

                if (rot == 1 || rot == 3)
                {
                    if (i < 128) bi = 128 + i;
                    else bi = i - 128;
                }

                diff += Math.Abs(a[offset + i] - b[offset + bi]);
            }

            double avg = diff / 256.0;
            return Clamp(100.0 - avg * 100.0 / 255.0);
        }

        private static double DensityScore(byte[] a, byte[] b)
        {
            if (a.Length <= EdgeBytes + 256 || b.Length <= EdgeBytes + 256)
                return 50;

            int da = a[EdgeBytes + 256];
            int db = b[EdgeBytes + 256];

            return Clamp(100 - Math.Abs(da - db) * 100.0 / 255.0);
        }

        private static int RotIndex(int x, int y, int rot)
        {
            if (rot == 1) return (Size128 - 1 - x) * Size128 + y;
            if (rot == 2) return (Size128 - 1 - y) * Size128 + (Size128 - 1 - x);
            if (rot == 3) return x * Size128 + (Size128 - 1 - y);
            return y * Size128 + x;
        }

        private static Bitmap NormalizeObject(Bitmap src, int size)
        {
            using (Bitmap small = ResizeToSquare(src, size))
            using (Bitmap gray = ToGray(small))
            using (Bitmap edge = EdgeDetect(gray))
            {
                Rectangle box = FindObjectBox(edge);

                if (box.Width < 10 || box.Height < 10)
                    return ResizeToSquare(src, size);

                Bitmap cropped = small.Clone(box, PixelFormat.Format24bppRgb);
                Bitmap result = ResizeToSquare(cropped, size);
                cropped.Dispose();
                return result;
            }
        }

        private static Rectangle FindObjectBox(Bitmap edge)
        {
            int minX = edge.Width, minY = edge.Height, maxX = 0, maxY = 0;

            for (int y = 0; y < edge.Height; y++)
            {
                for (int x = 0; x < edge.Width; x++)
                {
                    if (edge.GetPixel(x, y).R > 0)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX <= minX || maxY <= minY)
                return new Rectangle(0, 0, edge.Width, edge.Height);

            int pad = 8;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(edge.Width - 1, maxX + pad);
            maxY = Math.Min(edge.Height - 1, maxY + pad);

            return Rectangle.FromLTRB(minX, minY, maxX, maxY);
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

                g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
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
                        -Get(gray, x - 1, y - 1) + Get(gray, x + 1, y - 1) +
                        -2 * Get(gray, x - 1, y) + 2 * Get(gray, x + 1, y) +
                        -Get(gray, x - 1, y + 1) + Get(gray, x + 1, y + 1);

                    int gy =
                        -Get(gray, x - 1, y - 1) - 2 * Get(gray, x, y - 1) - Get(gray, x + 1, y - 1) +
                         Get(gray, x - 1, y + 1) + 2 * Get(gray, x, y + 1) + Get(gray, x + 1, y + 1);

                    int v = (int)Math.Sqrt(gx * gx + gy * gy);
                    v = v > 38 ? 255 : 0;

                    dst.SetPixel(x, y, Color.FromArgb(v, v, v));
                }
            }

            return dst;
        }

        private static int Get(Bitmap bmp, int x, int y)
        {
            return bmp.GetPixel(x, y).R;
        }

        private static double Clamp(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }
    }
}
