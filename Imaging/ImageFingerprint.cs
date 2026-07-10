using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageFingerprint
    {
        public ulong Hash { get; set; }
        public double DarkRatio { get; set; }
        public double EdgeRatio { get; set; }

        private const int NormalSize = 128;
        private const int GridSize = 8;

        public static ImageFingerprint FromBitmap(Bitmap source)
        {
            if (source == null)
            {
                return new ImageFingerprint
                {
                    Hash = 0,
                    DarkRatio = 0,
                    EdgeRatio = 0
                };
            }

            using (Bitmap normalized = Normalize(source))
            {
                bool[,] darkMask =
                    BuildDarkMask(normalized);

                double darkRatio =
                    CalculateDarkRatio(
                        darkMask,
                        normalized.Width,
                        normalized.Height
                    );

                double edgeRatio =
                    CalculateEdgeRatio(
                        darkMask,
                        normalized.Width,
                        normalized.Height
                    );

                ulong hash =
                    BuildGridHash(
                        darkMask,
                        normalized.Width,
                        normalized.Height
                    );

                return new ImageFingerprint
                {
                    Hash = hash,
                    DarkRatio = darkRatio,
                    EdgeRatio = edgeRatio
                };
            }
        }

        public static double Compare(
            ImageFingerprint first,
            ImageFingerprint second)
        {
            if (first == null || second == null)
                return 0;

            int hashDistance =
                Hamming(first.Hash ^ second.Hash);

            double hashScore =
                1.0 - hashDistance / 64.0;

            double darkDifference =
                Math.Abs(
                    first.DarkRatio -
                    second.DarkRatio
                );

            double edgeDifference =
                Math.Abs(
                    first.EdgeRatio -
                    second.EdgeRatio
                );

            double score =
                hashScore * 0.72 +
                SimilarityFromDifference(
                    darkDifference,
                    0.45
                ) * 0.13 +
                SimilarityFromDifference(
                    edgeDifference,
                    0.35
                ) * 0.15;

            if (hashDistance > 34)
                score *= 0.72;

            if (darkDifference > 0.30)
                score *= 0.78;

            if (edgeDifference > 0.24)
                score *= 0.82;

            score = Clamp01(score);

            return Math.Round(
                score * 100.0,
                2
            );
        }

        private static Bitmap Normalize(Bitmap source)
        {
            Bitmap gray =
                new Bitmap(
                    NormalSize,
                    NormalSize,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics = Graphics.FromImage(gray))
            {
                graphics.Clear(Color.White);

                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;

                graphics.SmoothingMode =
                    SmoothingMode.HighQuality;

                graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                graphics.DrawImage(
                    source,
                    0,
                    0,
                    NormalSize,
                    NormalSize
                );
            }

            return gray;
        }

        private static bool[,] BuildDarkMask(
            Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            var grayValues =
                new int[width, height];

            long total = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color color =
                        bitmap.GetPixel(x, y);

                    int gray =
                        (
                            color.R * 30 +
                            color.G * 59 +
                            color.B * 11
                        ) / 100;

                    grayValues[x, y] = gray;
                    total += gray;
                }
            }

            double average =
                total /
                (double)Math.Max(
                    1,
                    width * height
                );

            int threshold =
                (int)Math.Min(
                    215,
                    Math.Max(
                        115,
                        average - 28
                    )
                );

            var mask =
                new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mask[x, y] =
                        grayValues[x, y] <
                        threshold;
                }
            }

            RemoveNoise(mask, width, height);

            return mask;
        }

        private static void RemoveNoise(
            bool[,] mask,
            int width,
            int height)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var next =
                    new bool[width, height];

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        int neighbours =
                            CountNeighbours(
                                mask,
                                x,
                                y
                            );

                        if (mask[x, y])
                        {
                            next[x, y] =
                                neighbours >= 2;
                        }
                        else
                        {
                            next[x, y] =
                                neighbours >= 6;
                        }
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                        mask[x, y] = next[x, y];
                }
            }
        }

        private static int CountNeighbours(
            bool[,] mask,
            int x,
            int y)
        {
            int count = 0;

            for (int yy = y - 1; yy <= y + 1; yy++)
            {
                for (int xx = x - 1; xx <= x + 1; xx++)
                {
                    if (xx == x && yy == y)
                        continue;

                    if (mask[xx, yy])
                        count++;
                }
            }

            return count;
        }

        private static ulong BuildGridHash(
            bool[,] mask,
            int width,
            int height)
        {
            double[] cellRatios =
                new double[GridSize * GridSize];

            int cellWidth =
                Math.Max(1, width / GridSize);

            int cellHeight =
                Math.Max(1, height / GridSize);

            double totalRatio = 0;

            for (int gridY = 0; gridY < GridSize; gridY++)
            {
                for (int gridX = 0; gridX < GridSize; gridX++)
                {
                    int startX =
                        gridX * cellWidth;

                    int startY =
                        gridY * cellHeight;

                    int endX =
                        gridX == GridSize - 1
                            ? width
                            : Math.Min(
                                width,
                                startX + cellWidth
                            );

                    int endY =
                        gridY == GridSize - 1
                            ? height
                            : Math.Min(
                                height,
                                startY + cellHeight
                            );

                    int darkPixels = 0;
                    int totalPixels = 0;

                    for (int y = startY; y < endY; y++)
                    {
                        for (int x = startX; x < endX; x++)
                        {
                            if (mask[x, y])
                                darkPixels++;

                            totalPixels++;
                        }
                    }

                    double ratio =
                        totalPixels > 0
                            ? darkPixels /
                              (double)totalPixels
                            : 0;

                    int index =
                        gridY * GridSize +
                        gridX;

                    cellRatios[index] = ratio;
                    totalRatio += ratio;
                }
            }

            double averageCellRatio =
                totalRatio / 64.0;

            ulong hash = 0;

            for (int i = 0; i < 64; i++)
            {
                double localThreshold =
                    Math.Max(
                        0.055,
                        averageCellRatio * 0.72
                    );

                if (cellRatios[i] >= localThreshold)
                    hash |= 1UL << i;
            }

            return hash;
        }

        private static double CalculateDarkRatio(
            bool[,] mask,
            int width,
            int height)
        {
            int dark = 0;
            int total = width * height;

            if (total <= 0)
                return 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[x, y])
                        dark++;
                }
            }

            return dark / (double)total;
        }

        private static double CalculateEdgeRatio(
            bool[,] mask,
            int width,
            int height)
        {
            int edges = 0;
            int possible = 0;

            for (int y = 1; y < height; y++)
            {
                for (int x = 1; x < width; x++)
                {
                    bool current =
                        mask[x, y];

                    if (current != mask[x - 1, y])
                        edges++;

                    if (current != mask[x, y - 1])
                        edges++;

                    possible += 2;
                }
            }

            if (possible <= 0)
                return 0;

            return edges / (double)possible;
        }

        private static double SimilarityFromDifference(
            double difference,
            double maximumDifference)
        {
            if (maximumDifference <= 0)
                return 0;

            double value =
                1.0 -
                difference / maximumDifference;

            return Clamp01(value);
        }

        private static double Clamp01(double value)
        {
            if (value < 0)
                return 0;

            if (value > 1)
                return 1;

            return value;
        }

        private static int Hamming(ulong value)
        {
            int count = 0;

            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }
    }
}
