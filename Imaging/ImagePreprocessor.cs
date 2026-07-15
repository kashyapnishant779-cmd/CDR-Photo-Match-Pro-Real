using System;
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

                silhouette = BuildSilhouette(cropped);
                lineArt = BuildLineArt(silhouette);

                return new ImagePreprocessResult
                {
                    CroppedOriginal = cropped,
                    Silhouette = silhouette,
                    LineArt = lineArt,
                    SourceBounds = bounds,
                    Confidence = 0.85,
                    UsedFallback = false,
                    Method = "SIMPLE-SHAPE"
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
                Math.Min(width, height) / 700
            );

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    Color c = source.GetPixel(x, y);

                    int difference =
                        Math.Abs(c.R - background.R) +
                        Math.Abs(c.G - background.G) +
                        Math.Abs(c.B - background.B);

                    int gray = Gray(c);

                    bool useful =
                        difference >= 48 ||
                        gray <= 205;

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

            return Rectangle.FromLTRB(
                minX,
                minY,
                Math.Min(width, maxX + step),
                Math.Min(height, maxY + step)
            );
        }

        private static Color EstimateBackground(Bitmap source)
        {
            long r = 0;
            long g = 0;
            long b = 0;
            int count = 0;

            int width = source.Width;
            int height = source.Height;
            int stepX = Math.Max(1, width / 60);
            int stepY = Math.Max(1, height / 60);

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

        private static Bitmap BuildSilhouette(Bitmap source)
        {
            Bitmap normalized = Normalize(source, OutputSize);
            int threshold = CalculateOtsu(normalized);

            Bitmap result = new Bitmap(
                OutputSize,
                OutputSize,
                PixelFormat.Format24bppRgb
            );

            using (Graphics graphics = Graphics.FromImage(result))
                graphics.Clear(Color.White);

            int dark = 0;
            int light = 0;

            for (int y = 0; y < normalized.Height; y++)
            {
                for (int x = 0; x < normalized.Width; x++)
                {
                    int gray = Gray(normalized.GetPixel(x, y));

                    if (gray <= threshold)
                        dark++;
                    else
                        light++;
                }
            }

            bool invert = dark > light * 2;

            for (int y = 0; y < normalized.Height; y++)
            {
                for (int x = 0; x < normalized.Width; x++)
                {
                    int gray = Gray(normalized.GetPixel(x, y));
                    bool foreground =
                        invert
                            ? gray >= threshold
                            : gray <= threshold;

                    result.SetPixel(
                        x,
                        y,
                        foreground
                            ? Color.Black
                            : Color.White
                    );
                }
            }

            normalized.Dispose();
            return result;
        }

        private static Bitmap BuildLineArt(Bitmap silhouette)
        {
            Bitmap result = new Bitmap(
                silhouette.Width,
                silhouette.Height,
                PixelFormat.Format24bppRgb
            );

            using (Graphics graphics = Graphics.FromImage(result))
                graphics.Clear(Color.White);

            for (int y = 1; y < silhouette.Height - 1; y++)
            {
                for (int x = 1; x < silhouette.Width - 1; x++)
                {
                    bool current =
                        Gray(silhouette.GetPixel(x, y)) < 128;

                    if (!current)
                        continue;

                    bool boundary =
                        Gray(silhouette.GetPixel(x - 1, y)) >= 128 ||
                        Gray(silhouette.GetPixel(x + 1, y)) >= 128 ||
                        Gray(silhouette.GetPixel(x, y - 1)) >= 128 ||
                        Gray(silhouette.GetPixel(x, y + 1)) >= 128;

                    if (boundary)
                    {
                        result.SetPixel(x, y, Color.Black);

                        if (x + 1 < result.Width)
                            result.SetPixel(x + 1, y, Color.Black);

                        if (y + 1 < result.Height)
                            result.SetPixel(x, y + 1, Color.Black);
                    }
                }
            }

            return result;
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

                int margin = 18;
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

        private static int CalculateOtsu(Bitmap bitmap)
        {
            int[] histogram = new int[256];
            long totalIntensity = 0;
            int total = bitmap.Width * bitmap.Height;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int gray = Gray(bitmap.GetPixel(x, y));
                    histogram[gray]++;
                    totalIntensity += gray;
                }
            }

            long backgroundIntensity = 0;
            int backgroundCount = 0;
            double bestVariance = -1;
            int bestThreshold = 180;

            for (int threshold = 0; threshold < 256; threshold++)
            {
                backgroundCount += histogram[threshold];

                if (backgroundCount == 0)
                    continue;

                int foregroundCount = total - backgroundCount;

                if (foregroundCount == 0)
                    break;

                backgroundIntensity +=
                    (long)threshold * histogram[threshold];

                double backgroundMean =
                    backgroundIntensity /
                    (double)backgroundCount;

                double foregroundMean =
                    (totalIntensity - backgroundIntensity) /
                    (double)foregroundCount;

                double difference =
                    backgroundMean - foregroundMean;

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

            return ClampInt(bestThreshold, 75, 220);
        }

        private static Rectangle AddPadding(
            Rectangle bounds,
            int width,
            int height)
        {
            int horizontal = Math.Max(6, bounds.Width / 14);
            int vertical = Math.Max(6, bounds.Height / 14);

            return Rectangle.FromLTRB(
                Math.Max(0, bounds.Left - horizontal),
                Math.Max(0, bounds.Top - vertical),
                Math.Min(width, bounds.Right + horizontal),
                Math.Min(height, bounds.Bottom + vertical)
            );
        }

        private static ImagePreprocessResult CreateFallback(
            Bitmap source)
        {
            Bitmap crop = new Bitmap(source);
            Bitmap silhouette = BuildSilhouette(crop);
            Bitmap lineArt = BuildLineArt(silhouette);

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
                Confidence = 0.45,
                UsedFallback = true,
                Method = "SIMPLE-FALLBACK"
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

            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.White);

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
