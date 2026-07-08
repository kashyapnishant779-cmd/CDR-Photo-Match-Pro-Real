using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int PartSize = 32;
        private const int MaxParts = 6;
        private const int TotalBytes = MaxParts * PartSize;

        public static double Compare(string queryImagePath, string dbImagePath)
        {
            ImageMatcher m = new ImageMatcher();
            return m.Compare(
                m.ExtractDescriptorBytes(queryImagePath),
                m.ExtractDescriptorBytes(dbImagePath)
            );
        }

        public static double CompareImages(string queryImagePath, string dbImagePath)
        {
            return Compare(queryImagePath, dbImagePath);
        }

        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            byte[] data = new byte[TotalBytes];

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return data;

            try
            {
                using (Bitmap bmp = new Bitmap(imagePath))
                {
                    List<ImageSegment> parts = ImageSegmenter.Segment(bmp);

                    int count = Math.Min(MaxParts, parts.Count);

                    for (int i = 0; i < count; i++)
                    {
                        ImageFingerprint fp = ImageFingerprint.FromBitmap(parts[i].Bitmap);

                        WritePart(data, i, fp.Hash, fp.DarkRatio, fp.EdgeRatio, parts[i].Weight);
                    }
                }
            }
            catch
            {
            }

            return data;
        }

        public double Compare(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length < TotalBytes || b.Length < TotalBytes)
                return 0;

            double fullScore = ComparePart(a, b, 0, 0);

            double weighted = 0;
            double weightSum = 0;
            int goodParts = 0;

            for (int i = 0; i < MaxParts; i++)
            {
                double qWeight = ReadDouble(a, i, 24);
                if (qWeight <= 0) continue;

                double best = 0;

                for (int j = 0; j < MaxParts; j++)
                {
                    double cWeight = ReadDouble(b, j, 24);
                    if (cWeight <= 0) continue;

                    double s = ComparePart(a, b, i, j);

                    if (s > best)
                        best = s;
                }

                weighted += best * qWeight;
                weightSum += qWeight;

                if (best >= 72)
                    goodParts++;
            }

            double partScore = weightSum > 0 ? weighted / weightSum : 0;

            double final = fullScore * 0.60 + partScore * 0.40;

            if (goodParts <= 1 && final > 68)
                final = 58;

            if (fullScore < 45 && partScore > 78)
                final = 55;

            if (fullScore < 35)
                final = Math.Min(final, 50);

            if (final > 100) final = 100;
            if (final < 0) final = 0;

            return Math.Round(final, 2);
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
            return new Size(160, 160);
        }

        private static double ComparePart(byte[] a, byte[] b, int ai, int bi)
        {
            ulong ha = ReadUlong(a, ai, 0);
            ulong hb = ReadUlong(b, bi, 0);

            double da = ReadDouble(a, ai, 8);
            double db = ReadDouble(b, bi, 8);

            double ea = ReadDouble(a, ai, 16);
            double eb = ReadDouble(b, bi, 16);

            int dist = Hamming(ha ^ hb);

            double hashScore = 1.0 - dist / 64.0;
            double darkPenalty = Math.Abs(da - db);
            double edgePenalty = Math.Abs(ea - eb);

            double score = hashScore
                         - darkPenalty * 0.55
                         - edgePenalty * 0.75;

            if (score < 0) score = 0;
            if (score > 1) score = 1;

            return score * 100.0;
        }

        private static void WritePart(byte[] data, int part, ulong hash, double dark, double edge, double weight)
        {
            int o = part * PartSize;

            Array.Copy(BitConverter.GetBytes(hash), 0, data, o + 0, 8);
            Array.Copy(BitConverter.GetBytes(dark), 0, data, o + 8, 8);
            Array.Copy(BitConverter.GetBytes(edge), 0, data, o + 16, 8);
            Array.Copy(BitConverter.GetBytes(weight), 0, data, o + 24, 8);
        }

        private static ulong ReadUlong(byte[] data, int part, int offset)
        {
            return BitConverter.ToUInt64(data, part * PartSize + offset);
        }

        private static double ReadDouble(byte[] data, int part, int offset)
        {
            return BitConverter.ToDouble(data, part * PartSize + offset);
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
