using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int N = 96;

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image not found", imagePath);

            using (var original = new Bitmap(imagePath))
            using (var cropped = CropCenterImportantArea(original))
            using (var normalized = NormalizeToSquare(cropped, N))
            {
                byte[] gray = ToGray(normalized);
                byte[] edge = EdgeDescriptor(gray, N, N);
                return edge;
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
            int inter = 0;
            int union = 0;
            int qCount = 0;
            int dCount = 0;

            for (int i = 0; i < q.Length; i++)
            {
                bool qe = q[i] > 0;
                bool de = d[i] > 0;

                if (qe) qCount++;
                if (de) dCount++;

                if (qe && de) inter++;
                if (qe || de) union++;
            }

            if (qCount < 10 || dCount < 10 || union == 0)
                return 0;

            double jaccard = inter * 100.0 / union;

            double densityRatio = Math.Min(qCount, dCount) * 1.0 / Math.Max(qCount, dCount);
            double densityPenalty = densityRatio * 100.0;

            return (jaccard * 0.75) + (densityPenalty * 0.25);
        }

        private Bitmap CropCenterImportantArea(Bitmap src)
        {
            // Customer photo me finger/jeans aa jate hain.
            // Isliye pehle center area pe focus.
            int cx1 = src.Width / 8;
            int cy1 = src.Height / 8;
            int cx2 = src.Width - cx1;
            int cy2 = src.Height - cy1;

            int minX = src.Width;
            int minY = src.Height;
            int maxX = 0;
            int maxY = 0;

            for (int y = cy1; y < cy2; y += 2)
            {
                for (int x = cx1; x < cx2; x += 2)
                {
                    Color c = src.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;

                    // Very dark + very bright contrast edges pakdo,
                    // normal skin/jeans ko ignore karne ki koshish.
                    if (gray < 80 || gray > 235)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX <= minX || maxY <= minY)
            {
                Rectangle center = new Rectangle(
                    src.Width / 4,
                    src.Height / 4,
                    src.Width / 2,
                    src.Height / 2
                );

                return src.Clone(center, src.PixelFormat);
            }

            int pad = 18;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(src.Width - 1, maxX + pad);
            maxY = Math.Min(src.Height - 1, maxY + pad);

            Rectangle rect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return src.Clone(rect, src.PixelFormat);
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
                    (size - 8) / (double)src.Width,
                    (size - 8) / (double)src.Height
                );

                int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                int h = Math.Max(1, (int)Math.Round(src.Height * scale));

                int x = (size - w) / 2;
                int y = (size - h) / 2;

                g.DrawImage(src, x, y, w, h);
            }

            return dst;
        }

        private byte[] ToGray(Bitmap bmp)
        {
            byte[] data = new byte[N * N];

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    data[y * N + x] = (byte)((c.R + c.G + c.B) / 3);
                }
            }

            return data;
        }

        private byte[] EdgeDescriptor(byte[] g, int w, int h)
        {
            byte[] e = new byte[w * h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;

                    int gx =
                        -g[(y - 1) * w + (x - 1)] + g[(y - 1) * w + (x + 1)] +
                        -2 * g[y * w + (x - 1)] + 2 * g[y * w + (x + 1)] +
                        -g[(y + 1) * w + (x - 1)] + g[(y + 1) * w + (x + 1)];

                    int gy =
                        -g[(y - 1) * w + (x - 1)] - 2 * g[(y - 1) * w + x] - g[(y - 1) * w + (x + 1)] +
                         g[(y + 1) * w + (x - 1)] + 2 * g[(y + 1) * w + x] + g[(y + 1) * w + (x + 1)];

                    int mag = Math.Abs(gx) + Math.Abs(gy);

                    e[i] = (byte)(mag > 85 ? 255 : 0);
                }
            }

            return e;
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
