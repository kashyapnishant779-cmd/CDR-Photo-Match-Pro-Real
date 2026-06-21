using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image not found", imagePath);

            using (var original = new Bitmap(imagePath))
            using (var cropped = CropImportantArea(original))
            using (var small = new Bitmap(64, 64))
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(cropped, 0, 0, 64, 64);

                byte[] data = new byte[64 * 64];

                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        int gray = (c.R + c.G + c.B) / 3;

                        // foreground ko strong karo
                        data[y * 64 + x] = (byte)(gray < 180 ? 0 : 255);
                    }
                }

                return data;
            }
        }

        public double Compare(byte[] queryDescriptor, byte[] indexedDescriptor)
        {
            if (queryDescriptor == null || indexedDescriptor == null)
                return 0;

            if (queryDescriptor.Length == 0 || indexedDescriptor.Length == 0)
                return 0;

            int len = Math.Min(queryDescriptor.Length, indexedDescriptor.Length);

            int sameDark = 0;
            int queryDark = 0;
            int indexedDark = 0;

            for (int i = 0; i < len; i++)
            {
                bool qDark = queryDescriptor[i] < 128;
                bool dDark = indexedDescriptor[i] < 128;

                if (qDark) queryDark++;
                if (dDark) indexedDark++;
                if (qDark && dDark) sameDark++;
            }

            if (queryDark == 0 || indexedDark == 0)
                return 0;

            double score1 = sameDark * 100.0 / queryDark;
            double score2 = sameDark * 100.0 / indexedDark;

            double score = (score1 + score2) / 2.0;

            if (score < 0) score = 0;
            if (score > 100) score = 100;

            return score;
        }

        public Size ReadSize(string imagePath)
        {
            using (var img = Image.FromFile(imagePath))
                return img.Size;
        }

        private Bitmap CropImportantArea(Bitmap src)
        {
            int minX = src.Width;
            int minY = src.Height;
            int maxX = 0;
            int maxY = 0;

            for (int y = 0; y < src.Height; y += 2)
            {
                for (int x = 0; x < src.Width; x += 2)
                {
                    Color c = src.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;

                    // dark design area detect
                    if (gray < 190)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX <= minX || maxY <= minY)
                return new Bitmap(src);

            int pad = 20;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(src.Width - 1, maxX + pad);
            maxY = Math.Min(src.Height - 1, maxY + pad);

            Rectangle rect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return src.Clone(rect, src.PixelFormat);
        }
    }
}
