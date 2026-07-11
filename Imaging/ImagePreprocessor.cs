using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImagePreprocessResult : IDisposable
    {
        public Bitmap CroppedOriginal { get; set; }
        public Bitmap Silhouette { get; set; }
        public Bitmap LineArt { get; set; }

        public Rectangle SourceBounds { get; set; }
        public double Confidence { get; set; }
        public bool UsedFallback { get; set; }
        public string Method { get; set; }

        public void Dispose()
        {
            if (CroppedOriginal != null)
            {
                CroppedOriginal.Dispose();
                CroppedOriginal = null;
            }

            if (Silhouette != null)
            {
                Silhouette.Dispose();
                Silhouette = null;
            }

            if (LineArt != null)
            {
                LineArt.Dispose();
                LineArt = null;
            }
        }
    }

    public static class ImagePreprocessor
    {
        private const int MaximumWorkingSize = 900;
        private const int OutputSize = 384;

        private sealed class Component
        {
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;

            public int PixelCount;
            public int EdgePixels;
            public int SkinPixels;
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
                    return Rectangle.FromLTRB(
                        MinX,
                        MinY,
                        MaxX + 1,
                        MaxY + 1
                    );
                }
            }
        }

        private sealed class Candidate
        {
            public bool[,] Mask;
            public Rectangle Bounds;
            public double Score;
            public string Method;
        }

        public static Bitmap ExtractDesign(Bitmap source)
        {
            ImagePreprocessResult result = null;

            try
            {
                result = Process(source);

                if (result == null ||
                    result.LineArt == null)
                {
                    return CreateBlankOutput();
                }

                return new Bitmap(result.LineArt);
            }
            finally
            {
                if (result != null)
                    result.Dispose();
            }
        }

        public static ImagePreprocessResult Process(Bitmap source)
        {
            if (source == null ||
                source.Width <= 0 ||
                source.Height <= 0)
            {
                return CreateEmptyResult();
            }

            Bitmap working = ResizeKeep(
                source,
                MaximumWorkingSize,
                MaximumWorkingSize
            );

            try
            {
                int width = working.Width;
                int height = working.Height;

                int[,] gray = ReadGray(working);
                int[,] saturation = ReadSaturation(working);
                int[,] edgeStrength = BuildEdgeStrength(
                    gray,
                    width,
                    height
                );

                Color background = EstimateBorderBackground(
                    working
                );

                List<Candidate> candidates =
                    new List<Candidate>();

                AddCandidate(
                    candidates,
                    BuildBackgroundDifferenceMask(
                        working,
                        gray,
                        saturation,
                        background
                    ),
                    gray,
                    edgeStrength,
                    working,
                    "BACKGROUND-DIFFERENCE"
                );

                AddCandidate(
                    candidates,
                    BuildAdaptiveMask(
                        working,
                        gray,
                        saturation
                    ),
                    gray,
                    edgeStrength,
                    working,
                    "ADAPTIVE"
                );

                AddCandidate(
                    candidates,
                    BuildJewelleryColourMask(
                        working,
                        gray,
                        saturation,
                        background
                    ),
                    gray,
                    edgeStrength,
                    working,
                    "COLOUR-METAL"
                );

                AddCandidate(
                    candidates,
                    BuildEdgeObjectMask(
                        working,
                        gray,
                        edgeStrength
                    ),
                    gray,
                    edgeStrength,
                    working,
                    "EDGE-OBJECT"
                );

                Candidate best = SelectBestCandidate(
                    candidates
                );

                if (best == null ||
                    best.Mask == null ||
                    best.Bounds == Rectangle.Empty ||
                    best.Score < 5)
                {
                    return CreateFallbackResult(
                        working,
                        gray
                    );
                }

                Rectangle paddedBounds = AddPadding(
                    best.Bounds,
                    width,
                    height
                );

                bool[,] selectedMask = KeepOnlySelectedArea(
                    best.Mask,
                    paddedBounds,
                    width,
                    height
                );

                FillSmallHoles(
                    selectedMask,
                    width,
                    height
                );

                RemoveTinyComponents(
                    selectedMask,
                    width,
                    height,
                    Math.Max(
                        12,
                        paddedBounds.Width *
                        paddedBounds.Height /
                        2500
                    )
                );

                Rectangle finalBounds = FindMaskBounds(
                    selectedMask,
                    width,
                    height
                );

                if (finalBounds == Rectangle.Empty)
                {
                    return CreateFallbackResult(
                        working,
                        gray
                    );
                }

                finalBounds = AddPadding(
                    finalBounds,
                    width,
                    height
                );

                Bitmap croppedOriginal = CropBitmap(
                    working,
                    finalBounds
                );

                Bitmap silhouetteCrop = RenderMaskCrop(
                    selectedMask,
                    finalBounds
                );

                Bitmap normalizedSilhouette =
                    NormalizeBlackWhite(
                        silhouetteCrop,
                        OutputSize,
                        false
                    );

                Bitmap lineArt =
                    CreateLineArt(
                        normalizedSilhouette
                    );

                silhouetteCrop.Dispose();

                double confidence = CalculateConfidence(
                    selectedMask,
                    finalBounds,
                    width,
                    height,
                    best.Score
                );

                return new ImagePreprocessResult
                {
                    CroppedOriginal = croppedOriginal,
                    Silhouette = normalizedSilhouette,
                    LineArt = lineArt,
                    SourceBounds = ScaleBoundsToSource(
                        finalBounds,
                        working.Width,
                        working.Height,
                        source.Width,
                        source.Height
                    ),
                    Confidence = confidence,
                    UsedFallback = false,
                    Method = best.Method
                };
            }
            catch
            {
                int[,] gray = ReadGray(working);

                return CreateFallbackResult(
                    working,
                    gray
                );
            }
            finally
            {
                working.Dispose();
            }
        }

        private static void AddCandidate(
            List<Candidate> candidates,
            bool[,] mask,
            int[,] gray,
            int[,] edgeStrength,
            Bitmap bitmap,
            string method)
        {
            if (mask == null)
                return;

            int width = bitmap.Width;
            int height = bitmap.Height;

            MorphologicalClean(
                mask,
                width,
                height
            );

            List<Component> components =
                FindComponents(
                    mask,
                    edgeStrength,
                    bitmap,
                    width,
                    height
                );

            Component best = SelectBestComponent(
                components,
                width,
                height
            );

            if (best == null)
                return;

            Rectangle combinedBounds =
                MergeNearbyComponents(
                    best,
                    components,
                    width,
                    height
                );

            bool[,] combinedMask =
                BuildCombinedMask(
                    mask,
                    components,
                    combinedBounds,
                    width,
                    height
                );

            Rectangle finalBounds =
                FindMaskBounds(
                    combinedMask,
                    width,
                    height
                );

            if (finalBounds == Rectangle.Empty)
                return;

            double score =
                ScoreCandidate(
                    combinedMask,
                    finalBounds,
                    gray,
                    edgeStrength,
                    bitmap,
                    width,
                    height
                );

            candidates.Add(
                new Candidate
                {
                    Mask = combinedMask,
                    Bounds = finalBounds,
                    Score = score,
                    Method = method
                }
            );
        }

        private static Candidate SelectBestCandidate(
            List<Candidate> candidates)
        {
            if (candidates == null ||
                candidates.Count == 0)
            {
                return null;
            }

            Candidate best = null;
            double bestScore = double.MinValue;

            for (int index = 0;
                 index < candidates.Count;
                 index++)
            {
                Candidate candidate =
                    candidates[index];

                if (candidate == null ||
                    candidate.Bounds == Rectangle.Empty)
                {
                    continue;
                }

                if (candidate.Score > bestScore)
                {
                    bestScore = candidate.Score;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool[,] BuildBackgroundDifferenceMask(
            Bitmap bitmap,
            int[,] gray,
            int[,] saturation,
            Color background)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] mask =
                new bool[width, height];

            int backgroundGray =
                Gray(background);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
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
                            gray[x, y] -
                            backgroundGray
                        );

                    bool meaningfulDifference =
                        colorDifference >= 82 ||
                        grayDifference >= 31 ||
                        saturation[x, y] >= 48;

                    bool excluded =
                        IsStrongSkin(color) ||
                        IsNearWhite(color);

                    mask[x, y] =
                        meaningfulDifference &&
                        !excluded;
                }
            }

            return mask;
        }

        private static bool[,] BuildAdaptiveMask(
            Bitmap bitmap,
            int[,] gray,
            int[,] saturation)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] mask =
                new bool[width, height];

            long[,] integral =
                BuildIntegralImage(
                    gray,
                    width,
                    height
                );

            int radius = Math.Max(
                8,
                Math.Min(
                    width,
                    height
                ) / 28
            );

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int left = Math.Max(
                        0,
                        x - radius
                    );

                    int top = Math.Max(
                        0,
                        y - radius
                    );

                    int right = Math.Min(
                        width - 1,
                        x + radius
                    );

                    int bottom = Math.Min(
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

                    double localDifference =
                        localAverage -
                        gray[x, y];

                    Color color =
                        bitmap.GetPixel(x, y);

                    bool localObject =
                        localDifference >= 13 ||
                        saturation[x, y] >= 55;

                    bool usableBrightness =
                        gray[x, y] > 20 &&
                        gray[x, y] < 238;

                    mask[x, y] =
                        localObject &&
                        usableBrightness &&
                        !IsStrongSkin(color);
                }
            }

            return mask;
        }

        private static bool[,] BuildJewelleryColourMask(
            Bitmap bitmap,
            int[,] gray,
            int[,] saturation,
            Color background)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] mask =
                new bool[width, height];

            int backgroundGray =
                Gray(background);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
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

                    int spread =
                        maximum -
                        minimum;

                    bool goldLike =
                        color.R > 92 &&
                        color.G > 52 &&
                        color.R >
                            color.B + 18 &&
                        color.G >
                            color.B + 3 &&
                        spread > 20;

                    bool silverLike =
                        spread < 34 &&
                        gray[x, y] > 68 &&
                        gray[x, y] < 222 &&
                        Math.Abs(
                            gray[x, y] -
                            backgroundGray
                        ) > 19;

                    bool colouredStone =
                        saturation[x, y] > 58 &&
                        gray[x, y] > 28 &&
                        gray[x, y] < 238;

                    bool darkJewelleryDetail =
                        gray[x, y] < 125 &&
                        Math.Abs(
                            gray[x, y] -
                            backgroundGray
                        ) > 20;

                    bool excluded =
                        IsStrongSkin(color) ||
                        IsNearWhite(color) ||
                        IsStrongBlueObject(color);

                    mask[x, y] =
                        !excluded &&
                        (
                            goldLike ||
                            silverLike ||
                            colouredStone ||
                            darkJewelleryDetail
                        );
                }
            }

            return mask;
        }

        private static bool[,] BuildEdgeObjectMask(
            Bitmap bitmap,
            int[,] gray,
            int[,] edgeStrength)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            bool[,] initial =
                new bool[width, height];

            for (int y = 1;
                 y < height - 1;
                 y++)
            {
                for (int x = 1;
                     x < width - 1;
                     x++)
                {
                    Color color =
                        bitmap.GetPixel(x, y);

                    initial[x, y] =
                        edgeStrength[x, y] >= 58 &&
                        gray[x, y] < 240 &&
                        !IsStrongSkin(color);
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
                    int edgeCount = 0;

                    for (int yy = y - 2;
                         yy <= y + 2;
                         yy++)
                    {
                        for (int xx = x - 2;
                             xx <= x + 2;
                             xx++)
                        {
                            if (initial[xx, yy])
                                edgeCount++;
                        }
                    }

                    expanded[x, y] =
                        edgeCount >= 3;
                }
            }

            return expanded;
        }

        private static void MorphologicalClean(
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
                            nearby >= 10;
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

        private static List<Component> FindComponents(
            bool[,] mask,
            int[,] edgeStrength,
            Bitmap bitmap,
            int width,
            int height)
        {
            List<Component> result =
                new List<Component>();

            bool[,] visited =
                new bool[width, height];

            int[] directionX =
            {
                -1, 0, 1,
                -1, 1,
                -1, 0, 1
            };

            int[] directionY =
            {
                -1, -1, -1,
                0, 0,
                1, 1, 1
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y] ||
                        visited[x, y])
                    {
                        continue;
                    }

                    Queue<Point> queue =
                        new Queue<Point>();

                    queue.Enqueue(
                        new Point(x, y)
                    );

                    visited[x, y] = true;

                    Component component =
                        new Component
                        {
                            MinX = x,
                            MinY = y,
                            MaxX = x,
                            MaxY = y,
                            PixelCount = 0,
                            EdgePixels = 0,
                            SkinPixels = 0,
                            TouchesBorder = false
                        };

                    while (queue.Count > 0)
                    {
                        Point point =
                            queue.Dequeue();

                        component.PixelCount++;

                        if (edgeStrength[
                                point.X,
                                point.Y
                            ] >= 48)
                        {
                            component.EdgePixels++;
                        }

                        if (IsLikelySkin(
                                bitmap.GetPixel(
                                    point.X,
                                    point.Y
                                )
                            ))
                        {
                            component.SkinPixels++;
                        }

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

                        for (int direction = 0;
                             direction < 8;
                             direction++)
                        {
                            int nextX =
                                point.X +
                                directionX[direction];

                            int nextY =
                                point.Y +
                                directionY[direction];

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

                    if (component.PixelCount >= 18)
                    {
                        result.Add(component);
                    }
                }
            }

            return result;
        }

        private static Component SelectBestComponent(
            List<Component> components,
            int imageWidth,
            int imageHeight)
        {
            if (components == null ||
                components.Count == 0)
            {
                return null;
            }

            Component best = null;
            double bestScore = double.MinValue;

            double imageArea =
                imageWidth *
                (double)imageHeight;

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

                double areaRatio =
                    boxArea /
                    Math.Max(1.0, imageArea);

                double fillRatio =
                    component.PixelCount /
                    Math.Max(1.0, boxArea);

                double edgeRatio =
                    component.EdgePixels /
                    Math.Max(
                        1.0,
                        component.PixelCount
                    );

                double skinRatio =
                    component.SkinPixels /
                    Math.Max(
                        1.0,
                        component.PixelCount
                    );

                double aspect =
                    component.Width /
                    (double)Math.Max(
                        1,
                        component.Height
                    );

                if (areaRatio < 0.00008 ||
                    areaRatio > 0.64)
                {
                    continue;
                }

                if (aspect < 0.045 ||
                    aspect > 11.0)
                {
                    continue;
                }

                if (fillRatio > 0.96 &&
                    areaRatio > 0.03)
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

                double usefulFill =
                    1.0 -
                    Math.Min(
                        1.0,
                        Math.Abs(
                            fillRatio -
                            0.31
                        ) / 0.31
                    );

                double score =
                    Math.Sqrt(
                        component.PixelCount
                    ) * 4.3 +
                    Math.Sqrt(
                        boxArea
                    ) * 1.45 +
                    edgeRatio * 95.0 +
                    usefulFill * 55.0 -
                    skinRatio * 190.0 -
                    centerDistance * 38.0;

                if (component.TouchesBorder)
                    score -= 85.0;

                if (fillRatio < 0.008)
                    score -= 70.0;

                if (edgeRatio < 0.015)
                    score -= 45.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = component;
                }
            }

            return best;
        }

        private static Rectangle MergeNearbyComponents(
            Component best,
            List<Component> components,
            int imageWidth,
            int imageHeight)
        {
            Rectangle combined =
                best.Bounds;

            bool changed = true;
            int passes = 0;

            while (changed &&
                   passes < 4)
            {
                changed = false;
                passes++;

                for (int index = 0;
                     index < components.Count;
                     index++)
                {
                    Component component =
                        components[index];

                    if (component == best)
                        continue;

                    Rectangle candidate =
                        component.Bounds;

                    if (combined.Contains(candidate))
                        continue;

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
                        AxisOverlap(
                            combined.Left,
                            combined.Right,
                            candidate.Left,
                            candidate.Right
                        );

                    int verticalOverlap =
                        AxisOverlap(
                            combined.Top,
                            combined.Bottom,
                            candidate.Top,
                            candidate.Bottom
                        );

                    bool verticalPart =
                        horizontalOverlap >=
                        Math.Max(
                            2,
                            Math.Min(
                                combined.Width,
                                candidate.Width
                            ) / 7
                        ) &&
                        verticalGap <=
                        Math.Max(
                            24,
                            combined.Height * 2 / 3
                        );

                    bool sidePart =
                        verticalOverlap >=
                        Math.Max(
                            2,
                            Math.Min(
                                combined.Height,
                                candidate.Height
                            ) / 6
                        ) &&
                        horizontalGap <=
                        Math.Max(
                            22,
                            combined.Width / 2
                        );

                    double candidateArea =
                        candidate.Width *
                        (double)candidate.Height;

                    double imageArea =
                        imageWidth *
                        (double)imageHeight;

                    bool reasonableSize =
                        candidateArea /
                        Math.Max(
                            1.0,
                            imageArea
                        ) < 0.17;

                    if ((verticalPart ||
                         sidePart) &&
                        reasonableSize)
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

        private static bool[,] BuildCombinedMask(
            bool[,] originalMask,
            List<Component> components,
            Rectangle selectedBounds,
            int width,
            int height)
        {
            bool[,] result =
                new bool[width, height];

            Rectangle expanded =
                InflateInside(
                    selectedBounds,
                    Math.Max(
                        10,
                        selectedBounds.Width / 6
                    ),
                    Math.Max(
                        10,
                        selectedBounds.Height / 6
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

        private static double ScoreCandidate(
            bool[,] mask,
            Rectangle bounds,
            int[,] gray,
            int[,] edgeStrength,
            Bitmap bitmap,
            int imageWidth,
            int imageHeight)
        {
            int foreground = 0;
            int edgePixels = 0;
            int skinPixels = 0;

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

                    if (edgeStrength[x, y] >= 48)
                        edgePixels++;

                    if (IsLikelySkin(
                            bitmap.GetPixel(x, y)
                        ))
                    {
                        skinPixels++;
                    }
                }
            }

            double boxArea =
                bounds.Width *
                (double)bounds.Height;

            double imageArea =
                imageWidth *
                (double)imageHeight;

            double fillRatio =
                foreground /
                Math.Max(1.0, boxArea);

            double areaRatio =
                boxArea /
                Math.Max(1.0, imageArea);

            double edgeRatio =
                edgePixels /
                Math.Max(
                    1.0,
                    foreground
                );

            double skinRatio =
                skinPixels /
                Math.Max(
                    1.0,
                    foreground
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

            double fillQuality =
                1.0 -
                Math.Min(
                    1.0,
                    Math.Abs(
                        fillRatio -
                        0.30
                    ) / 0.30
                );

            double score =
                Math.Sqrt(
                    Math.Max(
                        1,
                        foreground
                    )
                ) * 2.8 +
                Math.Sqrt(
                    Math.Max(
                        1.0,
                        boxArea
                    )
                ) * 1.25 +
                edgeRatio * 120.0 +
                fillQuality * 75.0 -
                skinRatio * 210.0 -
                centerDistance * 28.0;

            if (areaRatio < 0.001)
                score -= 80.0;

            if (areaRatio > 0.60)
                score -= 100.0;

            if (fillRatio < 0.008)
                score -= 90.0;

            if (fillRatio > 0.95)
                score -= 90.0;

            return score;
        }

        private static double CalculateConfidence(
            bool[,] mask,
            Rectangle bounds,
            int imageWidth,
            int imageHeight,
            double rawScore)
        {
            int foreground = 0;

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int x = bounds.Left;
                     x < bounds.Right;
                     x++)
                {
                    if (mask[x, y])
                        foreground++;
                }
            }

            double fillRatio =
                foreground /
                Math.Max(
                    1.0,
                    bounds.Width *
                    (double)bounds.Height
                );

            double areaRatio =
                bounds.Width *
                (double)bounds.Height /
                Math.Max(
                    1.0,
                    imageWidth *
                    (double)imageHeight
                );

            double confidence =
                0.35 +
                Math.Min(
                    0.30,
                    rawScore / 900.0
                );

            if (fillRatio >= 0.03 &&
                fillRatio <= 0.82)
            {
                confidence += 0.18;
            }

            if (areaRatio >= 0.004 &&
                areaRatio <= 0.48)
            {
                confidence += 0.12;
            }

            return Clamp01(confidence);
        }

        private static ImagePreprocessResult CreateFallbackResult(
            Bitmap working,
            int[,] gray)
        {
            int threshold =
                CalculateOtsuThreshold(
                    gray,
                    working.Width,
                    working.Height
                );

            bool[,] mask =
                new bool[
                    working.Width,
                    working.Height
                ];

            for (int y = 0;
                 y < working.Height;
                 y++)
            {
                for (int x = 0;
                     x < working.Width;
                     x++)
                {
                    mask[x, y] =
                        gray[x, y] <
                        ClampInt(
                            threshold,
                            65,
                            190
                        );
                }
            }

            MorphologicalClean(
                mask,
                working.Width,
                working.Height
            );

            Rectangle bounds =
                FindMaskBounds(
                    mask,
                    working.Width,
                    working.Height
                );

            if (bounds == Rectangle.Empty)
            {
                bounds = new Rectangle(
                    0,
                    0,
                    working.Width,
                    working.Height
                );
            }

            bounds = AddPadding(
                bounds,
                working.Width,
                working.Height
            );

            Bitmap originalCrop =
                CropBitmap(
                    working,
                    bounds
                );

            Bitmap maskCrop =
                RenderMaskCrop(
                    mask,
                    bounds
                );

            Bitmap silhouette =
                NormalizeBlackWhite(
                    maskCrop,
                    OutputSize,
                    false
                );

            Bitmap lineArt =
                CreateLineArt(
                    silhouette
                );

            maskCrop.Dispose();

            return new ImagePreprocessResult
            {
                CroppedOriginal = originalCrop,
                Silhouette = silhouette,
                LineArt = lineArt,
                SourceBounds = bounds,
                Confidence = 0.25,
                UsedFallback = true,
                Method = "FALLBACK"
            };
        }

        private static ImagePreprocessResult CreateEmptyResult()
        {
            return new ImagePreprocessResult
            {
                CroppedOriginal = CreateBlankOutput(),
                Silhouette = CreateBlankOutput(),
                LineArt = CreateBlankOutput(),
                SourceBounds = Rectangle.Empty,
                Confidence = 0,
                UsedFallback = true,
                Method = "EMPTY"
            };
        }

        private static Bitmap CreateLineArt(
            Bitmap silhouette)
        {
            int width = silhouette.Width;
            int height = silhouette.Height;

            bool[,] dark =
                new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    dark[x, y] =
                        Gray(
                            silhouette.GetPixel(
                                x,
                                y
                            )
                        ) < 128;
                }
            }

            Bitmap lineArt =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics =
                Graphics.FromImage(lineArt))
            {
                graphics.Clear(Color.White);
            }

            for (int y = 1;
                 y < height - 1;
                 y++)
            {
                for (int x = 1;
                     x < width - 1;
                     x++)
                {
                    if (!dark[x, y])
                        continue;

                    bool boundary =
                        !dark[x - 1, y] ||
                        !dark[x + 1, y] ||
                        !dark[x, y - 1] ||
                        !dark[x, y + 1] ||
                        !dark[x - 1, y - 1] ||
                        !dark[x + 1, y - 1] ||
                        !dark[x - 1, y + 1] ||
                        !dark[x + 1, y + 1];

                    if (boundary)
                    {
                        lineArt.SetPixel(
                            x,
                            y,
                            Color.Black
                        );

                        if (x + 1 < width)
                        {
                            lineArt.SetPixel(
                                x + 1,
                                y,
                                Color.Black
                            );
                        }

                        if (y + 1 < height)
                        {
                            lineArt.SetPixel(
                                x,
                                y + 1,
                                Color.Black
                            );
                        }
                    }
                }
            }

            return lineArt;
        }

        private static Bitmap NormalizeBlackWhite(
            Bitmap source,
            int outputSize,
            bool keepAspectOnly)
        {
            Rectangle darkBounds =
                FindDarkBounds(source);

            if (darkBounds == Rectangle.Empty)
                return CreateBlankOutput();

            Bitmap cropped =
                source.Clone(
                    darkBounds,
                    PixelFormat.Format24bppRgb
                );

            try
            {
                Bitmap output =
                    new Bitmap(
                        outputSize,
                        outputSize,
                        PixelFormat.Format24bppRgb
                    );

                using (Graphics graphics =
                    Graphics.FromImage(output))
                {
                    graphics.Clear(Color.White);

                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;

                    graphics.SmoothingMode =
                        SmoothingMode.HighQuality;

                    graphics.PixelOffsetMode =
                        PixelOffsetMode.HighQuality;

                    int margin = 18;
                    int available =
                        outputSize -
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
                            outputSize -
                            drawWidth
                        ) / 2;

                    int drawY =
                        (
                            outputSize -
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

                MakeStrictBlackWhite(output);

                return output;
            }
            finally
            {
                cropped.Dispose();
            }
        }

        private static void MakeStrictBlackWhite(
            Bitmap bitmap)
        {
            int[,] gray =
                ReadGray(bitmap);

            int threshold =
                CalculateOtsuThreshold(
                    gray,
                    bitmap.Width,
                    bitmap.Height
                );

            threshold = ClampInt(
                threshold + 15,
                85,
                220
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

        private static void FillSmallHoles(
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

                        next[x, y] =
                            mask[x, y] ||
                            neighbours >= 6;
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

        private static void RemoveTinyComponents(
            bool[,] mask,
            int width,
            int height,
            int minimumPixels)
        {
            bool[,] visited =
                new bool[width, height];

            int[] directionX =
            {
                -1, 0, 1,
                -1, 1,
                -1, 0, 1
            };

            int[] directionY =
            {
                -1, -1, -1,
                0, 0,
                1, 1, 1
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y] ||
                        visited[x, y])
                    {
                        continue;
                    }

                    Queue<Point> queue =
                        new Queue<Point>();

                    List<Point> points =
                        new List<Point>();

                    queue.Enqueue(
                        new Point(x, y)
                    );

                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        Point point =
                            queue.Dequeue();

                        points.Add(point);

                        for (int direction = 0;
                             direction < 8;
                             direction++)
                        {
                            int nextX =
                                point.X +
                                directionX[direction];

                            int nextY =
                                point.Y +
                                directionY[direction];

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

                    if (points.Count <
                        minimumPixels)
                    {
                        for (int index = 0;
                             index < points.Count;
                             index++)
                        {
                            mask[
                                points[index].X,
                                points[index].Y
                            ] = false;
                        }
                    }
                }
            }
        }

        private static bool[,] KeepOnlySelectedArea(
            bool[,] source,
            Rectangle bounds,
            int width,
            int height)
        {
            bool[,] result =
                new bool[width, height];

            for (int y = bounds.Top;
                 y < bounds.Bottom;
                 y++)
            {
                for (int x = bounds.Left;
                     x < bounds.Right;
                     x++)
                {
                    result[x, y] =
                        source[x, y];
                }
            }

            return result;
        }

        private static Bitmap RenderMaskCrop(
            bool[,] mask,
            Rectangle bounds)
        {
            Bitmap output =
                new Bitmap(
                    Math.Max(1, bounds.Width),
                    Math.Max(1, bounds.Height),
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics =
                Graphics.FromImage(output))
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

                    output.SetPixel(
                        x,
                        y,
                        foreground
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            return output;
        }

        private static Bitmap CropBitmap(
            Bitmap source,
            Rectangle bounds)
        {
            Rectangle imageBounds =
                new Rectangle(
                    0,
                    0,
                    source.Width,
                    source.Height
                );

            bounds.Intersect(imageBounds);

            if (bounds.Width <= 0 ||
                bounds.Height <= 0)
            {
                return new Bitmap(source);
            }

            return source.Clone(
                bounds,
                PixelFormat.Format24bppRgb
            );
        }

        private static Rectangle ScaleBoundsToSource(
            Rectangle workingBounds,
            int workingWidth,
            int workingHeight,
            int sourceWidth,
            int sourceHeight)
        {
            double scaleX =
                sourceWidth /
                (double)Math.Max(
                    1,
                    workingWidth
                );

            double scaleY =
                sourceHeight /
                (double)Math.Max(
                    1,
                    workingHeight
                );

            int left =
                ClampInt(
                    (int)Math.Round(
                        workingBounds.Left *
                        scaleX
                    ),
                    0,
                    sourceWidth
                );

            int top =
                ClampInt(
                    (int)Math.Round(
                        workingBounds.Top *
                        scaleY
                    ),
                    0,
                    sourceHeight
                );

            int right =
                ClampInt(
                    (int)Math.Round(
                        workingBounds.Right *
                        scaleX
                    ),
                    left,
                    sourceWidth
                );

            int bottom =
                ClampInt(
                    (int)Math.Round(
                        workingBounds.Bottom *
                        scaleY
                    ),
                    top,
                    sourceHeight
                );

            return Rectangle.FromLTRB(
                left,
                top,
                right,
                bottom
            );
        }

        private static Rectangle AddPadding(
            Rectangle bounds,
            int width,
            int height)
        {
            int horizontal =
                Math.Max(
                    8,
                    bounds.Width / 9
                );

            int vertical =
                Math.Max(
                    8,
                    bounds.Height / 9
                );

            return InflateInside(
                bounds,
                horizontal,
                vertical,
                width,
                height
            );
        }

        private static Rectangle InflateInside(
            Rectangle bounds,
            int horizontal,
            int vertical,
            int width,
            int height)
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
                    width,
                    bounds.Right +
                    horizontal
                );

            int bottom =
                Math.Min(
                    height,
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

        private static Rectangle FindMaskBounds(
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

        private static Rectangle FindDarkBounds(
            Bitmap bitmap)
        {
            int minimumX = bitmap.Width;
            int minimumY = bitmap.Height;
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
                            bitmap.GetPixel(x, y)
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

        private static int[,] ReadGray(
            Bitmap bitmap)
        {
            int[,] result =
                new int[
                    bitmap.Width,
                    bitmap.Height
                ];

            for (int y = 0;
                 y < bitmap.Height;
                 y++)
            {
                for (int x = 0;
                     x < bitmap.Width;
                     x++)
                {
                    result[x, y] =
                        Gray(
                            bitmap.GetPixel(x, y)
                        );
                }
            }

            return result;
        }

        private static int[,] ReadSaturation(
            Bitmap bitmap)
        {
            int[,] result =
                new int[
                    bitmap.Width,
                    bitmap.Height
                ];

            for (int y = 0;
                 y < bitmap.Height;
                 y++)
            {
                for (int x = 0;
                     x < bitmap.Width;
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

                    result[x, y] =
                        maximum -
                        minimum;
                }
            }

            return result;
        }

        private static int[,] BuildEdgeStrength(
            int[,] gray,
            int width,
            int height)
        {
            int[,] result =
                new int[width, height];

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

                    result[x, y] =
                        horizontal +
                        vertical +
                        diagonalOne / 2 +
                        diagonalTwo / 2;
                }
            }

            return result;
        }

        private static Color EstimateBorderBackground(
            Bitmap bitmap)
        {
            List<Color> samples =
                new List<Color>();

            int stepX =
                Math.Max(
                    1,
                    bitmap.Width / 36
                );

            int stepY =
                Math.Max(
                    1,
                    bitmap.Height / 36
                );

            for (int x = 0;
                 x < bitmap.Width;
                 x += stepX)
            {
                samples.Add(
                    bitmap.GetPixel(x, 0)
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
                    bitmap.GetPixel(0, y)
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

            int[] reds =
                new int[samples.Count];

            int[] greens =
                new int[samples.Count];

            int[] blues =
                new int[samples.Count];

            for (int index = 0;
                 index < samples.Count;
                 index++)
            {
                reds[index] =
                    samples[index].R;

                greens[index] =
                    samples[index].G;

                blues[index] =
                    samples[index].B;
            }

            Array.Sort(reds);
            Array.Sort(greens);
            Array.Sort(blues);

            int middle =
                samples.Count / 2;

            return Color.FromArgb(
                reds[middle],
                greens[middle],
                blues[middle]
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

        private static int CalculateOtsuThreshold(
            int[,] gray,
            int width,
            int height)
        {
            int[] histogram =
                new int[256];

            int total =
                width * height;

            if (total <= 0)
                return 150;

            long totalIntensity = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
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

            double bestVariance = -1;
            int bestThreshold = 150;

            for (int threshold = 0;
                 threshold < 256;
                 threshold++)
            {
                backgroundCount +=
                    histogram[threshold];

                if (backgroundCount == 0)
                    continue;

                int foregroundCount =
                    total -
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

                if (variance > bestVariance)
                {
                    bestVariance = variance;
                    bestThreshold = threshold;
                }
            }

            return ClampInt(
                bestThreshold,
                55,
                220
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
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    destination[x, y] =
                        source[x, y];
                }
            }
        }

        private static int AxisGap(
            int firstStart,
            int firstEnd,
            int secondStart,
            int secondEnd)
        {
            if (firstEnd < secondStart)
            {
                return secondStart -
                       firstEnd;
            }

            if (secondEnd < firstStart)
            {
                return firstStart -
                       secondEnd;
            }

            return 0;
        }

        private static int AxisOverlap(
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

            return
                color.R > 78 &&
                color.G > 30 &&
                color.B > 15 &&
                color.R > color.G &&
                color.R > color.B &&
                maximum - minimum > 12 &&
                color.R - color.B > 18;
        }

        private static bool IsStrongSkin(
            Color color)
        {
            return
                color.R > 118 &&
                color.G > 56 &&
                color.B > 30 &&
                color.R > color.G + 12 &&
                color.R > color.B + 25;
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

            return Gray(color) > 244 &&
                   maximum - minimum < 20;
        }

        private static bool IsStrongBlueObject(
            Color color)
        {
            return color.B > 120 &&
                   color.B > color.R + 20 &&
                   color.B > color.G + 9;
        }

        private static Bitmap ResizeKeep(
            Bitmap source,
            int maximumWidth,
            int maximumHeight)
        {
            double scale =
                Math.Min(
                    maximumWidth /
                    (double)Math.Max(
                        1,
                        source.Width
                    ),
                    maximumHeight /
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

        private static Bitmap CreateBlankOutput()
        {
            Bitmap blank =
                new Bitmap(
                    OutputSize,
                    OutputSize,
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
    }
}
