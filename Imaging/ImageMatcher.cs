using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int N = 64;

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image not found", imagePath);

            using (var original = new Bitmap(imagePath))
            using (var cropped = CropImportantArea(original))
            using (var normalized = NormalizeToSquare(cropped, N))
            {
                byte[] data = new byte[N * N];

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        Color c = normalized.GetPixel(x, y);
                        int gray = (c.R + c.G + c.B) / 3;

                        // WhatsApp/photo blur ke liye thoda soft threshold.
                        data[y * N + x] = (byte)(gray < 200 ? 0 : 255);
                    }
                }

                return data;
            }
        }

        public double Compare(byte[] queryDescriptor, byte[] indexedDescriptor)
        {
            if (queryDescriptor == null || indexedDescriptor == null)
                return 0;

            if (queryDescriptor.Length != N * N || indexedDescriptor.Length != N * N)
                return 0;

            double best = 0;

            best = Math.Max(best, CompareSame(queryDescriptor, indexedDescriptor));
            best = Math.Max(best, CompareSame(Rotate90(queryDescriptor), indexedDescriptor));
            best = Math.Max(best, CompareSame(Rotate180(queryDescriptor), indexedDescriptor));
            best = Math.Max(best, CompareSame(Rotate270(queryDescriptor), indexedDescriptor));

            if (best < 0) best = 0;
            if (best > 100) best = 100;

            return best;
        }

        public Size ReadSize(string imagePath)
        {
            using (var img = Image.FromFile(imagePath))
                return img.Size;
        }

        private double CompareSame(byte[] q, byte[] d)
        {
            int sameDark = 0;
            int queryDark = 0;
            int indexedDark = 0;

            for (int i = 0; i < q.Length; i++)
            {
                bool qDark = q[i] < 128;
                bool dDark = d[i] < 128;

                if (qDark) queryDark++;
                if (dDark) indexedDark++;
                if (qDark && dDark) sameDark++;
            }

            if (queryDark == 0 || indexedDark == 0)
                return 0;

            double a = sameDark * 100.0 / queryDark;
            double b = sameDark * 100.0 / indexedDark;

            return (a + b) / 2.0;
        }

        private Bitmap NormalizeToSquare(Bitmap src, int size)
        {
            Bitmap dst = new Bitmap(size, size);

            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                double scale = Math.Min(
                    (size - 4) / (double)src.Width,
                    (size - 4) / (double)src.Height
                );

                int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                int h = Math.Max(1, (int)Math.Round(src.Height * scale));

                int x = (size - w) / 2;
                int y = (size - h) / 2;

                g.DrawImage(src, x, y, w, h);
            }

            return dst;
        }

        private Bitmap CropImportantArea(Bitmap src)
        {
            int minX = src.Width;
            int minY = src.Height;
            int maxX = 0;
            int maxY = 0;

            for (int y = 0; y < src.Height; y += 2)
            {
                for (int x = 0; x < src.Width; x += 2)
                {
                    Color c = src.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;

                    // Background white/light hota hai, design dark hoti hai.
                    if (gray < 220)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX <= minX || maxY <= minY)
                return new Bitmap(src);

            int pad = 12;

            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(src.Width - 1, maxX + pad);
            maxY = Math.Min(src.Height - 1, maxY + pad);

            Rectangle rect = new Rectangle(
                minX,
                minY,
                maxX - minX + 1,
                maxY - minY + 1
            );

            return src.Clone(rect, src.PixelFormat);
        }

        private byte[] Rotate90(byte[] src)
        {
            byte[] dst = new byte[N * N];

            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    dst[x * N + (N - 1 - y)] = src[y * N + x];

            return dst;
        }

        private byte[] Rotate180(byte[] src)
        {
            byte[] dst = new byte[N * N];

            for (int i = 0; i < src.Length; i++)
                dst[src.Length - 1 - i] = src[i];

            return dst;
        }

        private byte[] Rotate270(byte[] src)
        {
            byte[] dst = new byte[N * N];

            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    dst[(N - 1 - x) * N + y] = src[y * N + x];

            return dst;
        }
    }
}
