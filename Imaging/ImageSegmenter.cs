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
    }

    public static class ImageSegmenter
    {
        public static List<ImageSegment> Split(Bitmap source)
        {
            var list = new List<ImageSegment>();

            Bitmap clean = CleanDesignOnly(source);

            list.Add(new ImageSegment { Name = "FULL", Bitmap = new Bitmap(clean) });

            int w = clean.Width;
            int h = clean.Height;

            AddCrop(list, clean, "TOP", new Rectangle(0, 0, w, h / 2));
            AddCrop(list, clean, "BOTTOM", new Rectangle(0, h / 2, w, h - h / 2));
            AddCrop(list, clean, "LEFT", new Rectangle(0, 0, w / 2, h));
            AddCrop(list, clean, "RIGHT", new Rectangle(w / 2, 0, w - w / 2, h));
            AddCrop(list, clean, "CENTER", new Rectangle(w / 4, h / 4, w / 2, h / 2));

            clean.Dispose();
            return list;
        }

        private static void AddCrop(List<ImageSegment> list, Bitmap src, string name, Rectangle r)
        {
            if (r.Width < 20 || r.Height < 20) return;

            list.Add(new ImageSegment
            {
                Name = name,
                Bitmap = src.Clone(r, PixelFormat.Format24bppRgb)
            });
        }

        private static Bitmap CleanDesignOnly(Bitmap src)
        {
            int w = src.Width;
            int h = src.Height;

            Bitmap bw = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            int minX = w, minY = h, maxX = 0, maxY = 0;
            bool found = false;

            using (Graphics g = Graphics.FromImage(bw))
                g.Clear(Color.White);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = src.GetPixel(x, y);

                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;
                    int bright = (c.R + c.G + c.B) / 3;

                    bool gold = c.R > 120 && c.G > 75 && c.B < 120 && sat > 35;
                    bool stone = sat > 45 && bright > 70;
                    bool darkCut = bright < 95 && sat < 80;

                    bool design = gold || stone || darkCut;

                    if (design)
                    {
                        bw.SetPixel(x, y, Color.Black);
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                        found = true;
                    }
                    else
                    {
                        bw.SetPixel(x, y, Color.White);
                    }
                }
            }

            if (!found)
                return new Bitmap(src);

            int pad = 25;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(w - 1, maxX + pad);
            maxY = Math.Min(h - 1, maxY + pad);

            Rectangle crop = new Rectangle(minX, minY, Math.Max(20, maxX - minX), Math.Max(20, maxY - minY));
            Bitmap result = bw.Clone(crop, PixelFormat.Format24bppRgb);
            bw.Dispose();

            return result;
        }
    }
}
