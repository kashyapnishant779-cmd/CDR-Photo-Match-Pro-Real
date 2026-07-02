using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CDRPhotoMatchPro.Core
{
    public static class ImageMatcher
    {
        public static double Compare(string queryImagePath, string dbImagePath)
        {
            return CompareImages(queryImagePath, dbImagePath);
        }

        public static double CompareImages(string queryImagePath, string dbImagePath)
        {
            if (!File.Exists(queryImagePath) || !File.Exists(dbImagePath))
                return 0;

            using (Mat q0 = Cv2.ImRead(queryImagePath, ImreadModes.Color))
            using (Mat d0 = Cv2.ImRead(dbImagePath, ImreadModes.Color))
            {
                if (q0.Empty() || d0.Empty())
                    return 0;

                using (Mat q = PreprocessJewellery(q0))
                using (Mat d = PreprocessJewellery(d0))
                {
                    double best = 0;

                    int[] angles = { 0, 90, 180, 270 };

                    foreach (int angle in angles)
                    {
                        using (Mat qr = Rotate(q, angle))
                        {
                            double score = FinalScore(qr, d);
                            if (score > best) best = score;
                        }
                    }

                    if (best < 0) best = 0;
                    if (best > 100) best = 100;

                    return Math.Round(best, 2);
                }
            }
        }

        private static Mat PreprocessJewellery(Mat src)
        {
            Mat resized = ResizeMax(src, 700);

            Mat gray = new Mat();
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.EqualizeHist(gray, gray);
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(3, 3), 0);

            Mat edges = new Mat();
            Cv2.Canny(gray, edges, 45, 130);

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            Cv2.Dilate(edges, edges, kernel);
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel);

            Mat mask = RemoveBackgroundByContours(edges, resized.Size());

            Mat output = new Mat();
            Cv2.BitwiseAnd(resized, resized, output, mask);

            gray.Dispose();
            edges.Dispose();
            kernel.Dispose();
            mask.Dispose();
            resized.Dispose();

            return output;
        }

        private static Mat RemoveBackgroundByContours(Mat edge, OpenCvSharp.Size size)
        {
            Mat mask = Mat.Zeros(size, MatType.CV_8UC1);

            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;

            Cv2.FindContours(edge.Clone(), out contours, out hierarchy,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
                return edge.Clone();

            double imgArea = size.Width * size.Height;

            foreach (var c in contours)
            {
                double area = Cv2.ContourArea(c);
                if (area < imgArea * 0.002) continue;
                if (area > imgArea * 0.85) continue;

                Rect r = Cv2.BoundingRect(c);

                double ratio = r.Width / (double)Math.Max(1, r.Height);

                if (ratio > 8 || ratio < 0.12)
                    continue;

                Cv2.DrawContours(mask, new[] { c }, -1, Scalar.White, -1);
            }

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(9, 9));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.Dilate(mask, mask, kernel);

            kernel.Dispose();
            return mask;
        }

        private static double FinalScore(Mat query, Mat candidate)
        {
            using (Mat qGray = ToGray(query))
            using (Mat cGray = ToGray(candidate))
            using (Mat qEdge = Edge(qGray))
            using (Mat cEdge = Edge(cGray))
            {
                double orb = OrbScore(qGray, cGray);
                double contour = ContourScore(qEdge, cEdge);
                double hist = HistogramScore(qGray, cGray);
                double aspect = AspectScore(qEdge, cEdge);
                double edge = EdgeScore(qEdge, cEdge);
                double center = CenterMassScore(qEdge, cEdge);

                double final =
                    orb * 0.30 +
                    contour * 0.25 +
                    edge * 0.18 +
                    hist * 0.12 +
                    aspect * 0.10 +
                    center * 0.05;

                if (orb < 12 && contour < 20 && edge < 18)
                    final *= 0.45;

                if (aspect < 25)
                    final *= 0.65;

                return final;
            }
        }

        private static double OrbScore(Mat a, Mat b)
        {
            try
            {
                var orb = ORB.Create(800);

                KeyPoint[] kp1, kp2;
                Mat des1 = new Mat();
                Mat des2 = new Mat();

                orb.DetectAndCompute(a, null, out kp1, des1);
                orb.DetectAndCompute(b, null, out kp2, des2);

                if (des1.Empty() || des2.Empty() || kp1.Length < 5 || kp2.Length < 5)
                    return 0;

                var matcher = new BFMatcher(NormTypes.Hamming, false);
                var matches = matcher.KnnMatch(des1, des2, 2);

                int good = 0;
                foreach (var m in matches)
                {
                    if (m.Length >= 2 && m[0].Distance < 0.75 * m[1].Distance)
                        good++;
                }

                double baseScore = good * 100.0 / Math.Max(20, Math.Min(kp1.Length, kp2.Length));

                des1.Dispose();
                des2.Dispose();
                orb.Dispose();
                matcher.Dispose();

                return Clamp(baseScore * 1.6);
            }
            catch
            {
                return 0;
            }
        }

        private static double ContourScore(Mat aEdge, Mat bEdge)
        {
            var ca = BiggestContour(aEdge);
            var cb = BiggestContour(bEdge);

            if (ca == null || cb == null)
                return 0;

            double diff = Cv2.MatchShapes(ca, cb, ShapeMatchModes.I1, 0);
            double score = 100.0 / (1.0 + diff * 25.0);

            return Clamp(score);
        }

        private static double HistogramScore(Mat a, Mat b)
        {
            Mat ha = new Mat();
            Mat hb = new Mat();

            Cv2.CalcHist(new[] { a }, new[] { 0 }, null, ha, 1, new[] { 64 }, new Rangef[] { new Rangef(0, 256) });
            Cv2.CalcHist(new[] { b }, new[] { 0 }, null, hb, 1, new[] { 64 }, new Rangef[] { new Rangef(0, 256) });

            Cv2.Normalize(ha, ha, 0, 1, NormTypes.MinMax);
            Cv2.Normalize(hb, hb, 0, 1, NormTypes.MinMax);

            double corr = Cv2.CompareHist(ha, hb, HistCompMethods.Correl);
            ha.Dispose();
            hb.Dispose();

            return Clamp((corr + 1) * 50);
        }

        private static double AspectScore(Mat aEdge, Mat bEdge)
        {
            Rect ra = ObjectRect(aEdge);
            Rect rb = ObjectRect(bEdge);

            if (ra.Width <= 0 || rb.Width <= 0)
                return 0;

            double ar1 = ra.Width / (double)Math.Max(1, ra.Height);
            double ar2 = rb.Width / (double)Math.Max(1, rb.Height);

            double diff = Math.Abs(ar1 - ar2) / Math.Max(ar1, ar2);
            return Clamp(100 - diff * 100);
        }

        private static double EdgeScore(Mat aEdge, Mat bEdge)
        {
            Mat ar = new Mat();
            Mat br = new Mat();

            Cv2.Resize(aEdge, ar, new OpenCvSharp.Size(256, 256));
            Cv2.Resize(bEdge, br, new OpenCvSharp.Size(256, 256));

            Mat inter = new Mat();
            Mat union = new Mat();

            Cv2.BitwiseAnd(ar, br, inter);
            Cv2.BitwiseOr(ar, br, union);

            double i = Cv2.CountNonZero(inter);
            double u = Cv2.CountNonZero(union);

            ar.Dispose();
            br.Dispose();
            inter.Dispose();
            union.Dispose();

            if (u <= 0) return 0;

            return Clamp((i / u) * 100.0 * 2.2);
        }

        private static double CenterMassScore(Mat aEdge, Mat bEdge)
        {
            Moments ma = Cv2.Moments(aEdge, true);
            Moments mb = Cv2.Moments(bEdge, true);

            if (ma.M00 == 0 || mb.M00 == 0)
                return 0;

            double ax = ma.M10 / ma.M00 / aEdge.Width;
            double ay = ma.M01 / ma.M00 / aEdge.Height;

            double bx = mb.M10 / mb.M00 / bEdge.Width;
            double by = mb.M01 / mb.M00 / bEdge.Height;

            double dist = Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(ay - by, 2));
            return Clamp(100 - dist * 180);
        }

        private static OpenCvSharp.Point[] BiggestContour(Mat edge)
        {
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;

            Cv2.FindContours(edge.Clone(), out contours, out hierarchy,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
                return null;

            double bestArea = 0;
            OpenCvSharp.Point[] best = null;

            foreach (var c in contours)
            {
                double area = Cv2.ContourArea(c);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = c;
                }
            }

            return best;
        }

        private static Rect ObjectRect(Mat edge)
        {
            var c = BiggestContour(edge);
            if (c == null) return new Rect(0, 0, 0, 0);
            return Cv2.BoundingRect(c);
        }

        private static Mat ToGray(Mat src)
        {
            Mat gray = new Mat();

            if (src.Channels() == 1)
                src.CopyTo(gray);
            else
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.EqualizeHist(gray, gray);
            return gray;
        }

        private static Mat Edge(Mat gray)
        {
            Mat e = new Mat();
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(3, 3), 0);
            Cv2.Canny(gray, e, 40, 120);

            Mat k = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(e, e, MorphTypes.Close, k);

            k.Dispose();
            return e;
        }

        private static Mat ResizeMax(Mat src, int max)
        {
            int w = src.Width;
            int h = src.Height;

            double scale = Math.Min(max / (double)w, max / (double)h);
            if (scale >= 1.0)
                return src.Clone();

            Mat dst = new Mat();
            Cv2.Resize(src, dst, new OpenCvSharp.Size((int)(w * scale), (int)(h * scale)));
            return dst;
        }

        private static Mat Rotate(Mat src, int angle)
        {
            if (angle == 0)
                return src.Clone();

            Mat dst = new Mat();

            if (angle == 90)
                Cv2.Rotate(src, dst, RotateFlags.Rotate90Clockwise);
            else if (angle == 180)
                Cv2.Rotate(src, dst, RotateFlags.Rotate180);
            else if (angle == 270)
                Cv2.Rotate(src, dst, RotateFlags.Rotate90Counterclockwise);
            else
                dst = src.Clone();

            return dst;
        }

        private static double Clamp(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return 0;

            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }
    }
}
