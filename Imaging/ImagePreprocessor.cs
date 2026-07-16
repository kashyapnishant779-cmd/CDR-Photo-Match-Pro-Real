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
        private const int OutputSize = 384;

        public static Bitmap ExtractDesign(Bitmap source)
        {
            ImagePreprocessResult result = null;

            try
            {
                result = Process(source);

                if (result == null || result.LineArt == null)
                    return CreateBlank();

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
                return CreateEmpty();
            }

            Bitmap cropped = null;
            Bitmap silhouette = null;
            Bitmap lineArt = null;

            try
            {
                Rectangle bounds = FindUsefulBounds(source);

                if (bounds == Rectangle.Empty)
                {
                    bounds = new Rectangle(
                        0,
                        0,
                        source.Width,
                        source.Height
                    );
                }

                bounds = AddPadding(
                    bounds,
                    source.Width,
                    source.Height
                );

                cropped = source.Clone(
                    bounds,
                    PixelFormat.Format24bppRgb
                );

                double confidence;
                silhouette = BuildSilhouette(
                    cropped,
                    out confidence
                );

                lineArt = BuildLineArt(silhouette);

                return new ImagePreprocessResult
                {
                    CroppedOriginal = cropped,
                    Silhouette = silhouette,
                    LineArt = lineArt,
                    SourceBounds = bounds,
                    Confidence = confidence,
                    UsedFallback = false,
                    Method = "ADAPTIVE-JEWELLERY-MASK"
                };
            }
            catch
            {
                if (cropped != null)
                    cropped.Dispose();

                if (silhouette != null)
                    silhouette.Dispose();

                if (lineArt != null)
                    lineArt.Dispose();

                return CreateFallback(source);
            }
        }

        private static Rectangle FindUsefulBounds(Bitmap source)
        {
            int width = source.Width;
            int height = source.Height;

            Color background = EstimateBackground(source);

            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            int step = Math.Max(
                1,
                Math.Min(width, height) / 500
            );

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    Color c = source.GetPixel(x, y);

                    int colorDifference =
                        Math.Abs(c.R - background.R) +
                        Math.Abs(c.G - background.G) +
                        Math.Abs(c.B - background.B);

                    int gray = Gray(c);
                    int saturation = Saturation(c);

                    bool useful =
                        colorDifference >= 70 ||
                        gray <= 170 ||
                        saturation >= 45;

                    if (!useful)
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

            Rectangle result = Rectangle.FromLTRB(
                minX,
                minY,
                Math.Min(width, maxX + step),
                Math.Min(height, maxY + step)
            );

            double areaRatio =
                result.Width * (double)result.Height /
                Math.Max(1.0, width * (double)height);

            // Manual crop already object-focused hota hai.
            // Agar detected bounds bahut chhota ya almost full frame ho,
            // to original crop ko hi preserve karo.
            if (areaRatio < 0.08 || areaRatio > 0.96)
            {
                return new Rectangle(
                    0,
                    0,
                    width,
                    height
                );
            }

            return result;
        }

        private static Color EstimateBackground(Bitmap source)
        {
            long r = 0;
            long g = 0;
            long b = 0;
            int count = 0;

            int width = source.Width;
            int height = source.Height;
            int stepX = Math.Max(1, width / 50);
            int stepY = Math.Max(1, height / 50);

            for (int x = 0; x < width; x += stepX)
            {
                Add(source.GetPixel(x, 0), ref r, ref g, ref b, ref count);
                Add(source.GetPixel(x, height - 1), ref r, ref g, ref b, ref count);
            }

            for (int y = 0; y < height; y += stepY)
            {
                Add(source.GetPixel(0, y), ref r, ref g, ref b, ref count);
                Add(source.GetPixel(width - 1, y), ref r, ref g, ref b, ref count);
            }

            if (count <= 0)
                return Color.White;

            return Color.FromArgb(
                ClampInt((int)(r / count), 0, 255),
                ClampInt((int)(g / count), 0, 255),
                ClampInt((int)(b / count), 0, 255)
            );
        }

        private static void Add(
            Color color,
            ref long r,
            ref long g,
            ref long b,
            ref int count)
        {
            r += color.R;
            g += color.G;
            b += color.B;
            count++;
        }

        private static Bitmap BuildSilhouette(
            Bitmap source,
            out double confidence)
        {
            Bitmap normalized = Normalize(
                source,
                OutputSize
            );

            try
            {
                int width = normalized.Width;
                int height = normalized.Height;

                int[,] gray = new int[width, height];
                int[,] saturation = new int[width, height];

                long globalGraySum = 0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color c = normalized.GetPixel(x, y);
                        gray[x, y] = Gray(c);
                        saturation[x, y] = Saturation(c);
                        globalGraySum += gray[x, y];
                    }
                }

                int globalMean = (int)(
                    globalGraySum /
                    Math.Max(1.0, width * (double)height)
                );

                int otsu = CalculateOtsu(gray, width, height);
                int[,] integral = BuildIntegral(gray, width, height);

                bool[,] mask = new bool[width, height];

                int radius = 12;
                int darkLimit = Math.Min(205, otsu + 22);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int localMean = LocalMean(
                            integral,
                            width,
                            height,
                            x,
                            y,
                            radius
                        );

                        int g = gray[x, y];
                        int s = saturation[x, y];

                        bool darkShape =
                            g <= darkLimit &&
                            g <= localMean - 7;

                        bool colouredMetalOrStone =
                            s >= 42 &&
                            g <= 225 &&
                            g <= localMean + 18;

                        bool veryDark =
                            g <= Math.Min(145, globalMean - 20);

                        mask[x, y] =
                            darkShape ||
                            colouredMetalOrStone ||
                            veryDark;
                    }
                }

                // White border / margin ko foreground banne se roko.
                ClearOuterBorder(mask, width, height, 8);

                // Broken jewellery parts ko join karo.
                mask = Dilate(mask, width, height, 2);
                mask = Erode(mask, width, height, 2);

                // Isolated noise hatao.
                mask = Erode(mask, width, height, 1);
                mask = Dilate(mask, width, height, 1);

                mask = KeepUsefulComponents(
                    mask,
                    width,
                    height
                );

                // Stone highlights aur chhote internal gaps ko close karo.
                mask = Dilate(mask, width, height, 1);
                mask = Erode(mask, width, height, 1);

                int foregroundCount = CountForeground(
                    mask,
                    width,
                    height
                );

                double foregroundRatio =
                    foregroundCount /
                    Math.Max(1.0, width * (double)height);

                confidence = CalculateConfidence(
                    foregroundRatio
                );

                return MaskToBitmap(
                    mask,
                    width,
                    height
                );
            }
            finally
            {
                normalized.Dispose();
            }
        }

        private static bool[,] KeepUsefulComponents(
            bool[,] source,
            int width,
            int height)
        {
            bool[,] visited = new bool[width, height];
            bool[,] result = new bool[width, height];

            int minimumArea = Math.Max(
                18,
                width * height / 3500
            );

            double centerX = (width - 1) / 2.0;
            double centerY = (height - 1) / 2.0;
            double maxDistance = Math.Sqrt(
                centerX * centerX +
                centerY * centerY
            );

            int[] dx =
            {
                -1, 0, 1,
                -1,    1,
                -1, 0, 1
            };

            int[] dy =
            {
                -1, -1, -1,
                 0,      0,
                 1,  1,  1
            };

            Queue<Point> queue = new Queue<Point>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!source[x, y] || visited[x, y])
                        continue;

                    List<Point> points = new List<Point>();
                    queue.Enqueue(new Point(x, y));
                    visited[x, y] = true;

                    long sumX = 0;
                    long sumY = 0;

                    while (queue.Count > 0)
                    {
                        Point p = queue.Dequeue();
                        points.Add(p);
                        sumX += p.X;
                        sumY += p.Y;

                        for (int i = 0; i < dx.Length; i++)
                        {
                            int nx = p.X + dx[i];
                            int ny = p.Y + dy[i];

                            if (nx < 0 || nx >= width ||
                                ny < 0 || ny >= height)
                            {
                                continue;
                            }

                            if (visited[nx, ny] ||
                                !source[nx, ny])
                            {
                                continue;
                            }

                            visited[nx, ny] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }

                    int area = points.Count;

                    if (area < minimumArea)
                        continue;

                    double componentX =
                        sumX / (double)Math.Max(1, area);

                    double componentY =
                        sumY / (double)Math.Max(1, area);

                    double distance = Math.Sqrt(
                        (componentX - centerX) *
                        (componentX - centerX) +
                        (componentY - centerY) *
                        (componentY - centerY)
                    );

                    double normalizedDistance =
                        distance /
                        Math.Max(1.0, maxDistance);

                    bool keep =
                        area >= minimumArea * 4 ||
                        normalizedDistance <= 0.72;

                    if (!keep)
                        continue;

                    for (int i = 0; i < points.Count; i++)
                    {
                        Point p = points[i];
                        result[p.X, p.Y] = true;
                    }
                }
            }

            return result;
        }

        private static Bitmap BuildLineArt(Bitmap silhouette)
        {
            int width = silhouette.Width;
            int height = silhouette.Height;

            bool[,] mask = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mask[x, y] =
                        Gray(silhouette.GetPixel(x, y)) < 128;
                }
            }

            bool[,] boundary = new bool[width, height];

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    if (!mask[x, y])
                        continue;

                    bool isBoundary =
                        !mask[x - 1, y] ||
                        !mask[x + 1, y] ||
                        !mask[x, y - 1] ||
                        !mask[x, y + 1] ||
                        !mask[x - 1, y - 1] ||
                        !mask[x + 1, y - 1] ||
                        !mask[x - 1, y + 1] ||
                        !mask[x + 1, y + 1];

                    if (isBoundary)
                        boundary[x, y] = true;
                }
            }

            // Line ko visible aur continuous banao.
            boundary = Dilate(
                boundary,
                width,
                height,
                1
            );

            return MaskToBitmap(
                boundary,
                width,
                height
            );
        }

        private static Bitmap Normalize(
            Bitmap source,
            int size)
        {
            Bitmap result = new Bitmap(
                size,
                size,
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
                graphics.CompositingQuality =
                    CompositingQuality.HighQuality;

                int margin = 14;
                int available = size - margin * 2;

                double scale = Math.Min(
                    available / (double)Math.Max(1, source.Width),
                    available / (double)Math.Max(1, source.Height)
                );

                int width = Math.Max(
                    1,
                    (int)Math.Round(source.Width * scale)
                );

                int height = Math.Max(
                    1,
                    (int)Math.Round(source.Height * scale)
                );

                int left = (size - width) / 2;
                int top = (size - height) / 2;

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

        private static int[,] BuildIntegral(
            int[,] gray,
            int width,
            int height)
        {
            int[,] integral = new int[width + 1, height + 1];

            for (int y = 1; y <= height; y++)
            {
                int rowSum = 0;

                for (int x = 1; x <= width; x++)
                {
                    rowSum += gray[x - 1, y - 1];

                    integral[x, y] =
                        integral[x, y - 1] +
                        rowSum;
                }
            }

            return integral;
        }

        private static int LocalMean(
            int[,] integral,
            int width,
            int height,
            int x,
            int y,
            int radius)
        {
            int left = Math.Max(0, x - radius);
            int top = Math.Max(0, y - radius);
            int right = Math.Min(width - 1, x + radius);
            int bottom = Math.Min(height - 1, y + radius);

            int sum =
                integral[right + 1, bottom + 1] -
                integral[left, bottom + 1] -
                integral[right + 1, top] +
                integral[left, top];

            int count =
                (right - left + 1) *
                (bottom - top + 1);

            return sum / Math.Max(1, count);
        }

        private static int CalculateOtsu(
            int[,] gray,
            int width,
            int height)
        {
            int[] histogram = new int[256];
            long totalIntensity = 0;
            int total = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int value = gray[x, y];
                    histogram[value]++;
                    totalIntensity += value;
                }
            }

            long backgroundIntensity = 0;
            int backgroundCount = 0;
            double bestVariance = -1;
            int bestThreshold = 175;

            for (int threshold = 0; threshold < 256; threshold++)
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
                    (totalIntensity -
                     backgroundIntensity) /
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
                210
            );
        }

        private static bool[,] Dilate(
            bool[,] source,
            int width,
            int height,
            int iterations)
        {
            bool[,] current = source;

            for (int iteration = 0;
                 iteration < iterations;
                 iteration++)
            {
                bool[,] next =
                    new bool[width, height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        bool value = false;

                        for (int yy = -1;
                             yy <= 1 && !value;
                             yy++)
                        {
                            for (int xx = -1;
                                 xx <= 1;
                                 xx++)
                            {
                                int nx = x + xx;
                                int ny = y + yy;

                                if (nx < 0 || nx >= width ||
                                    ny < 0 || ny >= height)
                                {
                                    continue;
                                }

                                if (current[nx, ny])
                                {
                                    value = true;
                                    break;
                                }
                            }
                        }

                        next[x, y] = value;
                    }
                }

                current = next;
            }

            return current;
        }

        private static bool[,] Erode(
            bool[,] source,
            int width,
            int height,
            int iterations)
        {
            bool[,] current = source;

            for (int iteration = 0;
                 iteration < iterations;
                 iteration++)
            {
                bool[,] next =
                    new bool[width, height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        bool value = true;

                        for (int yy = -1;
                             yy <= 1 && value;
                             yy++)
                        {
                            for (int xx = -1;
                                 xx <= 1;
                                 xx++)
                            {
                                int nx = x + xx;
                                int ny = y + yy;

                                if (nx < 0 || nx >= width ||
                                    ny < 0 || ny >= height ||
                                    !current[nx, ny])
                                {
                                    value = false;
                                    break;
                                }
                            }
                        }

                        next[x, y] = value;
                    }
                }

                current = next;
            }

            return current;
        }

        private static void ClearOuterBorder(
            bool[,] mask,
            int width,
            int height,
            int border)
        {
            int safeBorder = Math.Max(
                1,
                Math.Min(
                    border,
                    Math.Min(width, height) / 4
                )
            );

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < safeBorder ||
                        y < safeBorder ||
                        x >= width - safeBorder ||
                        y >= height - safeBorder)
                    {
                        mask[x, y] = false;
                    }
                }
            }
        }

        private static int CountForeground(
            bool[,] mask,
            int width,
            int height)
        {
            int count = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[x, y])
                        count++;
                }
            }

            return count;
        }

        private static double CalculateConfidence(
            double foregroundRatio)
        {
            if (foregroundRatio < 0.01)
                return 0.15;

            if (foregroundRatio < 0.03)
                return 0.40;

            if (foregroundRatio <= 0.58)
                return 0.88;

            if (foregroundRatio <= 0.72)
                return 0.62;

            return 0.35;
        }

        private static Bitmap MaskToBitmap(
            bool[,] mask,
            int width,
            int height)
        {
            Bitmap result = new Bitmap(
                width,
                height,
                PixelFormat.Format24bppRgb
            );

            using (Graphics graphics =
                   Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[x, y])
                        result.SetPixel(x, y, Color.Black);
                }
            }

            return result;
        }

        private static Rectangle AddPadding(
            Rectangle bounds,
            int width,
            int height)
        {
            int horizontal = Math.Max(
                6,
                bounds.Width / 12
            );

            int vertical = Math.Max(
                6,
                bounds.Height / 12
            );

            return Rectangle.FromLTRB(
                Math.Max(
                    0,
                    bounds.Left - horizontal
                ),
                Math.Max(
                    0,
                    bounds.Top - vertical
                ),
                Math.Min(
                    width,
                    bounds.Right + horizontal
                ),
                Math.Min(
                    height,
                    bounds.Bottom + vertical
                )
            );
        }

        private static ImagePreprocessResult CreateFallback(
            Bitmap source)
        {
            Bitmap crop = new Bitmap(source);
            double confidence;
            Bitmap silhouette = BuildSilhouette(
                crop,
                out confidence
            );

            Bitmap lineArt =
                BuildLineArt(silhouette);

            return new ImagePreprocessResult
            {
                CroppedOriginal = crop,
                Silhouette = silhouette,
                LineArt = lineArt,
                SourceBounds = new Rectangle(
                    0,
                    0,
                    source.Width,
                    source.Height
                ),
                Confidence = confidence,
                UsedFallback = true,
                Method = "ADAPTIVE-FALLBACK"
            };
        }

        private static ImagePreprocessResult CreateEmpty()
        {
            return new ImagePreprocessResult
            {
                CroppedOriginal = CreateBlank(),
                Silhouette = CreateBlank(),
                LineArt = CreateBlank(),
                SourceBounds = Rectangle.Empty,
                Confidence = 0,
                UsedFallback = true,
                Method = "EMPTY"
            };
        }

        private static Bitmap CreateBlank()
        {
            Bitmap bitmap = new Bitmap(
                OutputSize,
                OutputSize,
                PixelFormat.Format24bppRgb
            );

            using (Graphics graphics =
                   Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
            }

            return bitmap;
        }

        private static int Gray(Color color)
        {
            return (
                color.R * 299 +
                color.G * 587 +
                color.B * 114
            ) / 1000;
        }

        private static int Saturation(Color color)
        {
            int maximum = Math.Max(
                color.R,
                Math.Max(color.G, color.B)
            );

            int minimum = Math.Min(
                color.R,
                Math.Min(color.G, color.B)
            );

            return maximum - minimum;
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
