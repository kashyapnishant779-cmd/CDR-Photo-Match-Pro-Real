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
                get { return MaxX - MinX + 1; }
            }

            public int Height
            {
                get { return MaxY - MinY + 1; }
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

        public static List<ImageSegment> Split(Bitmap source)
        {
            return Segment(source);
        }

        public static List<ImageSegment> Segment(Bitmap source)
        {
            var list = new List<ImageSegment>();

            if (source == null)
                return list;

            Bitmap clean = ExtractMainDesign(source);

            try
            {
                AddSegment(
                    list,
                    clean,
                    "FULL",
                    new Rectangle(0, 0, clean.Width, clean.Height),
                    1.00
                );

                int w = clean.Width;
                int h = clean.Height;

                // Jewellery ke main vertical parts.
                AddSegment(
                    list,
                    clean,
                    "TOP",
                    new Rectangle(0, 0, w, h * 40 / 100),
                    0.72
                );

                AddSegment(
                    list,
                    clean,
                    "MIDDLE",
                    new Rectangle(0, h * 25 / 100, w, h * 50 / 100),
                    0.92
                );

                AddSegment(
                    list,
                    clean,
                    "BOTTOM",
                    new Rectangle(0, h * 60 / 100, w, h * 40 / 100),
                    0.78
                );

                // Left/right symmetry aur side details.
                AddSegment(
                    list,
                    clean,
                    "LEFT",
                    new Rectangle(0, h / 10, w * 58 / 100, h * 80 / 100),
                    0.56
                );

                AddSegment(
                    list,
                    clean,
                    "RIGHT",
                    new Rectangle(w * 42 / 100, h / 10, w * 58 / 100, h * 80 / 100),
                    0.56
                );
            }
            finally
            {
                clean.Dispose();
            }

            return list;
        }

        private static void AddSegment(
            List<ImageSegment> list,
            Bitmap source,
            string name,
            Rectangle bounds,
            double weight)
        {
            Rectangle imageBounds =
                new Rectangle(0, 0, source.Width, source.Height);

            bounds.Intersect(imageBounds);

            if (bounds.Width < 20 || bounds.Height < 20)
                return;

            Bitmap crop =
                source.Clone(
                    bounds,
                    PixelFormat.Format24bppRgb
                );

            Bitmap normalized = NormalizeSilhouette(crop);
            crop.Dispose();

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

        public static Bitmap ExtractMainDesign(Bitmap source)
        {
            Bitmap working =
                ResizeKeep(
                    source,
                    WorkingSize,
                    WorkingSize
                );

            try
            {
                bool[,] mask = BuildForegroundMask(working);

                CleanMask(mask, working.Width, working.Height);

                List<Component> components =
                    FindComponents(
                        mask,
                        working.Width,
                        working.Height
                    );

                Rectangle jewelleryBounds =
                    SelectJewelleryBounds(
                        components,
                        working.Width,
                        working.Height
                    );

                if (jewelleryBounds == Rectangle.Empty)
                {
                    return CreateFallbackSilhouette(working);
                }

                jewelleryBounds =
                    AddPadding(
                        jewelleryBounds,
                        working.Width,
                        working.Height
                    );

                Bitmap result =
                    RenderMaskCrop(
                        mask,
                        jewelleryBounds
                    );

                Bitmap normalized =
                    NormalizeSilhouette(result);

                result.Dispose();

                return normalized;
            }
            finally
            {
                working.Dispose();
            }
        }

        private static bool[,] BuildForegroundMask(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            var mask = new bool[width, height];

            Color background =
                EstimateBackground(bitmap);

            int backgroundGray =
                Gray(background);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = bitmap.GetPixel(x, y);

                    int gray = Gray(c);
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int saturation = max - min;

                    int backgroundDifference =
                        Math.Abs(c.R - background.R) +
                        Math.Abs(c.G - background.G) +
                        Math.Abs(c.B - background.B);

                    bool skin =
                        c.R > 125 &&
                        c.G > 65 &&
                        c.B > 35 &&
                        c.R > c.G + 18 &&
                        c.G > c.B + 8;

                    bool strongSkin =
                        c.R > 150 &&
                        c.G > 85 &&
                        c.B > 55 &&
                        c.R > c.B + 35;

                    bool blueRuler =
                        c.B > 115 &&
                        c.B > c.R + 18 &&
                        c.B > c.G + 10;

                    bool nearWhite =
                        gray > 235 &&
                        saturation < 25;

                    bool gold =
                        c.R > 105 &&
                        c.G > 60 &&
                        c.R > c.B + 25 &&
                        c.G > c.B + 10 &&
                        saturation > 28;

                    bool colourfulStone =
                        saturation > 55 &&
                        gray > 45 &&
                        gray < 235 &&
                        !skin;

                    bool darkJewellery =
                        gray < 135 &&
                        saturation < 135;

                    bool differentFromBackground =
                        backgroundDifference > 95 &&
                        Math.Abs(gray - backgroundGray) > 22;

                    bool foreground =
                        gold ||
                        colourfulStone ||
                        darkJewellery ||
                        differentFromBackground;

                    if (skin || strongSkin || blueRuler || nearWhite)
                        foreground = false;

                    mask[x, y] = foreground;
                }
            }

            return mask;
        }

        private static Color EstimateBackground(Bitmap bitmap)
        {
            long red = 0;
            long green = 0;
            long blue = 0;
            int count = 0;

            int stepX = Math.Max(1, bitmap.Width / 30);
            int stepY = Math.Max(1, bitmap.Height / 30);

            // Border pixels se background estimate.
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                AddColor(bitmap.GetPixel(x, 0), ref red, ref green, ref blue, ref count);
                AddColor(bitmap.GetPixel(x, bitmap.Height - 1), ref red, ref green, ref blue, ref count);
            }

            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                AddColor(bitmap.GetPixel(0, y), ref red, ref green, ref blue, ref count);
                AddColor(bitmap.GetPixel(bitmap.Width - 1, y), ref red, ref green, ref blue, ref count);
            }

            if (count == 0)
                return Color.White;

            return Color.FromArgb(
                Clamp((int)(red / count)),
                Clamp((int)(green / count)),
                Clamp((int)(blue / count))
            );
        }

        private static void AddColor(
            Color color,
            ref long red,
            ref long green,
            ref long blue,
            ref int count)
        {
            red += color.R;
            green += color.G;
            blue += color.B;
            count++;
        }

        private static void CleanMask(
            bool[,] mask,
            int width,
            int height)
        {
            // Do passes: isolated noise hatao aur jewellery ke close pixels jodo.
            for (int pass = 0; pass < 2; pass++)
            {
                var copy = new bool[width, height];

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        int neighbours =
                            CountNeighbours(mask, x, y);

                        if (mask[x, y])
                        {
                            copy[x, y] = neighbours >= 2;
                        }
                        else
                        {
                            copy[x, y] = neighbours >= 6;
                        }
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                        mask[x, y] = copy[x, y];
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

        private static List<Component> FindComponents(
            bool[,] mask,
            int width,
            int height)
        {
            var result = new List<Component>();
            var visited = new bool[width, height];

            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y] || visited[x, y])
                        continue;

                    var queue = new Queue<Point>();
                    queue.Enqueue(new Point(x, y));
                    visited[x, y] = true;

                    var component = new Component
                    {
                        MinX = x,
                        MaxX = x,
                        MinY = y,
                        MaxY = y,
                        PixelCount = 0,
                        TouchesBorder = false
                    };

                    while (queue.Count > 0)
                    {
                        Point point = queue.Dequeue();

                        component.PixelCount++;

                        if (point.X < component.MinX) component.MinX = point.X;
                        if (point.X > component.MaxX) component.MaxX = point.X;
                        if (point.Y < component.MinY) component.MinY = point.Y;
                        if (point.Y > component.MaxY) component.MaxY = point.Y;

                        if (point.X <= 1 ||
                            point.Y <= 1 ||
                            point.X >= width - 2 ||
                            point.Y >= height - 2)
                        {
                            component.TouchesBorder = true;
                        }

                        for (int i = 0; i < 8; i++)
                        {
                            int nx = point.X + dx[i];
                            int ny = point.Y + dy[i];

                            if (nx < 0 || ny < 0 ||
                                nx >= width || ny >= height)
                            {
                                continue;
                            }

                            if (!mask[nx, ny] || visited[nx, ny])
                                continue;

                            visited[nx, ny] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }

                    if (component.PixelCount >= 18)
                        result.Add(component);
                }
            }

            return result;
        }

        private static Rectangle SelectJewelleryBounds(
            List<Component> components,
            int imageWidth,
            int imageHeight)
        {
            if (components == null || components.Count == 0)
                return Rectangle.Empty;

            Component best = null;
            double bestScore = double.MinValue;

            double imageCenterX = imageWidth / 2.0;
            double imageCenterY = imageHeight / 2.0;

            for (int i = 0; i < components.Count; i++)
            {
                Component component = components[i];

                int width = component.Width;
                int height = component.Height;

                if (width < 4 || height < 4)
                    continue;

                double aspect =
                    width / (double)Math.Max(1, height);

                double boxArea =
                    width * (double)height;

                double fill =
                    component.PixelCount /
                    Math.Max(1.0, boxArea);

                double areaRatio =
                    boxArea /
                    Math.Max(1.0, imageWidth * (double)imageHeight);

                if (aspect > 8.0 || aspect < 0.08)
                    continue;

                if (areaRatio > 0.70)
                    continue;

                if (fill > 0.91 && areaRatio > 0.05)
                    continue;

                double centerX =
                    component.MinX + width / 2.0;

                double centerY =
                    component.MinY + height / 2.0;

                double centerDistance =
                    Math.Abs(centerX - imageCenterX) / imageWidth +
                    Math.Abs(centerY - imageCenterY) / imageHeight;

                double score =
                    Math.Sqrt(component.PixelCount) * 5.0 +
                    Math.Sqrt(boxArea) * 1.5 -
                    centerDistance * 90.0;

                if (component.TouchesBorder)
                    score -= 65.0;

                if (fill < 0.025)
                    score -= 40.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = component;
                }
            }

            if (best == null)
                return Rectangle.Empty;

            Rectangle combined = best.Bounds;

            // Best jewellery component ke aas-paas ke top/drop parts bhi include karo.
            for (int i = 0; i < components.Count; i++)
            {
                Component component = components[i];

                if (component == best)
                    continue;

                Rectangle candidate = component.Bounds;

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

                bool aligned =
                    horizontalOverlap >=
                    Math.Min(combined.Width, candidate.Width) / 4;

                bool close =
                    horizontalGap <= Math.Max(12, combined.Width / 3) &&
                    verticalGap <= Math.Max(22, combined.Height / 2);

                if (aligned && close)
                    combined = Rectangle.Union(combined, candidate);
            }

            return combined;
        }

        private static int AxisGap(
            int a1,
            int a2,
            int b1,
            int b2)
        {
            if (a2 < b1)
                return b1 - a2;

            if (b2 < a1)
                return a1 - b2;

            return 0;
        }

        private static int Overlap(
            int a1,
            int a2,
            int b1,
            int b2)
        {
            return Math.Max(
                0,
                Math.Min(a2, b2) -
                Math.Max(a1, b1)
            );
        }

        private static Rectangle AddPadding(
            Rectangle bounds,
            int imageWidth,
            int imageHeight)
        {
            int paddingX =
                Math.Max(8, bounds.Width / 10);

            int paddingY =
                Math.Max(8, bounds.Height / 10);

            int left =
                Math.Max(0, bounds.Left - paddingX);

            int top =
                Math.Max(0, bounds.Top - paddingY);

            int right =
                Math.Min(imageWidth, bounds.Right + paddingX);

            int bottom =
                Math.Min(imageHeight, bounds.Bottom + paddingY);

            return Rectangle.FromLTRB(
                left,
                top,
                right,
                bottom
            );
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

            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.White);

            for (int y = 0; y < bounds.Height; y++)
            {
                for (int x = 0; x < bounds.Width; x++)
                {
                    int sourceX = bounds.Left + x;
                    int sourceY = bounds.Top + y;

                    bitmap.SetPixel(
                        x,
                        y,
                        mask[sourceX, sourceY]
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            return bitmap;
        }

        private static Bitmap NormalizeSilhouette(Bitmap source)
        {
            Rectangle bounds = FindDarkBounds(source);

            if (bounds == Rectangle.Empty)
            {
                Bitmap blank =
                    new Bitmap(
                        NormalizedSize,
                        NormalizedSize,
                        PixelFormat.Format24bppRgb
                    );

                using (Graphics graphics = Graphics.FromImage(blank))
                    graphics.Clear(Color.White);

                return blank;
            }

            Bitmap cropped =
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

            using (Graphics graphics = Graphics.FromImage(normalized))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int margin = 14;
                int available = NormalizedSize - margin * 2;

                double scale =
                    Math.Min(
                        available / (double)cropped.Width,
                        available / (double)cropped.Height
                    );

                int drawWidth =
                    Math.Max(1, (int)(cropped.Width * scale));

                int drawHeight =
                    Math.Max(1, (int)(cropped.Height * scale));

                int drawX =
                    (NormalizedSize - drawWidth) / 2;

                int drawY =
                    (NormalizedSize - drawHeight) / 2;

                graphics.DrawImage(
                    cropped,
                    drawX,
                    drawY,
                    drawWidth,
                    drawHeight
                );
            }

            cropped.Dispose();

            // Final strict black/white conversion.
            for (int y = 0; y < normalized.Height; y++)
            {
                for (int x = 0; x < normalized.Width; x++)
                {
                    Color c = normalized.GetPixel(x, y);
                    int gray = Gray(c);

                    normalized.SetPixel(
                        x,
                        y,
                        gray < 210
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            return normalized;
        }

        private static Rectangle FindDarkBounds(Bitmap bitmap)
        {
            int minX = bitmap.Width;
            int minY = bitmap.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (Gray(bitmap.GetPixel(x, y)) >= 220)
                        continue;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return Rectangle.Empty;

            return Rectangle.FromLTRB(
                minX,
                minY,
                maxX + 1,
                maxY + 1
            );
        }

        private static Bitmap CreateFallbackSilhouette(Bitmap source)
        {
            Bitmap fallback =
                new Bitmap(
                    source.Width,
                    source.Height,
                    PixelFormat.Format24bppRgb
                );

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    int gray =
                        Gray(source.GetPixel(x, y));

                    fallback.SetPixel(
                        x,
                        y,
                        gray < 150
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            Bitmap normalized =
                NormalizeSilhouette(fallback);

            fallback.Dispose();

            return normalized;
        }

        private static Bitmap ResizeKeep(
            Bitmap source,
            int maxWidth,
            int maxHeight)
        {
            double ratio =
                Math.Min(
                    maxWidth / (double)Math.Max(1, source.Width),
                    maxHeight / (double)Math.Max(1, source.Height)
                );

            if (ratio > 1.0)
                ratio = 1.0;

            int width =
                Math.Max(1, (int)(source.Width * ratio));

            int height =
                Math.Max(1, (int)(source.Height * ratio));

            Bitmap result =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                graphics.DrawImage(
                    source,
                    0,
                    0,
                    width,
                    height
                );
            }

            return result;
        }

        private static int Gray(Color color)
        {
            return (
                color.R * 30 +
                color.G * 59 +
                color.B * 11
            ) / 100;
        }

        private static int Clamp(int value)
        {
            if (value < 0)
                return 0;

            if (value > 255)
                return 255;

            return value;
        }
    }
}
