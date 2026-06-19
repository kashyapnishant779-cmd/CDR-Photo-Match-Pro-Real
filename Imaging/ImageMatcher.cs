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
            using (var small = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, 32, 32);

                byte[] data = new byte[32 * 32];

                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        int gray = (c.R + c.G + c.B) / 3;
                        data[y * 32 + x] = (byte)gray;
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
            long diff = 0;

            for (int i = 0; i < len; i++)
                diff += Math.Abs(queryDescriptor[i] - indexedDescriptor[i]);

            double maxDiff = len * 255.0;
            double score = 100.0 - ((diff / maxDiff) * 100.0);

            if (score < 0) score = 0;
            if (score > 100) score = 100;

            return score;
        }

        public Size ReadSize(string imagePath)
        {
            using (var img = Image.FromFile(imagePath))
                return img.Size;
        }
    }
}
