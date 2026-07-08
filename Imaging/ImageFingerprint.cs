using System;
using System.Drawing;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageFingerprint
    {
        public ulong Hash { get; set; }
        public double DarkRatio { get; set; }
        public double EdgeRatio { get; set; }

        public static ImageFingerprint FromBitmap(Bitmap bmp)
        {
            Bitmap small = new Bitmap(32, 32);

            using (Graphics g = Graphics.FromImage(small))
                g.DrawImage(bmp, 0, 0, 32, 32);

            double[] gray = new double[1024];
            double total = 0;
            int dark = 0;
            int edge = 0;

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Color c = small.GetPixel(x, y);
                    double v = (c.R + c.G + c.B) / 3.0;

                    gray[y * 32 + x] = v;
                    total += v;

                    if (v < 180)
                        dark++;

                    if (x > 0 && y > 0)
                    {
                        double dx = Math.Abs(v - gray[y * 32 + (x - 1)]);
                        double dy = Math.Abs(v - gray[(y - 1) * 32 + x]);

                        if (dx + dy > 45)
                            edge++;
                    }
                }
            }

            double avg = total / 1024.0;

            ulong hash = 0;

            for (int i = 0; i < 64; i++)
            {
                int idx = i * 16;

                if (gray[idx] < avg)
                    hash |= (1UL << i);
            }

            return new ImageFingerprint
            {
                Hash = hash,
                DarkRatio = dark / 1024.0,
                EdgeRatio = edge / 1024.0
            };
        }

        public static double Compare(ImageFingerprint a, ImageFingerprint b)
        {
            int dist = Hamming(a.Hash ^ b.Hash);

            double hashScore = 1.0 - dist / 64.0;

            double darkPenalty = Math.Abs(a.DarkRatio - b.DarkRatio);
            double edgePenalty = Math.Abs(a.EdgeRatio - b.EdgeRatio);

            double score = hashScore
                         - darkPenalty * 0.55
                         - edgePenalty * 0.75;

            if (score < 0) score = 0;
            if (score > 1) score = 1;

            return score * 100.0;
        }

        private static int Hamming(ulong x)
        {
            int c = 0;

            while (x != 0)
            {
                x &= x - 1;
                c++;
            }

            return c;
        }
    }
}
