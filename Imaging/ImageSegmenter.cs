using System;
using System.Collections.Generic;
using System.Drawing;

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

            list.Add(new ImageSegment
            {
                Name = "FULL",
                Bitmap = new Bitmap(source)
            });

            int w = source.Width;
            int h = source.Height;

            list.Add(new ImageSegment
            {
                Name = "TOP",
                Bitmap = source.Clone(
                    new Rectangle(0, 0, w, h / 2),
                    source.PixelFormat)
            });

            list.Add(new ImageSegment
            {
                Name = "BOTTOM",
                Bitmap = source.Clone(
                    new Rectangle(0, h / 2, w, h - h / 2),
                    source.PixelFormat)
            });

            list.Add(new ImageSegment
            {
                Name = "LEFT",
                Bitmap = source.Clone(
                    new Rectangle(0, 0, w / 2, h),
                    source.PixelFormat)
            });

            list.Add(new ImageSegment
            {
                Name = "RIGHT",
                Bitmap = source.Clone(
                    new Rectangle(w / 2, 0, w - w / 2, h),
                    source.PixelFormat)
            });

            return list;
        }
    }
}
