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

        public ulong EdgeHash { get; set; }
        public ulong HorizontalHash { get; set; }
        public ulong VerticalHash { get; set; }
        public ulong RadialHash { get; set; }

        public double AspectRatio { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double BorderRatio { get; set; }
        public double Symmetry { get; set; }

        private const int WorkingSize = 128;
        private const int GridSize = 8;

        public static ImageFingerprint FromBitmap(Bitmap source)
        {
            if (source == null ||
                source.Width <= 0 ||
                source.Height <= 0)
            {
                return Empty();
            }

            try
            {
                using (Bitmap normalized = Normalize(source))
                {
                    bool[,] mask = BuildMask(normalized);

                    Rectangle bounds = FindBounds(
                        mask,
                        WorkingSize,
                        WorkingSize
                    );

                    if (bounds.Width < 3 ||
                        bounds.Height < 3)
                    {
                        return Empty();
                    }

                    bool[,] centered = CenterMask(
                        mask,
                        bounds
                    );

                    Rectangle centeredBounds = FindBounds(
                        centered,
                        WorkingSize,
                        WorkingSize
                    );

                    bool[,] edges = BuildEdges(centered);

                    double darkRatio = Ratio(centered);
                    double edgeRatio = Ratio(edges);
                    PointF centroid = Centroid(centered);

                    return new ImageFingerprint
                    {
                        Hash = GridHash(centered),
                        EdgeHash = GridHash(edges),
                        HorizontalHash = HorizontalHash(centered),
                        VerticalHash = VerticalHash(centered),
                        RadialHash = RadialHash(centered, centroid),
                        DarkRatio = darkRatio,
                        EdgeRatio = edgeRatio,
                        AspectRatio =
                            centeredBounds.Width /
                            (double)Math.Max(
                                1,
                                centeredBounds.Height
                            ),
                        CenterX =
                            centroid.X /
                            Math.Max(
                                1.0,
                                WorkingSize - 1.0
                            ),
                        CenterY =
                            centroid.Y /
                            Math.Max(
                                1.0,
                                WorkingSize - 1.0
                            ),
                        BorderRatio =
                            CalculateBorderRatio(
                                centered,
                                centeredBounds
                            ),
                        Symmetry =
                            CalculateSymmetry(
                                centered,
                                centeredBounds
                            )
                    };
                }
            }
            catch
            {
                return Empty();
            }
        }

        public static double Compare(
            ImageFingerprint first,
            ImageFingerprint second)
        {
            if (first == null || second == null)
                return 0;

            if (first.AspectRatio <= 0 ||
                second.AspectRatio <= 0)
            {
                return 0;
            }

            double occupancy =
                HashSimilarity(
                    first.Hash,
                    second.Hash
                );

            double edges =
                HashSimilarity(
                    first.EdgeHash,
                    second.EdgeHash
                );

            double horizontal =
                HashSimilarity(
                    first.HorizontalHash,
                    second.HorizontalHash
                );

            double vertical =
                HashSimilarity(
                    first.VerticalHash,
                    second.VerticalHash
                );

            double radial =
                HashSimilarity(
                    first.RadialHash,
                    second.RadialHash
                );

            double aspect =
                RatioSimilarity(
                    first.AspectRatio,
                    second.AspectRatio
                );

            double dark =
                DifferenceSimilarity(
                    first.DarkRatio,
                    second.DarkRatio,
                    0.34
                );

            double edgeRatio =
                DifferenceSimilarity(
                    first.EdgeRatio,
                    second.EdgeRatio,
                    0.28
                );

            double border =
                DifferenceSimilarity(
                    first.BorderRatio,
                    second.BorderRatio,
                    0.30
                );

            double symmetry =
                DifferenceSimilarity(
                    first.Symmetry,
                    second.Symmetry,
                    0.42
                );

            double score =
                edges * 0.24 +
                radial * 0.20 +
                occupancy * 0.18 +
                horizontal * 0.11 +
                vertical * 0.11 +
                aspect * 0.08 +
                dark * 0.03 +
                edgeRatio * 0.025 +
                border * 0.015 +
                symmetry * 0.01;

            double structure =
                (
                    edges +
                    radial +
                    occupancy +
                    horizontal +
                    vertical
                ) / 5.0;

            // Only a light rejection penalty.
            // Correct photo-vs-vector matches ko 5–15% tak crush nahi karta.
            if (structure < 0.35)
                score *= 0.72;
            else if (structure < 0.48)
                score *= 0.86;

            if (aspect < 0.45)
                score *= 0.82;
            else if (aspect < 0.62)
                score *= 0.92;

            // Strong agreement bonus.
            if (edges >= 0.78 &&
                radial >= 0.74 &&
                occupancy >= 0.72)
            {
                score += 0.06;
            }
            else if (structure >= 0.70)
            {
                score += 0.03;
            }

            score = Clamp01(score);

            return Math.Round(
                score * 100.0,
                2
            );
        }

        private static Bitmap Normalize(Bitmap source)
        {
            Bitmap result = new Bitmap(
                WorkingSize,
                WorkingSize,
                PixelFormat.Format24bppRgb
            );

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode =
                    SmoothingMode.HighQuality;
                graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                double scale = Math.Min(
                    (WorkingSize - 10.0) /
                    Math.Max(1, source.Width),
                    (WorkingSize - 10.0) /
                    Math.Max(1, source.Height)
                );

                int width = Math.Max(
                    1,
                    (int)Math.Round(source.Width * scale)
                );

                int height = Math.Max(
                    1,
                    (int)Math.Round(source.Height * scale)
                );

                int left = (WorkingSize - width) / 2;
                int top = (WorkingSize - height) / 2;

                graphics.DrawImage(
                    source,
                    new Rectangle(left, top, width, height),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel
                );
            }

            return result;
        }

        private static bool[,] BuildMask(Bitmap bitmap)
        {
            int threshold = CalculateOtsu(bitmap);
            int darkCount = 0;
            int lightCount = 0;

            bool[,] dark =
                new bool[WorkingSize, WorkingSize];

            bool[,] light =
                new bool[WorkingSize, WorkingSize];

            for (int y = 0; y < WorkingSize; y++)
            {
                for (int x = 0; x < WorkingSize; x++)
                {
                    int gray =
                        Gray(bitmap.GetPixel(x, y));

                    dark[x, y] =
                        gray <= threshold;

                    light[x, y] =
                        gray >= 255 - threshold;

                    if (dark[x, y])
                        darkCount++;

                    if (light[x, y])
                        lightCount++;
                }
            }

            if (darkCount >
                WorkingSize * WorkingSize * 0.72 &&
                lightCount > 20)
            {
                return light;
            }

            return dark;
        }

        private static int CalculateOtsu(Bitmap bitmap)
        {
            int[] histogram = new int[256];
            long totalIntensity = 0;
            int total = bitmap.Width * bitmap.Height;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int gray =
                        Gray(bitmap.GetPixel(x, y));

                    histogram[gray]++;
                    totalIntensity += gray;
                }
            }

            long backgroundIntensity = 0;
            int backgroundCount = 0;
            double bestVariance = -1;
            int bestThreshold = 180;

            for (int threshold = 0;
                 threshold < 256;
                 threshold++)
            {
                backgroundCount += histogram[threshold];

                if (backgroundCount == 0)
                    continue;

                int foregroundCount =
                    total - backgroundCount;

                if (foregroundCount == 0)
                    break;

                backgroundIntensity +=
                    (long)threshold *
                    histogram[threshold];

                double backgroundMean =
                    backgroundIntensity /
                    (double)backgroundCount;

                double foregroundMean =
                    (
                        totalIntensity -
                        backgroundIntensity
                    ) /
                    (double)foregroundCount;

                double difference =
                    backgroundMean -
                    foregroundMean;

                double variance =
                    backgroundCount *
                    (double)foregroundCount *
                    difference *
                    difference;

                if (variance > bestVariance)
                {
                    bestVariance = variance;
                    bestThreshold = threshold;
                }
            }

            return ClampInt(
                bestThreshold,
                70,
                225
            );
        }

        private static Rectangle FindBounds(
            bool[,] mask,
            int width,
            int height)
        {
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y])
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX ||
                maxY < minY)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(
                minX,
                minY,
                maxX + 1,
                maxY + 1
            );
        }

        private static bool[,] CenterMask(
            bool[,] source,
            Rectangle bounds)
        {
            bool[,] result =
                new bool[WorkingSize, WorkingSize];

            double scale = Math.Min(
                (WorkingSize - 12.0) /
                Math.Max(1, bounds.Width),
                (WorkingSize - 12.0) /
                Math.Max(1, bounds.Height)
            );

            int width = Math.Max(
                1,
                (int)Math.Round(bounds.Width * scale)
            );

            int height = Math.Max(
                1,
                (int)Math.Round(bounds.Height * scale)
            );

            int left = (WorkingSize - width) / 2;
            int top = (WorkingSize - height) / 2;

            for (int y = 0; y < height; y++)
            {
                int sourceY = ClampInt(
                    bounds.Top +
                    (int)(y / Math.Max(0.0001, scale)),
                    bounds.Top,
                    bounds.Bottom - 1
                );

                for (int x = 0; x < width; x++)
                {
                    int sourceX = ClampInt(
                        bounds.Left +
                        (int)(x / Math.Max(0.0001, scale)),
                        bounds.Left,
                        bounds.Right - 1
                    );

                    result[left + x, top + y] =
                        source[sourceX, sourceY];
                }
            }

            return result;
        }

        private static bool[,] BuildEdges(
            bool[,] mask)
        {
            bool[,] result =
                new bool[WorkingSize, WorkingSize];

            for (int y = 1;
                 y < WorkingSize - 1;
                 y++)
            {
                for (int x = 1;
                     x < WorkingSize - 1;
                     x++)
                {
                    if (!mask[x, y])
                        continue;

                    result[x, y] =
                        !mask[x - 1, y] ||
                        !mask[x + 1, y] ||
                        !mask[x, y - 1] ||
                        !mask[x, y + 1] ||
                        !mask[x - 1, y - 1] ||
                        !mask[x + 1, y - 1] ||
                        !mask[x - 1, y + 1] ||
                        !mask[x + 1, y + 1];
                }
            }

            return result;
        }

        private static ulong GridHash(bool[,] mask)
        {
            double[] values =
                new double[GridSize * GridSize];

            double sum = 0;

            for (int gridY = 0;
                 gridY < GridSize;
                 gridY++)
            {
                int startY =
                    gridY * WorkingSize / GridSize;

                int endY =
                    (gridY + 1) *
                    WorkingSize / GridSize;

                for (int gridX = 0;
                     gridX < GridSize;
                     gridX++)
                {
                    int startX =
                        gridX * WorkingSize / GridSize;

                    int endX =
                        (gridX + 1) *
                        WorkingSize / GridSize;

                    int count = 0;
                    int total = 0;

                    for (int y = startY; y < endY; y++)
                    {
                        for (int x = startX; x < endX; x++)
                        {
                            if (mask[x, y])
                                count++;

                            total++;
                        }
                    }

                    double value =
                        count /
                        (double)Math.Max(1, total);

                    int index =
                        gridY * GridSize + gridX;

                    values[index] = value;
                    sum += value;
                }
            }

            double average =
                sum / values.Length;

            ulong hash = 0;

            for (int i = 0; i < values.Length; i++)
            {
                double threshold =
                    Math.Max(0.025, average * 0.74);

                if (values[i] >= threshold)
                    hash |= 1UL << i;
            }

            return hash;
        }

        private static ulong HorizontalHash(
            bool[,] mask)
        {
            double[] bins = new double[32];

            for (int bin = 0; bin < bins.Length; bin++)
            {
                int startY =
                    bin * WorkingSize / bins.Length;

                int endY =
                    (bin + 1) *
                    WorkingSize / bins.Length;

                int count = 0;
                int total = 0;

                for (int y = startY; y < endY; y++)
                {
                    for (int x = 0; x < WorkingSize; x++)
                    {
                        if (mask[x, y])
                            count++;

                        total++;
                    }
                }

                bins[bin] =
                    count /
                    (double)Math.Max(1, total);
            }

            return ProjectionHash(bins);
        }

        private static ulong VerticalHash(
            bool[,] mask)
        {
            double[] bins = new double[32];

            for (int bin = 0; bin < bins.Length; bin++)
            {
                int startX =
                    bin * WorkingSize / bins.Length;

                int endX =
                    (bin + 1) *
                    WorkingSize / bins.Length;

                int count = 0;
                int total = 0;

                for (int x = startX; x < endX; x++)
                {
                    for (int y = 0; y < WorkingSize; y++)
                    {
                        if (mask[x, y])
                            count++;

                        total++;
                    }
                }

                bins[bin] =
                    count /
                    (double)Math.Max(1, total);
            }

            return ProjectionHash(bins);
        }

        private static ulong ProjectionHash(
            double[] bins)
        {
            double average = 0;

            for (int i = 0; i < bins.Length; i++)
                average += bins[i];

            average /=
                Math.Max(1, bins.Length);

            ulong hash = 0;

            for (int i = 0;
                 i < bins.Length &&
                 i < 32;
                 i++)
            {
                if (bins[i] >= average)
                    hash |= 1UL << i;
            }

            for (int i = 0;
                 i < bins.Length - 1 &&
                 i < 31;
                 i++)
            {
                if (bins[i + 1] >= bins[i])
                    hash |= 1UL << (32 + i);
            }

            if (average >= 0.28)
                hash |= 1UL << 63;

            return hash;
        }

        private static ulong RadialHash(
            bool[,] mask,
            PointF center)
        {
            int bins = 64;
            int[] count = new int[bins];
            int[] total = new int[bins];

            double maximum =
                Math.Sqrt(
                    WorkingSize * WorkingSize +
                    WorkingSize * WorkingSize
                ) / 2.0;

            for (int y = 0; y < WorkingSize; y++)
            {
                for (int x = 0; x < WorkingSize; x++)
                {
                    double dx = x - center.X;
                    double dy = y - center.Y;
                    double radius =
                        Math.Sqrt(dx * dx + dy * dy);

                    int bin = ClampInt(
                        (int)(radius /
                        Math.Max(0.0001, maximum) *
                        bins),
                        0,
                        bins - 1
                    );

                    total[bin]++;

                    if (mask[x, y])
                        count[bin]++;
                }
            }

            double[] ratios = new double[bins];
            double average = 0;

            for (int i = 0; i < bins; i++)
            {
                ratios[i] =
                    count[i] /
                    (double)Math.Max(1, total[i]);

                average += ratios[i];
            }

            average /= bins;

            ulong hash = 0;

            for (int i = 0; i < bins; i++)
            {
                if (ratios[i] >= average)
                    hash |= 1UL << i;
            }

            return hash;
        }

        private static PointF Centroid(
            bool[,] mask)
        {
            double xTotal = 0;
            double yTotal = 0;
            int count = 0;

            for (int y = 0; y < WorkingSize; y++)
            {
                for (int x = 0; x < WorkingSize; x++)
                {
                    if (!mask[x, y])
                        continue;

                    xTotal += x;
                    yTotal += y;
                    count++;
                }
            }

            if (count <= 0)
            {
                return new PointF(
                    WorkingSize / 2f,
                    WorkingSize / 2f
                );
            }

            return new PointF(
                (float)(xTotal / count),
                (float)(yTotal / count)
            );
        }

        private static double Ratio(bool[,] mask)
        {
            int count = 0;

            for (int y = 0; y < WorkingSize; y++)
            {
                for (int x = 0; x < WorkingSize; x++)
                {
                    if (mask[x, y])
                        count++;
                }
            }

            return count /
                (double)(WorkingSize * WorkingSize);
        }

        private static double CalculateBorderRatio(
            bool[,] mask,
            Rectangle bounds)
        {
            int foreground = 0;
            int border = 0;

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int x = bounds.Left;
                     x < bounds.Right;
                     x++)
                {
                    if (!mask[x, y])
                        continue;

                    foreground++;

                    if (x == bounds.Left ||
                        x == bounds.Right - 1 ||
                        y == bounds.Top ||
                        y == bounds.Bottom - 1)
                    {
                        border++;
                    }
                }
            }

            return border /
                (double)Math.Max(1, foreground);
        }

        private static double CalculateSymmetry(
            bool[,] mask,
            Rectangle bounds)
        {
            int same = 0;
            int total = 0;

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int x = 0;
                     x < bounds.Width / 2;
                     x++)
                {
                    int left = bounds.Left + x;
                    int right = bounds.Right - 1 - x;

                    if (mask[left, y] ==
                        mask[right, y])
                    {
                        same++;
                    }

                    total++;
                }
            }

            return same /
                (double)Math.Max(1, total);
        }

        private static double HashSimilarity(
            ulong first,
            ulong second)
        {
            int distance = Hamming(first ^ second);

            return 1.0 -
                distance / 64.0;
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

        private static double RatioSimilarity(
            double first,
            double second)
        {
            double maximum =
                Math.Max(
                    Math.Abs(first),
                    Math.Abs(second)
                );

            if (maximum <= 0.000001)
                return 1;

            return Clamp01(
                1.0 -
                Math.Abs(first - second) /
                maximum
            );
        }

        private static double DifferenceSimilarity(
            double first,
            double second,
            double fullDifference)
        {
            return Clamp01(
                1.0 -
                Math.Abs(first - second) /
                Math.Max(0.0001, fullDifference)
            );
        }

        private static ImageFingerprint Empty()
        {
            return new ImageFingerprint
            {
                Hash = 0,
                DarkRatio = 0,
                EdgeRatio = 0,
                EdgeHash = 0,
                HorizontalHash = 0,
                VerticalHash = 0,
                RadialHash = 0,
                AspectRatio = 0,
                CenterX = 0,
                CenterY = 0,
                BorderRatio = 0,
                Symmetry = 0
            };
        }

        private static int Gray(Color color)
        {
            return (
                color.R * 299 +
                color.G * 587 +
                color.B * 114
            ) / 1000;
        }

        private static int ClampInt(
            int value,
            int minimum,
            int maximum)
        {
            if (value < minimum)
                return minimum;

            if (value > maximum)
                return maximum;

            return value;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                return 0;
            }

            if (value < 0)
                return 0;

            if (value > 1)
                return 1;

            return value;
        }
    }
}
