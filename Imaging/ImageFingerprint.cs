using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageFingerprint
    {
        // Purane ImageMatcher ke saath compatibility ke liye.
        public ulong Hash { get; set; }
        public double DarkRatio { get; set; }
        public double EdgeRatio { get; set; }

        // Naye strong ImageMatcher ke liye additional shape information.
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
        private const int ProjectionBins = 32;
        private const int RadialBins = 64;

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
                    int[,] grayValues = ReadGrayValues(normalized);

                    int threshold = CalculateOtsuThreshold(
                        grayValues,
                        normalized.Width,
                        normalized.Height
                    );

                    bool[,] mask = BuildForegroundMask(
                        grayValues,
                        normalized.Width,
                        normalized.Height,
                        threshold
                    );

                    CleanMask(
                        mask,
                        normalized.Width,
                        normalized.Height
                    );

                    Rectangle bounds = FindForegroundBounds(
                        mask,
                        normalized.Width,
                        normalized.Height
                    );

                    if (bounds.Width < 3 ||
                        bounds.Height < 3)
                    {
                        return Empty();
                    }

                    bool[,] centeredMask = CenterForeground(
                        mask,
                        bounds,
                        WorkingSize,
                        WorkingSize
                    );

                    Rectangle centeredBounds =
                        FindForegroundBounds(
                            centeredMask,
                            WorkingSize,
                            WorkingSize
                        );

                    if (centeredBounds.Width < 3 ||
                        centeredBounds.Height < 3)
                    {
                        return Empty();
                    }

                    bool[,] edgeMask = BuildEdgeMask(
                        centeredMask,
                        WorkingSize,
                        WorkingSize
                    );

                    double darkRatio = CalculateTrueRatio(
                        centeredMask,
                        WorkingSize,
                        WorkingSize
                    );

                    double edgeRatio = CalculateTrueRatio(
                        edgeMask,
                        WorkingSize,
                        WorkingSize
                    );

                    PointF centroid = CalculateCentroid(
                        centeredMask,
                        WorkingSize,
                        WorkingSize
                    );

                    double aspectRatio =
                        centeredBounds.Height > 0
                            ? centeredBounds.Width /
                              (double)centeredBounds.Height
                            : 1.0;

                    double borderRatio = CalculateBorderRatio(
                        centeredMask,
                        centeredBounds
                    );

                    double symmetry = CalculateSymmetry(
                        centeredMask,
                        centeredBounds
                    );

                    ulong occupancyHash = BuildGridHash(
                        centeredMask,
                        WorkingSize,
                        WorkingSize
                    );

                    ulong edgeHash = BuildGridHash(
                        edgeMask,
                        WorkingSize,
                        WorkingSize
                    );

                    ulong horizontalHash =
                        BuildHorizontalProjectionHash(
                            centeredMask,
                            centeredBounds
                        );

                    ulong verticalHash =
                        BuildVerticalProjectionHash(
                            centeredMask,
                            centeredBounds
                        );

                    ulong radialHash = BuildRadialHash(
                        centeredMask,
                        centeredBounds,
                        centroid
                    );

                    return new ImageFingerprint
                    {
                        Hash = occupancyHash,
                        DarkRatio = darkRatio,
                        EdgeRatio = edgeRatio,

                        EdgeHash = edgeHash,
                        HorizontalHash = horizontalHash,
                        VerticalHash = verticalHash,
                        RadialHash = radialHash,

                        AspectRatio = aspectRatio,
                        CenterX = centroid.X /
                                  Math.Max(1.0, WorkingSize - 1.0),
                        CenterY = centroid.Y /
                                  Math.Max(1.0, WorkingSize - 1.0),
                        BorderRatio = borderRatio,
                        Symmetry = symmetry
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

            if (HasExtendedData(first) &&
                HasExtendedData(second))
            {
                return CompareExtended(first, second);
            }

            // Purane 32-byte descriptor ke liye fallback.
            return CompareLegacy(first, second);
        }

        private static double CompareExtended(
            ImageFingerprint first,
            ImageFingerprint second)
        {
            double occupancyScore = HashSimilarity(first.Hash, second.Hash);
            double edgeHashScore = HashSimilarity(first.EdgeHash, second.EdgeHash);
            double horizontalScore = HashSimilarity(first.HorizontalHash, second.HorizontalHash);
            double verticalScore = HashSimilarity(first.VerticalHash, second.VerticalHash);
            double radialScore = HashSimilarity(first.RadialHash, second.RadialHash);

            double darkScore = SimilarityFromDifference(
                Math.Abs(first.DarkRatio - second.DarkRatio), 0.28);

            double edgeRatioScore = SimilarityFromDifference(
                Math.Abs(first.EdgeRatio - second.EdgeRatio), 0.24);

            double aspectScore = RatioSimilarity(
                first.AspectRatio, second.AspectRatio);

            double centroidScore = SimilarityFromDifference(
                Distance(
                    first.CenterX,
                    first.CenterY,
                    second.CenterX,
                    second.CenterY),
                0.28);

            double holeScore = SimilarityFromDifference(
                Math.Abs(first.BorderRatio - second.BorderRatio), 0.22);

            double symmetryScore = SimilarityFromDifference(
                Math.Abs(first.Symmetry - second.Symmetry), 0.34);

            double score =
                edgeHashScore * 0.25 +
                radialScore * 0.22 +
                horizontalScore * 0.14 +
                verticalScore * 0.14 +
                occupancyScore * 0.10 +
                aspectScore * 0.05 +
                holeScore * 0.04 +
                edgeRatioScore * 0.025 +
                darkScore * 0.02 +
                centroidScore * 0.01 +
                symmetryScore * 0.005;

            int structuralMatches = 0;

            if (edgeHashScore >= 0.68) structuralMatches++;
            if (radialScore >= 0.68) structuralMatches++;
            if (horizontalScore >= 0.68) structuralMatches++;
            if (verticalScore >= 0.68) structuralMatches++;
            if (occupancyScore >= 0.68) structuralMatches++;

            if (edgeHashScore < 0.42)
                score *= 0.62;
            else if (edgeHashScore < 0.54)
                score *= 0.78;

            if (radialScore < 0.42)
                score *= 0.66;
            else if (radialScore < 0.54)
                score *= 0.82;

            if (horizontalScore < 0.44 &&
                verticalScore < 0.44)
            {
                score *= 0.72;
            }

            if (aspectScore < 0.52)
                score *= 0.72;
            else if (aspectScore < 0.66)
                score *= 0.86;

            if (holeScore < 0.40)
                score *= 0.80;

            if (edgeRatioScore < 0.38)
                score *= 0.78;

            if (occupancyScore >= 0.68 &&
                edgeHashScore < 0.50 &&
                radialScore < 0.50)
            {
                score *= 0.58;
            }

            if (structuralMatches == 0)
                score *= 0.58;
            else if (structuralMatches == 1)
                score *= 0.76;
            else if (structuralMatches == 2)
                score *= 0.90;

            if (structuralMatches < 2 && score > 0.58)
                score = 0.58;

            if (structuralMatches < 3 && score > 0.72)
                score = 0.72;

            if (structuralMatches < 4 && score > 0.86)
                score = 0.86;

            if (structuralMatches >= 4 &&
                edgeHashScore >= 0.78 &&
                radialScore >= 0.76 &&
                aspectScore >= 0.75)
            {
                score += 0.035;
            }

            score = Clamp01(score);

            return Math.Round(score * 100.0, 2);
        }

        private static double CompareLegacy(
            ImageFingerprint first,
            ImageFingerprint second)
        {
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
                hashScore * 0.68 +
                SimilarityFromDifference(
                    darkDifference,
                    0.32
                ) * 0.14 +
                SimilarityFromDifference(
                    edgeDifference,
                    0.22
                ) * 0.18;

            if (hashDistance > 26)
                score *= 0.82;

            if (hashDistance > 34)
                score *= 0.72;

            if (darkDifference > 0.22)
                score *= 0.78;

            if (edgeDifference > 0.16)
                score *= 0.80;

            // Purane weak descriptor ko bina evidence 95–98% nahi dena.
            if (score > 0.90 &&
                hashDistance > 5)
            {
                score = 0.90;
            }

            score = Clamp01(score);

            return Math.Round(
                score * 100.0,
                2
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

        private static bool HasExtendedData(
            ImageFingerprint fingerprint)
        {
            if (fingerprint == null)
                return false;

            return
                fingerprint.EdgeHash != 0 ||
                fingerprint.HorizontalHash != 0 ||
                fingerprint.VerticalHash != 0 ||
                fingerprint.RadialHash != 0 ||
                fingerprint.AspectRatio > 0;
        }

        private static Bitmap Normalize(Bitmap source)
        {
            Bitmap result = new Bitmap(
                WorkingSize,
                WorkingSize,
                PixelFormat.Format24bppRgb
            );

            using (Graphics graphics =
                Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);

                graphics.CompositingMode =
                    CompositingMode.SourceCopy;

                graphics.CompositingQuality =
                    CompositingQuality.HighQuality;

                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;

                graphics.SmoothingMode =
                    SmoothingMode.HighQuality;

                graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                double scale = Math.Min(
                    (WorkingSize - 8.0) /
                    Math.Max(1, source.Width),
                    (WorkingSize - 8.0) /
                    Math.Max(1, source.Height)
                );

                int drawWidth = Math.Max(
                    1,
                    (int)Math.Round(
                        source.Width * scale
                    )
                );

                int drawHeight = Math.Max(
                    1,
                    (int)Math.Round(
                        source.Height * scale
                    )
                );

                int drawX =
                    (WorkingSize - drawWidth) / 2;

                int drawY =
                    (WorkingSize - drawHeight) / 2;

                graphics.DrawImage(
                    source,
                    new Rectangle(
                        drawX,
                        drawY,
                        drawWidth,
                        drawHeight
                    ),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel
                );
            }

            return result;
        }

        private static int[,] ReadGrayValues(
            Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            int[,] values =
                new int[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color color =
                        bitmap.GetPixel(x, y);

                    values[x, y] =
                        (
                            color.R * 299 +
                            color.G * 587 +
                            color.B * 114
                        ) / 1000;
                }
            }

            return values;
        }

        private static int CalculateOtsuThreshold(
            int[,] grayValues,
            int width,
            int height)
        {
            int[] histogram = new int[256];
            int totalPixels = width * height;

            if (totalPixels <= 0)
                return 180;

            long totalIntensity = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int value =
                        ClampInt(
                            grayValues[x, y],
                            0,
                            255
                        );

                    histogram[value]++;
                    totalIntensity += value;
                }
            }

            long backgroundIntensity = 0;
            int backgroundCount = 0;

            double maximumVariance = -1;
            int bestThreshold = 180;

            for (int threshold = 0;
                 threshold < 256;
                 threshold++)
            {
                backgroundCount +=
                    histogram[threshold];

                if (backgroundCount == 0)
                    continue;

                int foregroundCount =
                    totalPixels -
                    backgroundCount;

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

                if (variance > maximumVariance)
                {
                    maximumVariance = variance;
                    bestThreshold = threshold;
                }
            }

            return ClampInt(
                bestThreshold,
                70,
                225
            );
        }

        private static bool[,] BuildForegroundMask(
            int[,] grayValues,
            int width,
            int height,
            int threshold)
        {
            bool[,] darkMask =
                new bool[width, height];

            bool[,] lightMask =
                new bool[width, height];

            int darkCount = 0;
            int lightCount = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int gray =
                        grayValues[x, y];

                    bool dark =
                        gray <= threshold;

                    bool light =
                        gray >= 255 - threshold;

                    darkMask[x, y] = dark;
                    lightMask[x, y] = light;

                    if (dark)
                        darkCount++;

                    if (light)
                        lightCount++;
                }
            }

            double darkRatio =
                darkCount /
                (double)Math.Max(
                    1,
                    width * height
                );

            double lightRatio =
                lightCount /
                (double)Math.Max(
                    1,
                    width * height
                );

            // Usually jewellery/design dark hota hai aur background light.
            // Dark mask unreasonable ho to inverted image handle karo.
            if (darkRatio > 0.72 &&
                lightRatio > 0.015 &&
                lightRatio < darkRatio)
            {
                return lightMask;
            }

            return darkMask;
        }

        private static void CleanMask(
            bool[,] mask,
            int width,
            int height)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool[,] next =
                    new bool[width, height];

                for (int y = 1;
                     y < height - 1;
                     y++)
                {
                    for (int x = 1;
                         x < width - 1;
                         x++)
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

                CopyMask(
                    next,
                    mask,
                    width,
                    height
                );
            }

            // Single-pixel holes aur breaks ko halka close karo.
            bool[,] closed =
                new bool[width, height];

            for (int y = 1;
                 y < height - 1;
                 y++)
            {
                for (int x = 1;
                     x < width - 1;
                     x++)
                {
                    int neighbours =
                        CountNeighbours(
                            mask,
                            x,
                            y
                        );

                    closed[x, y] =
                        mask[x, y] ||
                        neighbours >= 5;
                }
            }

            CopyMask(
                closed,
                mask,
                width,
                height
            );
        }

        private static int CountNeighbours(
            bool[,] mask,
            int x,
            int y)
        {
            int count = 0;

            for (int yy = y - 1;
                 yy <= y + 1;
                 yy++)
            {
                for (int xx = x - 1;
                     xx <= x + 1;
                     xx++)
                {
                    if (xx == x && yy == y)
                        continue;

                    if (mask[xx, yy])
                        count++;
                }
            }

            return count;
        }

        private static void CopyMask(
            bool[,] source,
            bool[,] destination,
            int width,
            int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    destination[x, y] =
                        source[x, y];
                }
            }
        }

        private static Rectangle FindForegroundBounds(
            bool[,] mask,
            int width,
            int height)
        {
            int minimumX = width;
            int minimumY = height;
            int maximumX = -1;
            int maximumY = -1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y])
                        continue;

                    if (x < minimumX)
                        minimumX = x;

                    if (x > maximumX)
                        maximumX = x;

                    if (y < minimumY)
                        minimumY = y;

                    if (y > maximumY)
                        maximumY = y;
                }
            }

            if (maximumX < minimumX ||
                maximumY < minimumY)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(
                minimumX,
                minimumY,
                maximumX + 1,
                maximumY + 1
            );
        }

        private static bool[,] CenterForeground(
            bool[,] source,
            Rectangle bounds,
            int targetWidth,
            int targetHeight)
        {
            bool[,] result =
                new bool[targetWidth, targetHeight];

            if (bounds.Width <= 0 ||
                bounds.Height <= 0)
            {
                return result;
            }

            double scale = Math.Min(
                (targetWidth - 12.0) /
                bounds.Width,
                (targetHeight - 12.0) /
                bounds.Height
            );

            int destinationWidth =
                Math.Max(
                    1,
                    (int)Math.Round(
                        bounds.Width * scale
                    )
                );

            int destinationHeight =
                Math.Max(
                    1,
                    (int)Math.Round(
                        bounds.Height * scale
                    )
                );

            int destinationX =
                (targetWidth -
                 destinationWidth) / 2;

            int destinationY =
                (targetHeight -
                 destinationHeight) / 2;

            for (int y = 0;
                 y < destinationHeight;
                 y++)
            {
                double sourceY =
                    bounds.Top +
                    (
                        y + 0.5
                    ) /
                    Math.Max(
                        0.0001,
                        scale
                    );

                int originalY =
                    ClampInt(
                        (int)sourceY,
                        bounds.Top,
                        bounds.Bottom - 1
                    );

                int finalY =
                    destinationY + y;

                if (finalY < 0 ||
                    finalY >= targetHeight)
                {
                    continue;
                }

                for (int x = 0;
                     x < destinationWidth;
                     x++)
                {
                    double sourceX =
                        bounds.Left +
                        (
                            x + 0.5
                        ) /
                        Math.Max(
                            0.0001,
                            scale
                        );

                    int originalX =
                        ClampInt(
                            (int)sourceX,
                            bounds.Left,
                            bounds.Right - 1
                        );

                    int finalX =
                        destinationX + x;

                    if (finalX < 0 ||
                        finalX >= targetWidth)
                    {
                        continue;
                    }

                    result[finalX, finalY] =
                        source[
                            originalX,
                            originalY
                        ];
                }
            }

            return result;
        }

        private static bool[,] BuildEdgeMask(
            bool[,] mask,
            int width,
            int height)
        {
            bool[,] edges =
                new bool[width, height];

            for (int y = 1;
                 y < height - 1;
                 y++)
            {
                for (int x = 1;
                     x < width - 1;
                     x++)
                {
                    if (!mask[x, y])
                        continue;

                    bool isEdge =
                        !mask[x - 1, y] ||
                        !mask[x + 1, y] ||
                        !mask[x, y - 1] ||
                        !mask[x, y + 1] ||
                        !mask[x - 1, y - 1] ||
                        !mask[x + 1, y - 1] ||
                        !mask[x - 1, y + 1] ||
                        !mask[x + 1, y + 1];

                    edges[x, y] = isEdge;
                }
            }

            return edges;
        }

        private static ulong BuildGridHash(
            bool[,] mask,
            int width,
            int height)
        {
            double[] ratios =
                new double[GridSize * GridSize];

            double ratioTotal = 0;

            for (int gridY = 0;
                 gridY < GridSize;
                 gridY++)
            {
                int startY =
                    gridY * height /
                    GridSize;

                int endY =
                    (gridY + 1) *
                    height /
                    GridSize;

                for (int gridX = 0;
                     gridX < GridSize;
                     gridX++)
                {
                    int startX =
                        gridX * width /
                        GridSize;

                    int endX =
                        (gridX + 1) *
                        width /
                        GridSize;

                    int truePixels = 0;
                    int totalPixels = 0;

                    for (int y = startY;
                         y < endY;
                         y++)
                    {
                        for (int x = startX;
                             x < endX;
                             x++)
                        {
                            if (mask[x, y])
                                truePixels++;

                            totalPixels++;
                        }
                    }

                    double ratio =
                        totalPixels > 0
                            ? truePixels /
                              (double)totalPixels
                            : 0;

                    int index =
                        gridY * GridSize +
                        gridX;

                    ratios[index] = ratio;
                    ratioTotal += ratio;
                }
            }

            double average =
                ratioTotal /
                Math.Max(
                    1,
                    ratios.Length
                );

            ulong hash = 0;

            for (int i = 0;
                 i < ratios.Length;
                 i++)
            {
                double threshold =
                    Math.Max(
                        0.035,
                        average * 0.72
                    );

                if (ratios[i] >= threshold)
                    hash |= 1UL << i;
            }

            return hash;
        }

        private static ulong BuildHorizontalProjectionHash(
            bool[,] mask,
            Rectangle bounds)
        {
            double[] bins =
                new double[ProjectionBins];

            for (int bin = 0;
                 bin < ProjectionBins;
                 bin++)
            {
                int startY =
                    bounds.Top +
                    bin *
                    bounds.Height /
                    ProjectionBins;

                int endY =
                    bounds.Top +
                    (bin + 1) *
                    bounds.Height /
                    ProjectionBins;

                if (endY <= startY)
                    endY = startY + 1;

                int truePixels = 0;
                int totalPixels = 0;

                for (int y = startY;
                     y < endY &&
                     y < bounds.Bottom;
                     y++)
                {
                    for (int x = bounds.Left;
                         x < bounds.Right;
                         x++)
                    {
                        if (mask[x, y])
                            truePixels++;

                        totalPixels++;
                    }
                }

                bins[bin] =
                    totalPixels > 0
                        ? truePixels /
                          (double)totalPixels
                        : 0;
            }

            return BuildProjectionHash(bins);
        }

        private static ulong BuildVerticalProjectionHash(
            bool[,] mask,
            Rectangle bounds)
        {
            double[] bins =
                new double[ProjectionBins];

            for (int bin = 0;
                 bin < ProjectionBins;
                 bin++)
            {
                int startX =
                    bounds.Left +
                    bin *
                    bounds.Width /
                    ProjectionBins;

                int endX =
                    bounds.Left +
                    (bin + 1) *
                    bounds.Width /
                    ProjectionBins;

                if (endX <= startX)
                    endX = startX + 1;

                int truePixels = 0;
                int totalPixels = 0;

                for (int x = startX;
                     x < endX &&
                     x < bounds.Right;
                     x++)
                {
                    for (int y = bounds.Top;
                         y < bounds.Bottom;
                         y++)
                    {
                        if (mask[x, y])
                            truePixels++;

                        totalPixels++;
                    }
                }

                bins[bin] =
                    totalPixels > 0
                        ? truePixels /
                          (double)totalPixels
                        : 0;
            }

            return BuildProjectionHash(bins);
        }

        private static ulong BuildProjectionHash(
            double[] bins)
        {
            if (bins == null ||
                bins.Length == 0)
            {
                return 0;
            }

            double average = 0;

            for (int i = 0;
                 i < bins.Length;
                 i++)
            {
                average += bins[i];
            }

            average /=
                Math.Max(
                    1,
                    bins.Length
                );

            ulong hash = 0;

            // First 32 bits = bin occupancy.
            for (int i = 0;
                 i < bins.Length &&
                 i < 32;
                 i++)
            {
                if (bins[i] >= average)
                    hash |= 1UL << i;
            }

            // Next 31 bits = direction/change between adjacent bins.
            for (int i = 0;
                 i < bins.Length - 1 &&
                 i < 31;
                 i++)
            {
                if (bins[i + 1] >= bins[i])
                    hash |= 1UL << (32 + i);
            }

            // Last bit = overall strong density.
            if (average >= 0.32)
                hash |= 1UL << 63;

            return hash;
        }

        private static ulong BuildRadialHash(
            bool[,] mask,
            Rectangle bounds,
            PointF centroid)
        {
            double maximumRadius =
                Math.Sqrt(
                    bounds.Width *
                    bounds.Width +
                    bounds.Height *
                    bounds.Height
                ) / 2.0;

            if (maximumRadius <= 0)
                return 0;

            int[] trueCounts =
                new int[RadialBins];

            int[] totalCounts =
                new int[RadialBins];

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int x = bounds.Left;
                     x < bounds.Right;
                     x++)
                {
                    double radius =
                        Distance(
                            x,
                            y,
                            centroid.X,
                            centroid.Y
                        );

                    int bin =
                        ClampInt(
                            (int)(
                                radius /
                                maximumRadius *
                                RadialBins
                            ),
                            0,
                            RadialBins - 1
                        );

                    totalCounts[bin]++;

                    if (mask[x, y])
                        trueCounts[bin]++;
                }
            }

            double[] ratios =
                new double[RadialBins];

            double average = 0;

            for (int i = 0;
                 i < RadialBins;
                 i++)
            {
                ratios[i] =
                    totalCounts[i] > 0
                        ? trueCounts[i] /
                          (double)totalCounts[i]
                        : 0;

                average += ratios[i];
            }

            average /=
                Math.Max(
                    1,
                    RadialBins
                );

            ulong hash = 0;

            for (int i = 0;
                 i < RadialBins;
                 i++)
            {
                if (ratios[i] >= average)
                    hash |= 1UL << i;
            }

            return hash;
        }

        private static PointF CalculateCentroid(
            bool[,] mask,
            int width,
            int height)
        {
            double sumX = 0;
            double sumY = 0;
            int count = 0;

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    if (!mask[x, y])
                        continue;

                    sumX += x;
                    sumY += y;
                    count++;
                }
            }

            if (count <= 0)
            {
                return new PointF(
                    width / 2.0f,
                    height / 2.0f
                );
            }

            return new PointF(
                (float)(sumX / count),
                (float)(sumY / count)
            );
        }

        private static double CalculateBorderRatio(
            bool[,] mask,
            Rectangle bounds)
        {
            if (bounds.Width <= 0 ||
                bounds.Height <= 0)
            {
                return 0;
            }

            int borderPixels = 0;
            int foregroundPixels = 0;

            int borderThickness =
                Math.Max(
                    1,
                    Math.Min(
                        bounds.Width,
                        bounds.Height
                    ) / 12
                );

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

                    foregroundPixels++;

                    bool nearBorder =
                        x < bounds.Left +
                            borderThickness ||
                        x >= bounds.Right -
                            borderThickness ||
                        y < bounds.Top +
                            borderThickness ||
                        y >= bounds.Bottom -
                            borderThickness;

                    if (nearBorder)
                        borderPixels++;
                }
            }

            if (foregroundPixels <= 0)
                return 0;

            return borderPixels /
                   (double)foregroundPixels;
        }

        private static double CalculateSymmetry(
            bool[,] mask,
            Rectangle bounds)
        {
            if (bounds.Width <= 1 ||
                bounds.Height <= 1)
            {
                return 0;
            }

            int matches = 0;
            int comparisons = 0;

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int offset = 0;
                     offset < bounds.Width / 2;
                     offset++)
                {
                    int leftX =
                        bounds.Left + offset;

                    int rightX =
                        bounds.Right -
                        1 -
                        offset;

                    if (mask[leftX, y] ==
                        mask[rightX, y])
                    {
                        matches++;
                    }

                    comparisons++;
                }
            }

            if (comparisons <= 0)
                return 0;

            return matches /
                   (double)comparisons;
        }

        private static double CalculateTrueRatio(
            bool[,] mask,
            int width,
            int height)
        {
            int truePixels = 0;
            int totalPixels = width * height;

            if (totalPixels <= 0)
                return 0;

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    if (mask[x, y])
                        truePixels++;
                }
            }

            return truePixels /
                   (double)totalPixels;
        }

        private static void CountSignature(
            double score,
            ref int strong,
            ref int medium,
            ref int weak)
        {
            if (score >= 0.76)
                strong++;

            if (score >= 0.62)
                medium++;

            if (score < 0.48)
                weak++;
        }

        private static double HashSimilarity(
            ulong first,
            ulong second)
        {
            int distance =
                Hamming(first ^ second);

            return Clamp01(
                1.0 -
                distance / 64.0
            );
        }

        private static double RatioSimilarity(
            double first,
            double second)
        {
            double difference =
                NormalizedRatioDifference(
                    first,
                    second
                );

            return Clamp01(
                1.0 -
                difference
            );
        }

        private static double NormalizedRatioDifference(
            double first,
            double second)
        {
            if (first <= 0 ||
                second <= 0)
            {
                return 1;
            }

            double maximum =
                Math.Max(first, second);

            if (maximum <= 0)
                return 1;

            return Math.Abs(
                       first -
                       second
                   ) /
                   maximum;
        }

        private static double SimilarityFromDifference(
            double difference,
            double maximumDifference)
        {
            if (maximumDifference <= 0)
                return 0;

            return Clamp01(
                1.0 -
                difference /
                maximumDifference
            );
        }

        private static double Distance(
            double firstX,
            double firstY,
            double secondX,
            double secondY)
        {
            double differenceX =
                firstX -
                secondX;

            double differenceY =
                firstY -
                secondY;

            return Math.Sqrt(
                differenceX *
                differenceX +
                differenceY *
                differenceY
            );
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

        private static double Clamp01(
            double value)
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
    }
}
