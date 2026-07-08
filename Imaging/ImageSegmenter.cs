using System;
using System.Collections.Generic;
using System.Drawing;
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
        public static List<ImageSegment> Split(Bitmap source)
        {
            return Segment(source);
        }

        public static List<ImageSegment> Segment(Bitmap source)
        {
            var list = new List<ImageSegment>();

            Bitmap clean = ExtractMainDesign(source);

            list.Add(new ImageSegment
            {
                Name = "FULL",
                Bitmap = new Bitmap(clean),
                Bounds = new Rectangle(0, 0, clean.Width, clean.Height),
                Weight = 1.0
            });

            int w = clean.Width;
            int h = clean.Height;

            AddCrop(list, clean, "CENTER", new Rectangle(w / 4, h / 4, w / 2, h / 2), 0.85);
            AddCrop(list, clean, "LEFT", new Rectangle(0, h / 5, w / 2, h * 3 / 5), 0.55);
            AddCrop(list, clean, "RIGHT", new Rectangle(w / 2, h / 5, w / 2, h * 3 / 5), 0.55);
            AddCrop(list, clean, "TOP", new Rectangle(w / 5, 0, w * 3 / 5, h / 2), 0.45);
            AddCrop(list, clean, "BOTTOM", new Rectangle(w / 5, h / 2, w * 3 / 5, h / 2), 0.45);

            clean.Dispose();
            return list;
        }

        private static void AddCrop(List<ImageSegment> list, Bitmap src, string name, Rectangle r, double weight)
        {
            r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (r.Width < 20 || r.Height < 20) return;

            Bitmap b = src.Clone(r, PixelFormat.Format24bppRgb);

            list.Add(new ImageSegment
            {
                Name = name,
                Bitmap = b,
                Bounds = r,
                Weight = weight
            });
        }

        public static Bitmap ExtractMainDesign(Bitmap src)
        {
            Bitmap small = ResizeKeep(src, 600, 600);
            Rectangle box = FindDesignBounds(small);

            if (box == Rectangle.Empty || box.Width < 30 || box.Height < 30)
                return small;

            int padX = box.Width / 8;
            int padY = box.Height / 8;

            int left = Math.Max(0, box.Left - padX);
            int top = Math.Max(0, box.Top - padY);
            int right = Math.Min(small.Width - 1, box.Right + padX);
            int bottom = Math.Min(small.Height - 1, box.Bottom + padY);

            Rectangle crop = Rectangle.FromLTRB(left, top, right, bottom);

            Bitmap result = small.Clone(crop, PixelFormat.Format24bppRgb);
            small.Dispose();

            return result;
        }

        private static Rectangle FindDesignBounds(Bitmap bmp)
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
            bool found = false;

            for (int y = 0; y < bmp.Height; y += 2)
            {
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    Color c = bmp.GetPixel(x, y);

                    int gray = (c.R + c.G + c.B) / 3;
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;

                    bool skin = c.R > 140 && c.G > 80 && c.B > 45 && c.R > c.B + 25;
                    bool blueRuler = c.B > 120 && c.B > c.R + 20;
                    bool whiteBg = gray > 220 && sat < 40;

                    bool darkCut = gray < 150 && sat < 100;
                    bool gold = c.R > 120 && c.G > 70 && c.B < 150 && sat > 25;
                    bool stone = sat > 45 && gray > 55 && gray < 230;

                    bool design = (darkCut || gold || stone) && !skin && !blueRuler && !whiteBg;

                    if (design)
                    {
                        found = true;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (!found)
                return Rectangle.Empty;

            return Rectangle.FromLTRB(minX, minY, maxX, maxY);
        }

        private static Bitmap ResizeKeep(Bitmap src, int maxW, int maxH)
        {
            double ratio = Math.Min((double)maxW / src.Width, (double)maxH / src.Height);
            if (ratio > 1) ratio = 1;

            int w = Math.Max(1, (int)(src.Width * ratio));
            int h = Math.Max(1, (int)(src.Height * ratio));

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }

            return dst;
        }
    }
}
