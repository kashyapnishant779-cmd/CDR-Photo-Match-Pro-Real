using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageSegment
    {
        public string Name { get; set; }
        public Bitmap Bitmap { get; set; }
        public Rectangle Bounds { get; set; }
        public double Weight { get; set; }
    }

    public static class ImageSegmenter
    {
        private const int WorkingSize = 700;
        private const int NormalizedSize = 256;

        private sealed class Component
        {
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;
            public int PixelCount;
            public bool TouchesBorder;

            public int Width
            {
                get
                {
                    return MaxX - MinX + 1;
                }
            }

            public int Height
            {
                get
                {
                    return MaxY - MinY + 1;
                }
            }

            public Rectangle Bounds
            {
                get
                {
                    return new Rectangle(
                        MinX,
                        MinY,
                        Math.Max(1, Width),
                        Math.Max(1, Height)
                    );
                }
            }
        }

        private sealed class MaskCandidate
        {
            public bool[,] Mask;
            public Rectangle Bounds;
            public double Score;
        }

        public static List<ImageSegment> Split(
            Bitmap source)
        {
            return Segment(source);
        }

        public static List<ImageSegment> Segment(
            Bitmap source)
        {
            var segments =
                new List<ImageSegment>();

            if (source == null ||
                source.Width <= 0 ||
                source.Height <= 0)
            {
                return segments;
            }

            Bitmap clean = null;

            try
            {
                clean =
                    ExtractMainDesign(source);

                if (clean == null)
                    return segments;

                int width = clean.Width;
                int height = clean.Height;

                /*
                 * Exact required order:
                 *
                 * 0 = FULL
                 * 1 = CENTER
                 * 2 = LEFT
                 * 3 = RIGHT
                 * 4 = TOP
                 * 5 = BOTTOM
                 */

                AddSegment(
                    segments,
                    clean,
                    "FULL",
                    new Rectangle(
                        0,
                        0,
                        width,
                        height
                    ),
                    1.00
                );

                AddSegment(
                    segments,
                    clean,
                    "CENTER",
                    new Rectangle(
                        width * 17 / 100,
                        height * 17 / 100,
                        width * 66 / 100,
                        height * 66 / 100
                    ),
                    0.95
                );

                AddSegment(
                    segments,
                    clean,
                    "LEFT",
                    new Rectangle(
                        0,
                        height * 8 / 100,
                        width * 58 / 100,
                        height * 84 / 100
                    ),
                    0.68
                );

                AddSegment(
                    segments,
                    clean,
                    "RIGHT",
                    new Rectangle(
                        width * 42 / 100,
                        height * 8 / 100,
                        width * 58 / 100,
                        height * 84 / 100
                    ),
                    0.68
                );

                AddSegment(
                    segments,
                    clean,
                    "TOP",
                    new Rectangle(
                        width * 6 / 100,
                        0,
                        width * 88 / 100,
                        height * 48 / 100
                    ),
                    0.74
                );

                AddSegment(
                    segments,
                    clean,
                    "BOTTOM",
                    new Rectangle(
                        width * 6 / 100,
                        height * 52 / 100,
                        width * 88 / 100,
                        height * 48 / 100
                    ),
                    0.78
                );
            }
            catch
            {
                DisposeSegments(segments);
                segments.Clear();
            }
            finally
            {
                if (clean != null)
                    clean.Dispose();
            }

            return segments;
        }

        public static Bitmap ExtractMainDesign(
            Bitmap source)
        {
            Bitmap working =
                ResizeKeep(
                    source,
                    WorkingSize,
                    WorkingSize
                );

            try
            {
                MaskCandidate best =
                    BuildBestMaskCandidate(
                        working
                    );

                if (best == null ||
                    best.Mask == null ||
                    best.Bounds == Rectangle.Empty)
                {
                    return CreateFallbackSilhouette(
                        working
                    );
                }

                Rectangle paddedBounds =
                    AddPadding(
                        best.Bounds,
                        working.Width,
                        working.Height
                    );

                Bitmap rendered =
                    RenderMaskCrop(
                        best.Mask,
                        paddedBounds
                    );

                try
                {
                    Bitmap normalized =
                        NormalizeSilhouette(
                            rendered
                        );

                    return normalized;
                }
                finally
                {
                    rendered.Dispose();
                }
            }
            catch
            {
                return CreateFallbackSilhouette(
                    working
                );
            }
            finally
            {
                working.Dispose();
            }
        }

        private static MaskCandidate BuildBestMaskCandidate(
            Bitmap bitmap)
        {
            var candidates =
                new List<MaskCandidate>();

            int[,] gray =
                ReadGrayValues(bitmap);

            Color background =
                EstimateBackground(bitmap);

            int otsuThreshold =
                CalculateOtsuThreshold(
                    gray,
                    bitmap.Width,
                    bitmap.Height
                );

            AddMaskCandidate(
                candidates,
                BuildDarkMask(
                    bitmap,
                    gray,
                    otsuThreshold
                ),
                bitmap.Width,
                bitmap.Height
            );

            AddMaskCandidate(
                candidates,
                BuildAdaptiveDarkMask(
                    gray,
                    bitmap.Width,
                    bitmap.Height
                ),
                bitmap.Width,
                bitmap.Height
            );

            AddMaskCandidate(
                candidates,
                BuildBackgroundDifferenceMask(
                    bitmap,
                    background
                ),
                bitmap.Width,
                bitmap.Height
            );

            AddMaskCandidate(
                candidates,
                BuildColourJewelleryMask(
                    bitmap,
                    background
                ),
                bitmap.Width,
                bitmap.Height
            );

            AddMaskCandidate(
                candidates,
                BuildEdgeRegionMask(
                    gray,
                    bitmap.Width,
                    bitmap.Height
                ),
                bitmap.Width,
                bitmap.Height
            );

            MaskCandidate best = null;
            double bestScore =
                double.MinValue;

            for (int index = 0;
                 index < candidates.Count;
                 index++)
            {
                MaskCandidate candidate =
                    candidates[index];

                if (candidate == null)
                    continue;

                if (candidate.Score >
                    bestScore)
                {
                    bestScore =
                        candidate.Score;

                    best = candidate;
                }
            }

            return best;
        }

        private static void AddMaskCandidate(
            List<MaskCandidate> candidates,
            bool[,] mask,
            int width,
            int height)
        {
            if (mask == null)
                return;

            CleanMask(
                mask,
                width,
                height
            );

            RemoveBorderConnectedNoise(
                mask,
                width,
                height
            );

            CloseSmallGaps(
                mask,
                width,
                height
            );

            List<Component> components =
                FindComponents(
                    mask,
                    width,
                    height
                );

            Rectangle bounds =
                SelectJewelleryBounds(
                    components,
                    width,
                    height
                );

            if (bounds == Rectangle.Empty)
                return;

            bool[,] selectedMask =
                KeepComponentsNearBounds(
                    mask,
                    components,
                    bounds,
                    width,
                    height
                );

            Rectangle selectedBounds =
                FindMaskBounds(
                    selectedMask,
                    width,
                    height
                );

            if (selectedBounds ==
                Rectangle.Empty)
            {
                return;
            }

            double score =
                ScoreMaskCandidate(
                    selectedMask,
                    selectedBounds,
                    width,
                    height
                );

            candidates.Add(
                new MaskCandidate
                {
                    Mask = selectedMask,
                    Bounds = selectedBounds,
                    Score = score
                }
            );
        }

        private static bool[,] BuildDarkMask(
            Bitmap bitmap,
            int[,] gray,
            int threshold)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] mask =
                new bool[width, height];

            int limitedThreshold =
                ClampInt(
                    threshold + 12,
                    75,
                    205
                );

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    Color color =
                        bitmap.GetPixel(x, y);

                    bool excluded =
                        IsLikelySkin(color) ||
                        IsNearWhite(color);

                    mask[x, y] =
                        !excluded &&
                        gray[x, y] <=
                        limitedThreshold;
                }
            }

            return mask;
        }

        private static bool[,] BuildAdaptiveDarkMask(
            int[,] gray,
            int width,
            int height)
        {
            bool[,] mask =
                new bool[width, height];

            int radius =
                Math.Max(
                    5,
                    Math.Min(
                        width,
                        height
                    ) / 35
                );

            long[,] integral =
                BuildIntegralImage(
                    gray,
                    width,
                    height
                );

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    int left =
                        Math.Max(
                            0,
                            x - radius
                        );

                    int top =
                        Math.Max(
                            0,
                            y - radius
                        );

                    int right =
                        Math.Min(
                            width - 1,
                            x + radius
                        );

                    int bottom =
                        Math.Min(
                            height - 1,
                            y + radius
                        );

                    double localAverage =
                        ReadIntegralAverage(
                            integral,
                            left,
                            top,
                            right,
                            bottom
                        );

                    double difference =
                        localAverage -
                        gray[x, y];

                    mask[x, y] =
                        difference >= 19 &&
                        gray[x, y] < 220;
                }
            }

            return mask;
        }

        private static bool[,] BuildBackgroundDifferenceMask(
            Bitmap bitmap,
            Color background)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] mask =
                new bool[width, height];

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    Color color =
                        bitmap.GetPixel(x, y);

                    int colorDifference =
                        Math.Abs(
                            color.R -
                            background.R
                        ) +
                        Math.Abs(
                            color.G -
                            background.G
                        ) +
                        Math.Abs(
                            color.B -
                            background.B
                        );

                    int grayDifference =
                        Math.Abs(
                            Gray(color) -
                            Gray(background)
                        );

                    bool excluded =
                        IsLikelySkin(color) ||
                        IsNearWhite(color);

                    mask[x, y] =
                        !excluded &&
                        (
                            colorDifference >= 88 ||
                            grayDifference >= 38
                        );
                }
            }

            return mask;
        }

        private static bool[,] BuildColourJewelleryMask(
            Bitmap bitmap,
            Color background)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] mask =
                new bool[width, height];

            int backgroundGray =
                Gray(background);

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    Color color =
                        bitmap.GetPixel(x, y);

                    int maximum =
                        Math.Max(
                            color.R,
                            Math.Max(
                                color.G,
                                color.B
                            )
                        );

                    int minimum =
                        Math.Min(
                            color.R,
                            Math.Min(
                                color.G,
                                color.B
                            )
                        );

                    int saturation =
                        maximum -
                        minimum;

                    int gray =
                        Gray(color);

                    bool goldLike =
                        color.R > 95 &&
                        color.G > 55 &&
                        color.R >
                            color.B + 20 &&
                        color.G >
                            color.B + 5 &&
                        saturation > 24;

                    bool colouredStone =
                        saturation > 48 &&
                        gray > 35 &&
                        gray < 238;

                    bool darkDetail =
                        gray < 142 &&
                        Math.Abs(
                            gray -
                            backgroundGray
                        ) > 22;

                    bool excluded =
                        IsLikelySkin(color) ||
                        IsNearWhite(color) ||
                        IsLikelyBlueRuler(color);

                    mask[x, y] =
                        !excluded &&
                        (
                            goldLike ||
                            colouredStone ||
                            darkDetail
                        );
                }
            }

            return mask;
        }

        private static bool[,] BuildEdgeRegionMask(
            int[,] gray,
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
                    int horizontal =
                        Math.Abs(
                            gray[x + 1, y] -
                            gray[x - 1, y]
                        );

                    int vertical =
                        Math.Abs(
                            gray[x, y + 1] -
                            gray[x, y - 1]
                        );

                    int diagonalOne =
                        Math.Abs(
                            gray[x + 1, y + 1] -
                            gray[x - 1, y - 1]
                        );

                    int diagonalTwo =
                        Math.Abs(
                            gray[x + 1, y - 1] -
                            gray[x - 1, y + 1]
                        );

                    int strength =
                        horizontal +
                        vertical +
                        diagonalOne / 2 +
                        diagonalTwo / 2;

                    edges[x, y] =
                        strength >= 84;
                }
            }

            bool[,] expanded =
                new bool[width, height];

            for (int y = 2;
                 y < height - 2;
                 y++)
            {
                for (int x = 2;
                     x < width - 2;
                     x++)
                {
                    int count = 0;

                    for (int yy = y - 2;
                         yy <= y + 2;
                         yy++)
                    {
                        for (int xx = x - 2;
                             xx <= x + 2;
                             xx++)
                        {
                            if (edges[xx, yy])
                                count++;
                        }
                    }

                    expanded[x, y] =
                        count >= 3;
                }
            }

            return expanded;
        }

        private static int[,] ReadGrayValues(
            Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            int[,] values =
                new int[width, height];

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    values[x, y] =
                        Gray(
                            bitmap.GetPixel(
                                x,
                                y
                            )
                        );
                }
            }

            return values;
        }

        private static int CalculateOtsuThreshold(
            int[,] gray,
            int width,
            int height)
        {
            int[] histogram =
                new int[256];

            int totalPixels =
                width * height;

            if (totalPixels <= 0)
                return 160;

            long totalIntensity = 0;

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    int value =
                        ClampInt(
                            gray[x, y],
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
            int bestThreshold = 160;

            for (int threshold = 0;
                 threshold < 256;
                 threshold++)
            {
                backgroundCount +=
                    histogram[threshold];

                if (backgroundCount <= 0)
                    continue;

                int foregroundCount =
                    totalPixels -
                    backgroundCount;

                if (foregroundCount <= 0)
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

                if (variance >
                    maximumVariance)
                {
                    maximumVariance =
                        variance;

                    bestThreshold =
                        threshold;
                }
            }

            return ClampInt(
                bestThreshold,
                65,
                215
            );
        }

        private static long[,] BuildIntegralImage(
            int[,] values,
            int width,
            int height)
        {
            long[,] integral =
                new long[
                    width + 1,
                    height + 1
                ];

            for (int y = 1;
                 y <= height;
                 y++)
            {
                long rowTotal = 0;

                for (int x = 1;
                     x <= width;
                     x++)
                {
                    rowTotal +=
                        values[x - 1, y - 1];

                    integral[x, y] =
                        integral[x, y - 1] +
                        rowTotal;
                }
            }

            return integral;
        }

        private static double ReadIntegralAverage(
            long[,] integral,
            int left,
            int top,
            int right,
            int bottom)
        {
            int x1 = left;
            int y1 = top;
            int x2 = right + 1;
            int y2 = bottom + 1;

            long sum =
                integral[x2, y2] -
                integral[x1, y2] -
                integral[x2, y1] +
                integral[x1, y1];

            int area =
                Math.Max(
                    1,
                    (right - left + 1) *
                    (bottom - top + 1)
                );

            return sum /
                   (double)area;
        }

        private static void CleanMask(
            bool[,] mask,
            int width,
            int height)
        {
            for (int pass = 0;
                 pass < 2;
                 pass++)
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
        }

        private static void CloseSmallGaps(
            bool[,] mask,
            int width,
            int height)
        {
            for (int pass = 0;
                 pass < 2;
                 pass++)
            {
                bool[,] next =
                    new bool[width, height];

                for (int y = 2;
                     y < height - 2;
                     y++)
                {
                    for (int x = 2;
                         x < width - 2;
                         x++)
                    {
                        int nearby = 0;

                        for (int yy = y - 2;
                             yy <= y + 2;
                             yy++)
                        {
                            for (int xx = x - 2;
                                 xx <= x + 2;
                                 xx++)
                            {
                                if (mask[xx, yy])
                                    nearby++;
                            }
                        }

                        next[x, y] =
                            mask[x, y] ||
                            nearby >= 11;
                    }
                }

                CopyMask(
                    next,
                    mask,
                    width,
                    height
                );
            }
        }

        private static void RemoveBorderConnectedNoise(
            bool[,] mask,
            int width,
            int height)
        {
            bool[,] visited =
                new bool[width, height];

            var queue =
                new Queue<Point>();

            for (int x = 0;
                 x < width;
                 x++)
            {
                EnqueueBorderPoint(
                    mask,
                    visited,
                    queue,
                    x,
                    0
                );

                EnqueueBorderPoint(
                    mask,
                    visited,
                    queue,
                    x,
                    height - 1
                );
            }

            for (int y = 0;
                 y < height;
                 y++)
            {
                EnqueueBorderPoint(
                    mask,
                    visited,
                    queue,
                    0,
                    y
                );

                EnqueueBorderPoint(
                    mask,
                    visited,
                    queue,
                    width - 1,
                    y
                );
            }

            int[] differenceX =
            {
                -1, 0, 1,
                -1, 1,
                -1, 0, 1
            };

            int[] differenceY =
            {
                -1, -1, -1,
                0, 0,
                1, 1, 1
            };

            while (queue.Count > 0)
            {
                Point point =
                    queue.Dequeue();

                mask[point.X, point.Y] =
                    false;

                for (int index = 0;
                     index < 8;
                     index++)
                {
                    int nextX =
                        point.X +
                        differenceX[index];

                    int nextY =
                        point.Y +
                        differenceY[index];

                    if (nextX < 0 ||
                        nextY < 0 ||
                        nextX >= width ||
                        nextY >= height)
                    {
                        continue;
                    }

                    if (!mask[nextX, nextY] ||
                        visited[nextX, nextY])
                    {
                        continue;
                    }

                    visited[nextX, nextY] =
                        true;

                    queue.Enqueue(
                        new Point(
                            nextX,
                            nextY
                        )
                    );
                }
            }
        }

        private static void EnqueueBorderPoint(
            bool[,] mask,
            bool[,] visited,
            Queue<Point> queue,
            int x,
            int y)
        {
            if (x < 0 ||
                y < 0 ||
                x >= mask.GetLength(0) ||
                y >= mask.GetLength(1))
            {
                return;
            }

            if (!mask[x, y] ||
                visited[x, y])
            {
                return;
            }

            visited[x, y] = true;

            queue.Enqueue(
                new Point(
                    x,
                    y
                )
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
                    if (xx == x &&
                        yy == y)
                    {
                        continue;
                    }

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
            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    destination[x, y] =
                        source[x, y];
                }
            }
        }

        private static List<Component> FindComponents(
            bool[,] mask,
            int width,
            int height)
        {
            var components =
                new List<Component>();

            bool[,] visited =
                new bool[width, height];

            int[] differenceX =
            {
                -1, 0, 1,
                -1, 1,
                -1, 0, 1
            };

            int[] differenceY =
            {
                -1, -1, -1,
                0, 0,
                1, 1, 1
            };

            for (int y = 0;
                 y < height;
                 y++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    if (!mask[x, y] ||
                        visited[x, y])
                    {
                        continue;
                    }

                    var queue =
                        new Queue<Point>();

                    queue.Enqueue(
                        new Point(
                            x,
                            y
                        )
                    );

                    visited[x, y] = true;

                    var component =
                        new Component
                        {
                            MinX = x,
                            MinY = y,
                            MaxX = x,
                            MaxY = y,
                            PixelCount = 0,
                            TouchesBorder = false
                        };

                    while (queue.Count > 0)
                    {
                        Point point =
                            queue.Dequeue();

                        component.PixelCount++;

                        if (point.X <
                            component.MinX)
                        {
                            component.MinX =
                                point.X;
                        }

                        if (point.X >
                            component.MaxX)
                        {
                            component.MaxX =
                                point.X;
                        }

                        if (point.Y <
                            component.MinY)
                        {
                            component.MinY =
                                point.Y;
                        }

                        if (point.Y >
                            component.MaxY)
                        {
                            component.MaxY =
                                point.Y;
                        }

                        if (point.X <= 1 ||
                            point.Y <= 1 ||
                            point.X >= width - 2 ||
                            point.Y >= height - 2)
                        {
                            component.TouchesBorder =
                                true;
                        }

                        for (int index = 0;
                             index < 8;
                             index++)
                        {
                            int nextX =
                                point.X +
                                differenceX[index];

                            int nextY =
                                point.Y +
                                differenceY[index];

                            if (nextX < 0 ||
                                nextY < 0 ||
                                nextX >= width ||
                                nextY >= height)
                            {
                                continue;
                            }

                            if (!mask[nextX, nextY] ||
                                visited[nextX, nextY])
                            {
                                continue;
                            }

                            visited[nextX, nextY] =
                                true;

                            queue.Enqueue(
                                new Point(
                                    nextX,
                                    nextY
                                )
                            );
                        }
                    }

                    if (component.PixelCount >= 14)
                    {
                        components.Add(
                            component
                        );
                    }
                }
            }

            return components;
        }

        private static Rectangle SelectJewelleryBounds(
            List<Component> components,
            int imageWidth,
            int imageHeight)
        {
            if (components == null ||
                components.Count == 0)
            {
                return Rectangle.Empty;
            }

            Component best = null;

            double bestScore =
                double.MinValue;

            double imageCenterX =
                imageWidth / 2.0;

            double imageCenterY =
                imageHeight / 2.0;

            for (int index = 0;
                 index < components.Count;
                 index++)
            {
                Component component =
                    components[index];

                if (component.Width < 4 ||
                    component.Height < 4)
                {
                    continue;
                }

                double boxArea =
                    component.Width *
                    (double)component.Height;

                double imageArea =
                    imageWidth *
                    (double)imageHeight;

                double areaRatio =
                    boxArea /
                    Math.Max(
                        1.0,
                        imageArea
                    );

                double fillRatio =
                    component.PixelCount /
                    Math.Max(
                        1.0,
                        boxArea
                    );

                double aspectRatio =
                    component.Width /
                    (double)Math.Max(
                        1,
                        component.Height
                    );

                if (areaRatio < 0.00012 ||
                    areaRatio > 0.62)
                {
                    continue;
                }

                if (aspectRatio < 0.06 ||
                    aspectRatio > 9.0)
                {
                    continue;
                }

                if (fillRatio > 0.94 &&
                    areaRatio > 0.025)
                {
                    continue;
                }

                double centerX =
                    component.MinX +
                    component.Width / 2.0;

                double centerY =
                    component.MinY +
                    component.Height / 2.0;

                double centerDistance =
                    Math.Abs(
                        centerX -
                        imageCenterX
                    ) /
                    Math.Max(
                        1.0,
                        imageWidth
                    ) +
                    Math.Abs(
                        centerY -
                        imageCenterY
                    ) /
                    Math.Max(
                        1.0,
                        imageHeight
                    );

                double dimensionScore =
                    Math.Sqrt(
                        boxArea
                    ) * 2.1;

                double pixelScore =
                    Math.Sqrt(
                        component.PixelCount
                    ) * 4.8;

                double fillPreference =
                    1.0 -
                    Math.Abs(
                        fillRatio -
                        0.34
                    );

                double score =
                    dimensionScore +
                    pixelScore +
                    fillPreference * 55.0 -
                    centerDistance * 68.0;

                if (component.TouchesBorder)
                    score -= 90.0;

                if (fillRatio < 0.012)
                    score -= 55.0;

                if (component.Width >
                    imageWidth * 0.88)
                {
                    score -= 60.0;
                }

                if (component.Height >
                    imageHeight * 0.88)
                {
                    score -= 60.0;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = component;
                }
            }

            if (best == null)
                return Rectangle.Empty;

            Rectangle combined =
                best.Bounds;

            bool changed = true;
            int passes = 0;

            while (changed &&
                   passes < 3)
            {
                changed = false;
                passes++;

                for (int index = 0;
                     index < components.Count;
                     index++)
                {
                    Component component =
                        components[index];

                    Rectangle candidate =
                        component.Bounds;

                    if (combined.Contains(
                            candidate
                        ))
                    {
                        continue;
                    }

                    int horizontalGap =
                        AxisGap(
                            combined.Left,
                            combined.Right,
                            candidate.Left,
                            candidate.Right
                        );

                    int verticalGap =
                        AxisGap(
                            combined.Top,
                            combined.Bottom,
                            candidate.Top,
                            candidate.Bottom
                        );

                    int horizontalOverlap =
                        Overlap(
                            combined.Left,
                            combined.Right,
                            candidate.Left,
                            candidate.Right
                        );

                    int verticalOverlap =
                        Overlap(
                            combined.Top,
                            combined.Bottom,
                            candidate.Top,
                            candidate.Bottom
                        );

                    bool verticalAttachment =
                        horizontalOverlap >=
                        Math.Min(
                            combined.Width,
                            candidate.Width
                        ) / 5 &&
                        verticalGap <=
                        Math.Max(
                            18,
                            combined.Height / 3
                        );

                    bool sideAttachment =
                        verticalOverlap >=
                        Math.Min(
                            combined.Height,
                            candidate.Height
                        ) / 4 &&
                        horizontalGap <=
                        Math.Max(
                            18,
                            combined.Width / 3
                        );

                    double candidateAreaRatio =
                        candidate.Width *
                        (double)candidate.Height /
                        Math.Max(
                            1.0,
                            imageWidth *
                            (double)imageHeight
                        );

                    if ((verticalAttachment ||
                         sideAttachment) &&
                        candidateAreaRatio < 0.18)
                    {
                        combined =
                            Rectangle.Union(
                                combined,
                                candidate
                            );

                        changed = true;
                    }
                }
            }

            return combined;
        }

        private static bool[,] KeepComponentsNearBounds(
            bool[,] originalMask,
            List<Component> components,
            Rectangle selectedBounds,
            int width,
            int height)
        {
            bool[,] result =
                new bool[width, height];

            Rectangle expanded =
                InflateRectangle(
                    selectedBounds,
                    Math.Max(
                        10,
                        selectedBounds.Width / 5
                    ),
                    Math.Max(
                        10,
                        selectedBounds.Height / 5
                    ),
                    width,
                    height
                );

            for (int y = expanded.Top;
                 y < expanded.Bottom;
                 y++)
            {
                for (int x = expanded.Left;
                     x < expanded.Right;
                     x++)
                {
                    result[x, y] =
                        originalMask[x, y];
                }
            }

            return result;
        }

        private static double ScoreMaskCandidate(
            bool[,] mask,
            Rectangle bounds,
            int imageWidth,
            int imageHeight)
        {
            int foregroundPixels = 0;

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int x = bounds.Left;
                     x < bounds.Right;
                     x++)
                {
                    if (mask[x, y])
                        foregroundPixels++;
                }
            }

            double boxArea =
                bounds.Width *
                (double)bounds.Height;

            double fillRatio =
                foregroundPixels /
                Math.Max(
                    1.0,
                    boxArea
                );

            double areaRatio =
                boxArea /
                Math.Max(
                    1.0,
                    imageWidth *
                    (double)imageHeight
                );

            double aspectRatio =
                bounds.Width /
                (double)Math.Max(
                    1,
                    bounds.Height
                );

            double centerX =
                bounds.Left +
                bounds.Width / 2.0;

            double centerY =
                bounds.Top +
                bounds.Height / 2.0;

            double centerDistance =
                Math.Abs(
                    centerX -
                    imageWidth / 2.0
                ) /
                Math.Max(
                    1.0,
                    imageWidth
                ) +
                Math.Abs(
                    centerY -
                    imageHeight / 2.0
                ) /
                Math.Max(
                    1.0,
                    imageHeight
                );

            double fillPreference =
                1.0 -
                Math.Min(
                    1.0,
                    Math.Abs(
                        fillRatio -
                        0.34
                    ) / 0.34
                );

            double score =
                Math.Sqrt(
                    Math.Max(
                        1,
                        foregroundPixels
                    )
                ) * 3.2 +
                Math.Sqrt(
                    Math.Max(
                        1.0,
                        boxArea
                    )
                ) * 1.4 +
                fillPreference * 90.0 -
                centerDistance * 42.0;

            if (areaRatio < 0.002)
                score -= 70.0;

            if (areaRatio > 0.58)
                score -= 90.0;

            if (fillRatio < 0.015)
                score -= 70.0;

            if (fillRatio > 0.92)
                score -= 85.0;

            if (aspectRatio < 0.05 ||
                aspectRatio > 10.0)
            {
                score -= 100.0;
            }

            return score;
        }

        private static Rectangle FindMaskBounds(
            bool[,] mask,
            int width,
            int height)
        {
            int minimumX = width;
            int minimumY = height;
            int maximumX = -1;
            int maximumY = -1;

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

                    if (x < minimumX)
                        minimumX = x;

                    if (y < minimumY)
                        minimumY = y;

                    if (x > maximumX)
                        maximumX = x;

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

        private static void AddSegment(
            List<ImageSegment> list,
            Bitmap source,
            string name,
            Rectangle bounds,
            double weight)
        {
            Rectangle sourceBounds =
                new Rectangle(
                    0,
                    0,
                    source.Width,
                    source.Height
                );

            bounds.Intersect(
                sourceBounds
            );

            if (bounds.Width < 12 ||
                bounds.Height < 12)
            {
                list.Add(
                    new ImageSegment
                    {
                        Name = name,
                        Bitmap =
                            CreateBlankBitmap(),
                        Bounds = bounds,
                        Weight = weight
                    }
                );

                return;
            }

            Bitmap crop = null;

            try
            {
                crop =
                    source.Clone(
                        bounds,
                        PixelFormat.Format24bppRgb
                    );

                Bitmap normalized =
                    NormalizeSilhouette(
                        crop
                    );

                list.Add(
                    new ImageSegment
                    {
                        Name = name,
                        Bitmap = normalized,
                        Bounds = bounds,
                        Weight = weight
                    }
                );
            }
            finally
            {
                if (crop != null)
                    crop.Dispose();
            }
        }

        private static Bitmap RenderMaskCrop(
            bool[,] mask,
            Rectangle bounds)
        {
            Bitmap bitmap =
                new Bitmap(
                    bounds.Width,
                    bounds.Height,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics =
                Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
            }

            for (int y = 0;
                 y < bounds.Height;
                 y++)
            {
                for (int x = 0;
                     x < bounds.Width;
                     x++)
                {
                    int sourceX =
                        bounds.Left + x;

                    int sourceY =
                        bounds.Top + y;

                    bool foreground =
                        sourceX >= 0 &&
                        sourceY >= 0 &&
                        sourceX <
                            mask.GetLength(0) &&
                        sourceY <
                            mask.GetLength(1) &&
                        mask[sourceX, sourceY];

                    bitmap.SetPixel(
                        x,
                        y,
                        foreground
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            return bitmap;
        }

        private static Bitmap NormalizeSilhouette(
            Bitmap source)
        {
            Rectangle bounds =
                FindDarkBounds(source);

            if (bounds ==
                Rectangle.Empty)
            {
                return CreateBlankBitmap();
            }

            Bitmap cropped = null;

            try
            {
                cropped =
                    source.Clone(
                        bounds,
                        PixelFormat.Format24bppRgb
                    );

                Bitmap normalized =
                    new Bitmap(
                        NormalizedSize,
                        NormalizedSize,
                        PixelFormat.Format24bppRgb
                    );

                using (Graphics graphics =
                    Graphics.FromImage(normalized))
                {
                    graphics.Clear(Color.White);

                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;

                    graphics.SmoothingMode =
                        SmoothingMode.HighQuality;

                    graphics.PixelOffsetMode =
                        PixelOffsetMode.HighQuality;

                    int margin = 14;

                    int available =
                        NormalizedSize -
                        margin * 2;

                    double scale =
                        Math.Min(
                            available /
                            (double)Math.Max(
                                1,
                                cropped.Width
                            ),
                            available /
                            (double)Math.Max(
                                1,
                                cropped.Height
                            )
                        );

                    int drawWidth =
                        Math.Max(
                            1,
                            (int)Math.Round(
                                cropped.Width *
                                scale
                            )
                        );

                    int drawHeight =
                        Math.Max(
                            1,
                            (int)Math.Round(
                                cropped.Height *
                                scale
                            )
                        );

                    int drawX =
                        (
                            NormalizedSize -
                            drawWidth
                        ) / 2;

                    int drawY =
                        (
                            NormalizedSize -
                            drawHeight
                        ) / 2;

                    graphics.DrawImage(
                        cropped,
                        new Rectangle(
                            drawX,
                            drawY,
                            drawWidth,
                            drawHeight
                        ),
                        0,
                        0,
                        cropped.Width,
                        cropped.Height,
                        GraphicsUnit.Pixel
                    );
                }

                MakeStrictBlackWhite(
                    normalized
                );

                return normalized;
            }
            finally
            {
                if (cropped != null)
                    cropped.Dispose();
            }
        }

        private static void MakeStrictBlackWhite(
            Bitmap bitmap)
        {
            int[,] gray =
                ReadGrayValues(bitmap);

            int threshold =
                CalculateOtsuThreshold(
                    gray,
                    bitmap.Width,
                    bitmap.Height
                );

            threshold =
                ClampInt(
                    threshold + 22,
                    95,
                    222
                );

            for (int y = 0;
                 y < bitmap.Height;
                 y++)
            {
                for (int x = 0;
                     x < bitmap.Width;
                     x++)
                {
                    bitmap.SetPixel(
                        x,
                        y,
                        gray[x, y] <
                            threshold
                            ? Color.Black
                            : Color.White
                    );
                }
            }
        }

        private static Rectangle FindDarkBounds(
            Bitmap bitmap)
        {
            int minimumX =
                bitmap.Width;

            int minimumY =
                bitmap.Height;

            int maximumX = -1;
            int maximumY = -1;

            for (int y = 0;
                 y < bitmap.Height;
                 y++)
            {
                for (int x = 0;
                     x < bitmap.Width;
                     x++)
                {
                    if (Gray(
                            bitmap.GetPixel(
                                x,
                                y
                            )
                        ) >= 225)
                    {
                        continue;
                    }

                    if (x < minimumX)
                        minimumX = x;

                    if (y < minimumY)
                        minimumY = y;

                    if (x > maximumX)
                        maximumX = x;

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

        private static Bitmap CreateFallbackSilhouette(
            Bitmap source)
        {
            Bitmap fallback =
                new Bitmap(
                    source.Width,
                    source.Height,
                    PixelFormat.Format24bppRgb
                );

            int[,] gray =
                ReadGrayValues(source);

            int threshold =
                CalculateOtsuThreshold(
                    gray,
                    source.Width,
                    source.Height
                );

            threshold =
                ClampInt(
                    threshold,
                    75,
                    190
                );

            for (int y = 0;
                 y < source.Height;
                 y++)
            {
                for (int x = 0;
                     x < source.Width;
                     x++)
                {
                    fallback.SetPixel(
                        x,
                        y,
                        gray[x, y] <
                            threshold
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            Bitmap normalized =
                NormalizeSilhouette(
                    fallback
                );

            fallback.Dispose();

            return normalized;
        }

        private static Bitmap ResizeKeep(
            Bitmap source,
            int maxWidth,
            int maxHeight)
        {
            double scale =
                Math.Min(
                    maxWidth /
                    (double)Math.Max(
                        1,
                        source.Width
                    ),
                    maxHeight /
                    (double)Math.Max(
                        1,
                        source.Height
                    )
                );

            if (scale > 1.0)
                scale = 1.0;

            int width =
                Math.Max(
                    1,
                    (int)Math.Round(
                        source.Width *
                        scale
                    )
                );

            int height =
                Math.Max(
                    1,
                    (int)Math.Round(
                        source.Height *
                        scale
                    )
                );

            Bitmap result =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics =
                Graphics.FromImage(result))
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
                    new Rectangle(
                        0,
                        0,
                        width,
                        height
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

        private static Color EstimateBackground(
            Bitmap bitmap)
        {
            var samples =
                new List<Color>();

            int stepX =
                Math.Max(
                    1,
                    bitmap.Width / 32
                );

            int stepY =
                Math.Max(
                    1,
                    bitmap.Height / 32
                );

            for (int x = 0;
                 x < bitmap.Width;
                 x += stepX)
            {
                samples.Add(
                    bitmap.GetPixel(
                        x,
                        0
                    )
                );

                samples.Add(
                    bitmap.GetPixel(
                        x,
                        bitmap.Height - 1
                    )
                );
            }

            for (int y = 0;
                 y < bitmap.Height;
                 y += stepY)
            {
                samples.Add(
                    bitmap.GetPixel(
                        0,
                        y
                    )
                );

                samples.Add(
                    bitmap.GetPixel(
                        bitmap.Width - 1,
                        y
                    )
                );
            }

            if (samples.Count == 0)
                return Color.White;

            int[] redValues =
                new int[samples.Count];

            int[] greenValues =
                new int[samples.Count];

            int[] blueValues =
                new int[samples.Count];

            for (int index = 0;
                 index < samples.Count;
                 index++)
            {
                redValues[index] =
                    samples[index].R;

                greenValues[index] =
                    samples[index].G;

                blueValues[index] =
                    samples[index].B;
            }

            Array.Sort(redValues);
            Array.Sort(greenValues);
            Array.Sort(blueValues);

            int middle =
                samples.Count / 2;

            return Color.FromArgb(
                redValues[middle],
                greenValues[middle],
                blueValues[middle]
            );
        }

        private static bool IsLikelySkin(
            Color color)
        {
            int maximum =
                Math.Max(
                    color.R,
                    Math.Max(
                        color.G,
                        color.B
                    )
                );

            int minimum =
                Math.Min(
                    color.R,
                    Math.Min(
                        color.G,
                        color.B
                    )
                );

            bool skinOne =
                color.R > 92 &&
                color.G > 38 &&
                color.B > 20 &&
                maximum - minimum > 15 &&
                Math.Abs(
                    color.R -
                    color.G
                ) > 15 &&
                color.R > color.G &&
                color.R > color.B;

            bool skinTwo =
                color.R > 145 &&
                color.G > 75 &&
                color.B > 45 &&
                color.R >
                    color.B + 28;

            return skinOne ||
                   skinTwo;
        }

        private static bool IsNearWhite(
            Color color)
        {
            int maximum =
                Math.Max(
                    color.R,
                    Math.Max(
                        color.G,
                        color.B
                    )
                );

            int minimum =
                Math.Min(
                    color.R,
                    Math.Min(
                        color.G,
                        color.B
                    )
                );

            return Gray(color) > 242 &&
                   maximum - minimum < 22;
        }

        private static bool IsLikelyBlueRuler(
            Color color)
        {
            return color.B > 112 &&
                   color.B >
                       color.R + 18 &&
                   color.B >
                       color.G + 8;
        }

        private static Rectangle AddPadding(
            Rectangle bounds,
            int imageWidth,
            int imageHeight)
        {
            int paddingX =
                Math.Max(
                    8,
                    bounds.Width / 9
                );

            int paddingY =
                Math.Max(
                    8,
                    bounds.Height / 9
                );

            return InflateRectangle(
                bounds,
                paddingX,
                paddingY,
                imageWidth,
                imageHeight
            );
        }

        private static Rectangle InflateRectangle(
            Rectangle bounds,
            int horizontal,
            int vertical,
            int imageWidth,
            int imageHeight)
        {
            int left =
                Math.Max(
                    0,
                    bounds.Left -
                    horizontal
                );

            int top =
                Math.Max(
                    0,
                    bounds.Top -
                    vertical
                );

            int right =
                Math.Min(
                    imageWidth,
                    bounds.Right +
                    horizontal
                );

            int bottom =
                Math.Min(
                    imageHeight,
                    bounds.Bottom +
                    vertical
                );

            return Rectangle.FromLTRB(
                left,
                top,
                right,
                bottom
            );
        }

        private static int AxisGap(
            int firstStart,
            int firstEnd,
            int secondStart,
            int secondEnd)
        {
            if (firstEnd <
                secondStart)
            {
                return secondStart -
                       firstEnd;
            }

            if (secondEnd <
                firstStart)
            {
                return firstStart -
                       secondEnd;
            }

            return 0;
        }

        private static int Overlap(
            int firstStart,
            int firstEnd,
            int secondStart,
            int secondEnd)
        {
            return Math.Max(
                0,
                Math.Min(
                    firstEnd,
                    secondEnd
                ) -
                Math.Max(
                    firstStart,
                    secondStart
                )
            );
        }

        private static Bitmap CreateBlankBitmap()
        {
            Bitmap blank =
                new Bitmap(
                    NormalizedSize,
                    NormalizedSize,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics =
                Graphics.FromImage(blank))
            {
                graphics.Clear(Color.White);
            }

            return blank;
        }

        private static int Gray(
            Color color)
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

        private static void DisposeSegments(
            List<ImageSegment> segments)
        {
            if (segments == null)
                return;

            for (int index = 0;
                 index < segments.Count;
                 index++)
            {
                try
                {
                    ImageSegment segment =
                        segments[index];

                    if (segment != null &&
                        segment.Bitmap != null)
                    {
                        segment.Bitmap.Dispose();
                    }
                }
                catch
                {
                }
            }
        }
    }
}
