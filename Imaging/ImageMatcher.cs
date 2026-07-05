using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int S = 128;
        private const int Pixels = S * S;
        private const int MaskOffset = 0;
        private const int EdgeOffset = Pixels;
        private const int ProjOffset = Pixels * 2;
        private const int BlockOffset = ProjOffset + 256;
        private const int FeatureOffset = BlockOffset + 256;
        private const int TotalBytes = FeatureOffset + 16;

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
            if (a == null || b == null || a.Length < TotalBytes || b.Length < TotalBytes)
                return 0;

            double best = 0;

            for (int rot = 0; rot < 4; rot++)
            {
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    double mask = BestOverlap(a, b, MaskOffset, rot, mirror);
                    double edge = BestOverlap(a, b, EdgeOffset, rot, mirror);
                    double proj = ProjectionScore(a, b, rot, mirror);
                    double block = BlockScore(a, b, rot, mirror);
                    double feat = FeatureScore(a, b);

                    double score =
                        edge * 0.34 +
                        mask * 0.30 +
                        proj * 0.16 +
                        block * 0.12 +
                        feat * 0.08;

                    if (score > best) best = score;
                }
            }

            if (best < 18) best *= 0.55;
            return Math.Round(Clamp(best), 2);
        }

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return new byte[0];

            try
            {
                using (Bitmap src = new Bitmap(imagePath))
                using (Bitmap crop = CropJewellery(src))
                using (Bitmap norm = ResizeToSquare(crop, S))
                using (Bitmap gray = ToGray(norm))
                {
                    byte[] data = new byte[TotalBytes];

                    BuildMaskAndEdge(norm, gray, data);
                    BuildProjection(data);
                    BuildBlocks(data);
                    BuildFeatures(data);

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
            return new Size(S, S);
        }

        private static void BuildMaskAndEdge(Bitmap color, Bitmap gray, byte[] data)
        {
            for (int y = 1; y < S - 1; y++)
            {
                for (int x = 1; x < S - 1; x++)
                {
                    Color c = color.GetPixel(x, y);
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;
                    int bright = (c.R + c.G + c.B) / 3;

                    bool jewelryColor =
                        (sat > 28 && bright > 35 && bright < 245) ||
                        (bright < 170 && sat > 12);

                    int gx =
                        -G(gray, x - 1, y - 1) + G(gray, x + 1, y - 1) +
                        -2 * G(gray, x - 1, y) + 2 * G(gray, x + 1, y) +
                        -G(gray, x - 1, y + 1) + G(gray, x + 1, y + 1);

                    int gy =
                        -G(gray, x - 1, y - 1) - 2 * G(gray, x, y - 1) - G(gray, x + 1, y - 1) +
                         G(gray, x - 1, y + 1) + 2 * G(gray, x, y + 1) + G(gray, x + 1, y + 1);

                    int mag = (int)Math.Sqrt(gx * gx + gy * gy);

                    bool edge = mag > 34;
                    bool darkVector = bright < 210 && edge;

                    int idx = y * S + x;
                    data[MaskOffset + idx] = (jewelryColor || darkVector) ? (byte)255 : (byte)0;
                    data[EdgeOffset + idx] = edge ? (byte)255 : (byte)0;
                }
            }

            CleanNoise(data, MaskOffset);
            CleanNoise(data, EdgeOffset);
        }

        private static void CleanNoise(byte[] data, int offset)
        {
            byte[] copy = new byte[Pixels];
            Array.Copy(data, offset, copy, 0, Pixels);

            for (int y = 1; y < S - 1; y++)
            {
                for (int x = 1; x < S - 1; x++)
                {
                    int count = 0;
                    for (int yy = -1; yy <= 1; yy++)
                        for (int xx = -1; xx <= 1; xx++)
                            if (copy[(y + yy) * S + (x + xx)] > 0) count++;

                    int idx = y * S + x;
                    if (copy[idx] > 0 && count < 2) data[offset + idx] = 0;
                    if (copy[idx] == 0 && count >= 6) data[offset + idx] = 255;
                }
            }
        }

        private static void BuildProjection(byte[] data)
        {
            int p = ProjOffset;

            for (int y = 0; y < S; y++)
            {
                int count = 0;
                for (int x = 0; x < S; x++)
                    if (data[EdgeOffset + y * S + x] > 0) count++;

                data[p++] = (byte)Math.Min(255, count * 2);
            }

            for (int x = 0; x < S; x++)
            {
                int count = 0;
                for (int y = 0; y < S; y++)
                    if (data[EdgeOffset + y * S + x] > 0) count++;

                data[p++] = (byte)Math.Min(255, count * 2);
            }
        }

        private static void BuildBlocks(byte[] data)
        {
            int p = BlockOffset;
            int block = 8;

            for (int by = 0; by < 16; by++)
            {
                for (int bx = 0; bx < 16; bx++)
                {
                    int count = 0;

                    for (int y = by * block; y < by * block + block; y++)
                        for (int x = bx * block; x < bx * block + block; x++)
                            if (data[MaskOffset + y * S + x] > 0) count++;

                    data[p++] = (byte)Math.Min(255, count * 4);
                }
            }
        }

        private static void BuildFeatures(byte[] data)
        {
            int count = 0;
            int edge = 0;
            int minX = S, minY = S, maxX = 0, maxY = 0;
            double sx = 0, sy = 0;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int idx = y * S + x;

                    if (data[MaskOffset + idx] > 0)
                    {
                        count++;
                        sx += x;
                        sy += y;

                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }

                    if (data[EdgeOffset + idx] > 0)
                        edge++;
                }
            }

            int w = Math.Max(1, maxX - minX + 1);
            int h = Math.Max(1, maxY - minY + 1);

            int p = FeatureOffset;
            data[p++] = (byte)Math.Min(255, count * 255 / Pixels);
            data[p++] = (byte)Math.Min(255, edge * 255 / Pixels);
            data[p++] = (byte)Math.Min(255, w * 2);
            data[p++] = (byte)Math.Min(255, h * 2);
            data[p++] = (byte)(count == 0 ? 64 : Math.Min(255, (int)(sx / count * 2)));
            data[p++] = (byte)(count == 0 ? 64 : Math.Min(255, (int)(sy / count * 2)));
            data[p++] = (byte)Math.Min(255, Math.Abs(w - h) * 2);
            data[p++] = (byte)Math.Min(255, (w * h) * 255 / Pixels);

            while (p < TotalBytes)
                data[p++] = 0;
        }

        private static double BestOverlap(byte[] a, byte[] b, int offset, int rot, int mirror)
        {
            double best = 0;

            int[] shifts = { -6, -3, 0, 3, 6 };

            for (int sy = 0; sy < shifts.Length; sy++)
            {
                for (int sx = 0; sx < shifts.Length; sx++)
                {
                    double s = OverlapScore(a, b, offset, rot, mirror, shifts[sx], shifts[sy]);
                    if (s > best) best = s;
                }
            }

            return best;
        }

        private static double OverlapScore(byte[] a, byte[] b, int offset, int rot, int mirror, int dx, int dy)
        {
            int inter = 0;
            int union = 0;
            int ac = 0;
            int bc = 0;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int bx = x + dx;
                    int by = y + dy;

                    bool av = a[offset + y * S + x] > 0;
                    bool bv = false;

                    if (bx >= 0 && bx < S && by >= 0 && by < S)
                    {
                        int bi = TransformIndex(bx, by, rot, mirror);
                        bv = b[offset + bi] > 0;
                    }

                    if (av) ac++;
                    if (bv) bc++;
                    if (av && bv) inter++;
                    if (av || bv) union++;
                }
            }

            if (union == 0 || ac == 0 || bc == 0) return 0;

            double iou = inter * 100.0 / union;
            double coverA = inter * 100.0 / ac;
            double coverB = inter * 100.0 / bc;

            return Clamp(iou * 1.55 + Math.Min(coverA, coverB) * 0.35);
        }

        private static double ProjectionScore(byte[] a, byte[] b, int rot, int mirror)
        {
            double diff = 0;

            for (int i = 0; i < 256; i++)
            {
                int bi = i;

                if (rot == 1 || rot == 3)
                    bi = i < 128 ? 128 + i : i - 128;

                if (mirror == 1 && bi >= 128)
                    bi = 128 + (127 - (bi - 128));

                diff += Math.Abs(a[ProjOffset + i] - b[ProjOffset + bi]);
            }

            return Clamp(100.0 - (diff / 256.0) * 100.0 / 255.0);
        }

        private static double BlockScore(byte[] a, byte[] b, int rot, int mirror)
        {
            double diff = 0;

            for (int by = 0; by < 16; by++)
            {
                for (int bx = 0; bx < 16; bx++)
                {
                    int ai = by * 16 + bx;
                    int tx = bx;
                    int ty = by;

                    if (mirror == 1)
                        tx = 15 - tx;

                    int rx = tx;
                    int ry = ty;

                    if (rot == 1) { rx = 15 - ty; ry = tx; }
                    else if (rot == 2) { rx = 15 - tx; ry = 15 - ty; }
                    else if (rot == 3) { rx = ty; ry = 15 - tx; }

                    int bi = ry * 16 + rx;

                    diff += Math.Abs(a[BlockOffset + ai] - b[BlockOffset + bi]);
                }
            }

            return Clamp(100.0 - (diff / 256.0) * 100.0 / 255.0);
        }

        private static double FeatureScore(byte[] a, byte[] b)
        {
            double diff = 0;

            for (int i = 0; i < 8; i++)
                diff += Math.Abs(a[FeatureOffset + i] - b[FeatureOffset + i]);

            return Clamp(100.0 - (diff / 8.0) * 100.0 / 255.0);
        }

        private static int TransformIndex(int x, int y, int rot, int mirror)
        {
            if (mirror == 1)
                x = S - 1 - x;

            int rx = x;
            int ry = y;

            if (rot == 1)
            {
                rx = S - 1 - y;
                ry = x;
            }
            else if (rot == 2)
            {
                rx = S - 1 - x;
                ry = S - 1 - y;
            }
            else if (rot == 3)
            {
                rx = y;
                ry = S - 1 - x;
            }

            return ry * S + rx;
        }

        private static Bitmap CropJewellery(Bitmap src)
        {
            using (Bitmap small = ResizeMax(src, 700))
            using (Bitmap gray = ToGray(small))
            {
                Rectangle box = FindSmartBox(small, gray);

                if (box.Width < 20 || box.Height < 20)
                    return new Bitmap(small);

                Bitmap crop = small.Clone(box, PixelFormat.Format24bppRgb);
                return crop;
            }
        }

        private static Rectangle FindSmartBox(Bitmap color, Bitmap gray)
        {
            int w = color.Width;
            int h = color.Height;

            bool[,] mask = new bool[w, h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    Color c = color.GetPixel(x, y);

                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;
                    int bright = (c.R + c.G + c.B) / 3;

                    int gx =
                        -G(gray, x - 1, y - 1) + G(gray, x + 1, y - 1) +
                        -2 * G(gray, x - 1, y) + 2 * G(gray, x + 1, y) +
                        -G(gray, x - 1, y + 1) + G(gray, x + 1, y + 1);

                    int gy =
                        -G(gray, x - 1, y - 1) - 2 * G(gray, x, y - 1) - G(gray, x + 1, y - 1) +
                         G(gray, x - 1, y + 1) + 2 * G(gray, x, y + 1) + G(gray, x + 1, y + 1);

                    int mag = (int)Math.Sqrt(gx * gx + gy * gy);

                    bool gold = sat > 35 && bright > 45 && bright < 245;
                    bool dark = bright < 165 && mag > 20;
                    bool strongEdge = mag > 55 && bright < 235;

                    mask[x, y] = gold || dark || strongEdge;
                }
            }

            Rectangle best = LargestUsefulComponent(mask, w, h);

            if (best.Width <= 0 || best.Height <= 0)
                return new Rectangle(0, 0, w, h);

            int pad = Math.Max(8, Math.Max(best.Width, best.Height) / 8);

            int x1 = Math.Max(0, best.Left - pad);
            int y1 = Math.Max(0, best.Top - pad);
            int x2 = Math.Min(w - 1, best.Right + pad);
            int y2 = Math.Min(h - 1, best.Bottom + pad);

            return Rectangle.FromLTRB(x1, y1, x2, y2);
        }

        private static Rectangle LargestUsefulComponent(bool[,] mask, int w, int h)
        {
            bool[,] seen = new bool[w, h];
            Rectangle best = new Rectangle(0, 0, 0, 0);
            double bestScore = 0;

            int[] qx = new int[w * h];
            int[] qy = new int[w * h];

            for (int yy = 0; yy < h; yy += 2)
            {
                for (int xx = 0; xx < w; xx += 2)
                {
                    if (!mask[xx, yy] || seen[xx, yy])
                        continue;

                    int head = 0, tail = 0;
                    qx[tail] = xx;
                    qy[tail] = yy;
                    tail++;
                    seen[xx, yy] = true;

                    int minX = xx, maxX = xx, minY = yy, maxY = yy, count = 0;

                    while (head < tail)
                    {
                        int x = qx[head];
                        int y = qy[head];
                        head++;
                        count++;

                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;

                        Add(mask, seen, qx, qy, ref tail, w, h, x + 1, y);
                        Add(mask, seen, qx, qy, ref tail, w, h, x - 1, y);
                        Add(mask, seen, qx, qy, ref tail, w, h, x, y + 1);
                        Add(mask, seen, qx, qy, ref tail, w, h, x, y - 1);
                    }

                    int bw = maxX - minX + 1;
                    int bh = maxY - minY + 1;

                    if (bw < 8 || bh < 8 || count < 30)
                        continue;

                    double ratio = bw / (double)Math.Max(1, bh);
                    if (ratio > 5.5 || ratio < 0.18)
                        continue;

                    double area = bw * bh;
                    double centerX = (minX + maxX) / 2.0;
                    double centerY = (minY + maxY) / 2.0;
                    double dx = Math.Abs(centerX - w / 2.0) / w;
                    double dy = Math.Abs(centerY - h / 2.0) / h;
                    double centerPenalty = 1.0 - Math.Min(0.8, dx + dy);

                    double density = count / Math.Max(1.0, area);
                    double score = count * centerPenalty * (0.55 + density);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = Rectangle.FromLTRB(minX, minY, maxX, maxY);
                    }
                }
            }

            return best;
        }

        private static void Add(bool[,] mask, bool[,] seen, int[] qx, int[] qy, ref int tail, int w, int h, int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return;

            if (seen[x, y] || !mask[x, y])
                return;

            seen[x, y] = true;
            qx[tail] = x;
            qy[tail] = y;
            tail++;
        }

        private static Bitmap ResizeMax(Bitmap src, int max)
        {
            double scale = Math.Min(max / (double)src.Width, max / (double)src.Height);
            if (scale >= 1.0)
                return new Bitmap(src);

            int w = Math.Max(1, (int)(src.Width * scale));
            int h = Math.Max(1, (int)(src.Height * scale));

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return dst;
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

        private static int G(Bitmap bmp, int x, int y)
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
