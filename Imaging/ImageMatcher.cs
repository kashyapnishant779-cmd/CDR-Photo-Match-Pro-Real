using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        public byte[] ExtractDescriptorBytes(string imagePath)
        {
            using (var img = Cv2.ImRead(imagePath, ImreadModes.Grayscale))
            {
                if (img.Empty()) throw new InvalidOperationException("Image read failed: " + imagePath);
                using (var normalized = new Mat())
                using (var orb = ORB.Create(1500))
                using (var descriptors = new Mat())
                {
                    Cv2.Resize(img, normalized, new Size(900, 900), 0, 0, InterpolationFlags.Area);
                    Cv2.EqualizeHist(normalized, normalized);
                    KeyPoint[] kp; orb.DetectAndCompute(normalized, null, out kp, descriptors);
                    if (descriptors.Empty()) return new byte[0];
                    return descriptors.ToBytes();
                }
            }
        }
        public double Compare(byte[] queryDescriptor, byte[] indexedDescriptor)
        {
            if (queryDescriptor == null || indexedDescriptor == null || queryDescriptor.Length == 0 || indexedDescriptor.Length == 0) return 0;
            using (var q = Mat.FromPixelData(queryDescriptor.Length / 32, 32, MatType.CV_8UC1, queryDescriptor))
            using (var t = Mat.FromPixelData(indexedDescriptor.Length / 32, 32, MatType.CV_8UC1, indexedDescriptor))
            using (var matcher = new BFMatcher(NormTypes.Hamming, false))
            {
                var matches = matcher.KnnMatch(q, t, 2);
                int good = 0;
                foreach (var pair in matches)
                {
                    if (pair.Length >= 2 && pair[0].Distance < 0.78 * pair[1].Distance) good++;
                    else if (pair.Length == 1 && pair[0].Distance < 45) good++;
                }
                var basis = Math.Max(25.0, Math.Min(q.Rows, t.Rows));
                return Math.Min(100.0, (good / basis) * 100.0);
            }
        }
        public System.Drawing.Size ReadSize(string imagePath)
        {
            using (var img = System.Drawing.Image.FromFile(imagePath)) return img.Size;
        }
    }
}
