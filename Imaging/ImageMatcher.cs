using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int N = 128;
        private const int HIST = 32;
        private const int LEN = 4 + (N * N) + (HIST * 2) + 8;

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image not found", imagePath);

            using (Bitmap original = new Bitmap(imagePath))
            using (Bitmap crop = CropMainObject(original))
            using (Bitmap norm = Normalize(crop, N))
            {
                byte[] mask = MakeMask(norm);
                byte[] edge = MakeEdge(mask);

                byte[] desc = new byte[LEN];
                desc[0] = (byte)'I';
                desc[1] = (byte)'M';
                desc[2] = (byte)'G';
                desc[3] = 5;

                Buffer.BlockCopy(edge, 0, desc, 4, N * N);

                byte[] hx = HistX(edge);
                byte[] hy = HistY(edge);

                Buffer.BlockCopy(hx, 0, desc, 4 + N * N, HIST);
                Buffer.BlockCopy(hy, 0, desc, 4 + N * N + HIST, HIST);

                int meta = 4 + N * N + HIST * 2;

                desc[meta + 0] = (byte)Math.Min(255, Count(edge) * 255 / (N * N));
                desc[meta + 1] = (byte)Math.Min(255, Count(mask) * 255 / (N * N));
                desc[meta + 2] = (byte)Math.Min(255, Aspect(mask) * 40);
                desc[meta + 3] = (byte)Math.Min(255, CenterMassX(mask) * 255 / N);
                desc[meta + 4] = (byte)Math.Min(255, CenterMassY(mask) * 255 / N);

                return desc;
            }
        }

        public double Compare(byte[] q, byte[] d)
        {
            if (q == null || d == null) return 0;
            if (q.Length != LEN || d.Length != LEN) return 0;
            if (q[3] != d[3]) return 0;

            byte[] qe = Slice(q, 4, N * N);
            byte[] de = Slice(d, 4, N * N);

            byte[] qx = Slice(q, 4 + N * N, HIST);
            byte[] dx = Slice(d, 4 + N * N, HIST);
            byte[] qy = Slice(q, 4 + N * N + HIST, HIST);
            byte[] dy = Slice(d, 4 + N * N + HIST, HIST);

            int meta = 4 + N * N + HIST * 2;

            double best = 0;
            best = Math.Max(best, CompareOne(qe, de, qx, dx, qy, dy, q, d, meta));
            best = Math.Max(best, CompareOne(Rotate90(qe), de, qy, dx, qx, dy, q, d, meta));
            best = Math.Max(best, CompareOne(Rotate180(qe), de, Reverse(qx), dx, Reverse(qy), dy, q, d, meta));
            best = Math.Max(best, CompareOne(Rotate270(qe), de, qy, dx, qx, dy, q, d, meta));

            if (best < 0) best = 0;
            if (best > 100) best = 100;
            return best;
        }

        public Size ReadSize(string imagePath)
        {
            using (var img = Image.FromFile(imagePath))
                return img.Size;
        }

        private double CompareOne(byte[] qe, byte[] de, byte[] qx, byte[] dx, byte[] qy, byte[] dy, byte[] q, byte[] d, int meta)
        {
            double edge = ShiftBest(qe, de);
            double hx = HistScore(qx, dx);
            double hy = HistScore(qy, dy);

            double density = 100 - Math.Abs(q[meta] - d[meta]) * 100.0 / 255.0;
            double fill = 100 - Math.Abs(q[meta + 1] - d[meta + 1]) * 100.0 / 255.0;
            double aspect = 100 - Math.Abs(q[meta + 2] - d[meta + 2]) * 100.0 / 255.0;

            if (density < 0) density = 0;
            if (fill < 0) fill = 0;
            if (aspect < 0) aspect = 0;

            return edge * 0.45 + hx * 0.18 + hy * 0.18 + density * 0.08 + fill * 0.05 + aspect * 0.06;
        }

        private double ShiftBest(byte[] a, byte[] b)
        {
            double best = 0;
            int[] shifts = { -10, -6, -3, 0, 3, 6, 10 };

            foreach (int dy in shifts)
                foreach (int dx in shifts)
                    best = Math.Max(best, Jaccard(a, b, dx, dy));

            return best;
        }

        private double Jaccard(byte[] a, byte[] b, int dx, int dy)
        {
            int inter = 0, union = 0;

            for (int y = 0; y < N; y++)
            {
                int yy = y + dy;
                if (yy < 0 || yy >= N) continue;

                for (int x = 0; x < N; x++)
                {
                    int xx = x + dx;
                    if (xx < 0 || xx >= N) continue;

                    bool aa = a[y * N + x] > 0;
                    bool bb = b[yy * N + xx] > 0;

                    if (aa && bb) inter++;
                    if (aa || bb) union++;
                }
            }

            if (union < 20) return 0;
            return inter * 100.0 / union * 2.2;
        }

        private Bitmap CropMainObject(Bitmap src)
        {
            int minX = src.Width, minY = src.Height, maxX = 0, maxY = 0;

            for (int y = 0; y < src.Height; y += 3)
            {
                for (int x = 0; x < src.Width; x += 3)
                {
                    Color c = src.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;

                    bool blackVector = gray < 180;
                    bool gold = c.R > 120 && c.G > 85 && c.B < 100;
                    bool silver = gray > 125 && sat < 80;
                    bool whiteStone = gray > 200 && sat < 70;

                    if (blackVector || gold || silver || whiteStone)
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

            int pad = 25;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(src.Width - 1, maxX + pad);
            maxY = Math.Min(src.Height - 1, maxY + pad);

            return src.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), src.PixelFormat);
        }

        private Bitmap Normalize(Bitmap src, int size)
        {
            Bitmap dst = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                double scale = Math.Min((size - 12) / (double)src.Width, (size - 12) / (double)src.Height);
                int w = Math.Max(1, (int)(src.Width * scale));
                int h = Math.Max(1, (int)(src.Height * scale));
                g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
            }
            return dst;
        }

        private byte[] MakeMask(Bitmap bmp)
        {
            byte[] m = new byte[N * N];

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;

                    bool vector = gray < 210;
                    bool gold = c.R > 115 && c.G > 75 && c.B < 120;
                    bool silver = gray > 120 && sat < 90;
                    bool stone = gray > 200 && sat < 90;

                    if (vector || gold || silver || stone)
                        m[y * N + x] = 255;
                }
            }

            return m;
        }

        private byte[] MakeEdge(byte[] m)
        {
            byte[] e = new byte[N * N];

            for (int y = 1; y < N - 1; y++)
                for (int x = 1; x < N - 1; x++)
                {
                    int i = y * N + x;
                    if (m[i] != m[i - 1] || m[i] != m[i + 1] || m[i] != m[i - N] || m[i] != m[i + N])
                        e[i] = 255;
                }

            return e;
        }

        private byte[] HistX(byte[] e)
        {
            byte[] h = new byte[HIST];
            for (int x = 0; x < N; x++)
            {
                int c = 0;
                for (int y = 0; y < N; y++)
                    if (e[y * N + x] > 0) c++;
                h[x * HIST / N] = (byte)Math.Min(255, h[x * HIST / N] + c);
            }
            return h;
        }

        private byte[] HistY(byte[] e)
        {
            byte[] h = new byte[HIST];
            for (int y = 0; y < N; y++)
            {
                int c = 0;
                for (int x = 0; x < N; x++)
                    if (e[y * N + x] > 0) c++;
                h[y * HIST / N] = (byte)Math.Min(255, h[y * HIST / N] + c);
            }
            return h;
        }

        private double HistScore(byte[] a, byte[] b)
        {
            int diff = 0, total = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff += Math.Abs(a[i] - b[i]);
                total += Math.Max(a[i], b[i]);
            }

            if (total == 0) return 0;
            return Math.Max(0, 100 - diff * 100.0 / total);
        }

        private int Count(byte[] a)
        {
            int c = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i] > 0) c++;
            return c;
        }

        private int Aspect(byte[] m)
        {
            int minX = N, minY = N, maxX = 0, maxY = 0;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    if (m[y * N + x] > 0)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }

            if (maxX <= minX || maxY <= minY) return 1;
            return Math.Max(1, (maxX - minX + 1) * 10 / Math.Max(1, maxY - minY + 1));
        }

        private int CenterMassX(byte[] m)
        {
            int sum = 0, c = 0;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    if (m[y * N + x] > 0) { sum += x; c++; }
            return c == 0 ? N / 2 : sum / c;
        }

        private int CenterMassY(byte[] m)
        {
            int sum = 0, c = 0;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    if (m[y * N + x] > 0) { sum += y; c++; }
            return c == 0 ? N / 2 : sum / c;
        }

        private byte[] Slice(byte[] src, int start, int len)
        {
            byte[] r = new byte[len];
            Buffer.BlockCopy(src, start, r, 0, len);
            return r;
        }

        private byte[] Reverse(byte[] a)
        {
            byte[] r = new byte[a.Length];
            for (int i = 0; i < a.Length; i++)
                r[i] = a[a.Length - 1 - i];
            return r;
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
