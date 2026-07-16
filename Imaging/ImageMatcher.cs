using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        /*
         * Exact segment order:
         *
         * 0 = FULL
         * 1 = CENTER
         * 2 = LEFT
         * 3 = RIGHT
         * 4 = TOP
         * 5 = BOTTOM
         */

        private const int FullPart = 0;
        private const int CenterPart = 1;
        private const int LeftPart = 2;
        private const int RightPart = 3;
        private const int TopPart = 4;
        private const int BottomPart = 5;

        private const int MaxParts = 6;

        /*
         * Route 0 = extracted clean line-art
         * Route 1 = extracted cropped original / blur route
         */

        private const int CleanRoute = 0;
        private const int DirectRoute = 1;
        private const int RouteCount = 2;

        /*
         * Per-part descriptor:
         *
         *  0  Hash
         *  8  EdgeHash
         * 16  HorizontalHash
         * 24  VerticalHash
         * 32  RadialHash
         *
         * 40  DarkRatio
         * 48  EdgeRatio
         * 56  AspectRatio
         * 64  CenterX
         * 72  CenterY
         * 80  BorderRatio
         * 88  Symmetry
         * 96  Segment Weight
         *
         * Total = 104 bytes per part
         */

        private const int PartSize = 104;
        private const int RouteSize = MaxParts * PartSize;
        private const int TotalBytes = RouteCount * RouteSize;

        private const int HashOffset = 0;
        private const int EdgeHashOffset = 8;
        private const int HorizontalHashOffset = 16;
        private const int VerticalHashOffset = 24;
        private const int RadialHashOffset = 32;

        private const int DarkRatioOffset = 40;
        private const int EdgeRatioOffset = 48;
        private const int AspectRatioOffset = 56;
        private const int CenterXOffset = 64;
        private const int CenterYOffset = 72;
        private const int BorderRatioOffset = 80;
        private const int SymmetryOffset = 88;
        private const int WeightOffset = 96;

        private const int PreparedSize = 256;

        public static double Compare(
            string queryImagePath,
            string databaseImagePath)
        {
            ImageMatcher matcher = new ImageMatcher();

            byte[] query =
                matcher.ExtractDescriptorBytes(
                    queryImagePath
                );

            byte[] candidate =
                matcher.ExtractDescriptorBytes(
                    databaseImagePath
                );

            return matcher.Compare(
                query,
                candidate
            );
        }

        public static double CompareImages(
            string queryImagePath,
            string databaseImagePath)
        {
            return Compare(
                queryImagePath,
                databaseImagePath
            );
        }

        public byte[] ExtractDescriptorBytes(
            string imagePath)
        {
            byte[] descriptor =
                new byte[TotalBytes];

            if (string.IsNullOrEmpty(imagePath) ||
                !File.Exists(imagePath))
            {
                return descriptor;
            }

            ImagePreprocessResult processed = null;
            List<ImageSegment> cleanSegments = null;
            List<ImageSegment> directSegments = null;

            try
            {
                using (Bitmap original =
                    new Bitmap(imagePath))
                {
                    /*
                     * Sabse pehle:
                     *
                     * background remove
                     * jewellery detect
                     * jewellery crop
                     * silhouette
                     * line-art
                     */

                    processed =
                        ImagePreprocessor.Process(
                            original
                        );

                    /*
                     * Clean route:
                     * Extracted line-art ko directly segment karo.
                     */

                    if (processed != null &&
                        processed.LineArt != null)
                    {
                        cleanSegments =
                            BuildPreparedSegments(
                                processed.LineArt,
                                true
                            );
                    }

                    /*
                     * Direct route:
                     * Sirf extracted jewellery crop ko use karo.
                     * Puri original JPG ko nahi.
                     */

                    if (processed != null &&
                        processed.CroppedOriginal != null)
                    {
                        directSegments =
                            BuildPreparedSegments(
                                processed.CroppedOriginal,
                                false
                            );
                    }

                    /*
                     * Preprocessor weak/fail ho to old route backup.
                     */

                    if (!HasUsableSegments(
                            cleanSegments
                        ))
                    {
                        DisposeSegments(
                            cleanSegments
                        );

                        cleanSegments =
                            ImageSegmenter.Segment(
                                original
                            );
                    }

                    if (!HasUsableSegments(
                            directSegments
                        ))
                    {
                        DisposeSegments(
                            directSegments
                        );

                        directSegments =
                            BuildPreparedSegments(
                                original,
                                false
                            );
                    }

                    WriteRoute(
                        descriptor,
                        CleanRoute,
                        cleanSegments
                    );

                    WriteRoute(
                        descriptor,
                        DirectRoute,
                        directSegments
                    );
                }
            }
            catch
            {
                return new byte[TotalBytes];
            }
            finally
            {
                DisposeSegments(
                    cleanSegments
                );

                DisposeSegments(
                    directSegments
                );

                if (processed != null)
                    processed.Dispose();
            }

            return descriptor;
        }

        public double Compare(
            byte[] query,
            byte[] candidate)
        {
            if (!IsValidDescriptor(query) ||
                !IsValidDescriptor(candidate))
            {
                return 0;
            }

            double cleanToClean =
                CompareRoute(
                    query,
                    candidate,
                    CleanRoute,
                    CleanRoute
                );

            double directToDirect =
                CompareRoute(
                    query,
                    candidate,
                    DirectRoute,
                    DirectRoute
                );

            double cleanToDirect =
                CompareRoute(
                    query,
                    candidate,
                    CleanRoute,
                    DirectRoute
                );

            double directToClean =
                CompareRoute(
                    query,
                    candidate,
                    DirectRoute,
                    CleanRoute
                );

            double[] routes =
            {
                cleanToClean,
                directToDirect,
                cleanToDirect,
                directToClean
            };

            Array.Sort(routes);

            double best = routes[3];
            double second = routes[2];

            /*
             * Photo/vector pair me ek route naturally strongest ho sakta hai.
             * Lekin top score ko second route ka support chahiye.
             */

            double finalScore =
                best * 0.68 +
                second * 0.32;

            double gap =
                best - second;

            if (gap > 32)
                finalScore *= 0.82;
            else if (gap > 22)
                finalScore *= 0.90;

            if (best >= 86 &&
                second >= 72)
            {
                finalScore += 3.0;
            }
            else if (best >= 78 &&
                     second >= 64)
            {
                finalScore += 1.5;
            }

            return Math.Round(
                Clamp(
                    finalScore,
                    0,
                    100
                ),
                2
            );
        }

        private static double CompareRoute(
            byte[] query,
            byte[] candidate,
            int queryRoute,
            int candidateRoute)
        {
            double fullScore =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    FullPart,
                    FullPart
                );

            double centerScore =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    CenterPart,
                    CenterPart
                );

            double topScore =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    TopPart,
                    TopPart
                );

            double bottomScore =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    BottomPart,
                    BottomPart
                );

            double leftNormal =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    LeftPart,
                    LeftPart
                );

            double rightNormal =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    RightPart,
                    RightPart
                );

            double leftMirror =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    LeftPart,
                    RightPart
                );

            double rightMirror =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    RightPart,
                    LeftPart
                );

            double sideScore =
                Math.Max(
                    (leftNormal + rightNormal) / 2.0,
                    (leftMirror + rightMirror) / 2.0
                );

            double score =
                fullScore * 0.62 +
                centerScore * 0.18 +
                bottomScore * 0.09 +
                topScore * 0.06 +
                sideScore * 0.05;

            double support =
                (
                    centerScore +
                    topScore +
                    bottomScore +
                    sideScore
                ) / 4.0;

            /*
             * FULL silhouette weak ho to generic flower/top/side
             * similarities high result nahi bana sakti.
             */

            if (fullScore < 34)
                score *= 0.50;
            else if (fullScore < 46)
                score *= 0.68;
            else if (fullScore < 58)
                score *= 0.84;

            if (support < 30)
                score *= 0.80;
            else if (support < 42)
                score *= 0.90;

            if (fullScore >= 82 &&
                centerScore >= 70 &&
                support >= 62)
            {
                score += 3.0;
            }

            return Clamp(
                score,
                0,
                100
            );
        }

        private static double ComparePart(
            byte[] query,
            byte[] candidate,
            int queryRoute,
            int candidateRoute,
            int queryPart,
            int candidatePart)
        {
            if (!CanReadPart(
                    query,
                    queryRoute,
                    queryPart
                ) ||
                !CanReadPart(
                    candidate,
                    candidateRoute,
                    candidatePart
                ))
            {
                return 0;
            }

            double queryWeight =
                ReadDouble(
                    query,
                    queryRoute,
                    queryPart,
                    WeightOffset
                );

            double candidateWeight =
                ReadDouble(
                    candidate,
                    candidateRoute,
                    candidatePart,
                    WeightOffset
                );

            if (queryWeight <= 0 ||
                candidateWeight <= 0)
            {
                return 0;
            }

            ImageFingerprint first =
                ReadFingerprint(
                    query,
                    queryRoute,
                    queryPart
                );

            ImageFingerprint second =
                ReadFingerprint(
                    candidate,
                    candidateRoute,
                    candidatePart
                );

            if (!IsFingerprintValid(first) ||
                !IsFingerprintValid(second))
            {
                return 0;
            }

            double score =
                ImageFingerprint.Compare(
                    first,
                    second
                );

            /*
             * Fingerprint already silhouette, edges, projections,
             * radial structure, aspect aur symmetry compare karta hai.
             * Yahan dobara harsh penalties lagane se exact match crush hota tha.
             * Sirf clearly incompatible aspect par light correction rakhi hai.
             */

            double aspectDifference =
                NormalizedDifference(
                    first.AspectRatio,
                    second.AspectRatio
                );

            if (aspectDifference > 0.62)
                score *= 0.82;
            else if (aspectDifference > 0.46)
                score *= 0.91;

            double weightAgreement =
                Math.Min(
                    queryWeight,
                    candidateWeight
                ) /
                Math.Max(
                    0.0001,
                    Math.Max(
                        queryWeight,
                        candidateWeight
                    )
                );

            score =
                score * 0.96 +
                score * weightAgreement * 0.04;

            return Clamp(
                score,
                0,
                100
            );
        }

        private static List<ImageSegment> BuildPreparedSegments(
            Bitmap source,
            bool strictLineArt)
        {
            var segments =
                new List<ImageSegment>();

            if (source == null ||
                source.Width <= 0 ||
                source.Height <= 0)
            {
                return segments;
            }

            Bitmap normalized = null;

            try
            {
                normalized =
                    NormalizePreparedImage(
                        source,
                        strictLineArt
                    );

                int width = normalized.Width;
                int height = normalized.Height;

                AddPreparedSegment(
                    segments,
                    normalized,
                    "FULL",
                    new Rectangle(
                        0,
                        0,
                        width,
                        height
                    ),
                    1.00
                );

                AddPreparedSegment(
                    segments,
                    normalized,
                    "CENTER",
                    new Rectangle(
                        width * 17 / 100,
                        height * 17 / 100,
                        width * 66 / 100,
                        height * 66 / 100
                    ),
                    0.95
                );

                AddPreparedSegment(
                    segments,
                    normalized,
                    "LEFT",
                    new Rectangle(
                        0,
                        height * 8 / 100,
                        width * 58 / 100,
                        height * 84 / 100
                    ),
                    0.68
                );

                AddPreparedSegment(
                    segments,
                    normalized,
                    "RIGHT",
                    new Rectangle(
                        width * 42 / 100,
                        height * 8 / 100,
                        width * 58 / 100,
                        height * 84 / 100
                    ),
                    0.68
                );

                AddPreparedSegment(
                    segments,
                    normalized,
                    "TOP",
                    new Rectangle(
                        width * 6 / 100,
                        0,
                        width * 88 / 100,
                        height * 48 / 100
                    ),
                    0.74
                );

                AddPreparedSegment(
                    segments,
                    normalized,
                    "BOTTOM",
                    new Rectangle(
                        width * 6 / 100,
                        height * 52 / 100,
                        width * 88 / 100,
                        height * 48 / 100
                    ),
                    0.78
                );
            }
            catch
            {
                DisposeSegments(
                    segments
                );

                segments.Clear();
            }
            finally
            {
                if (normalized != null)
                    normalized.Dispose();
            }

            return segments;
        }

        private static Bitmap NormalizePreparedImage(
            Bitmap source,
            bool strictLineArt)
        {
            Bitmap result =
                new Bitmap(
                    PreparedSize,
                    PreparedSize,
                    PixelFormat.Format24bppRgb
                );

            using (Graphics graphics =
                Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);

                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;

                graphics.SmoothingMode =
                    SmoothingMode.HighQuality;

                graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                int margin = 10;
                int available =
                    PreparedSize -
                    margin * 2;

                double scale =
                    Math.Min(
                        available /
                        (double)Math.Max(
                            1,
                            source.Width
                        ),
                        available /
                        (double)Math.Max(
                            1,
                            source.Height
                        )
                    );

                int drawWidth =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            source.Width *
                            scale
                        )
                    );

                int drawHeight =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            source.Height *
                            scale
                        )
                    );

                int drawX =
                    (
                        PreparedSize -
                        drawWidth
                    ) / 2;

                int drawY =
                    (
                        PreparedSize -
                        drawHeight
                    ) / 2;

                graphics.DrawImage(
                    source,
                    new Rectangle(
                        drawX,
                        drawY,
                        drawWidth,
                        drawHeight
                    ),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel
                );
            }

            if (strictLineArt)
                MakeStrictBlackWhite(result);

            return result;
        }

        private static void MakeStrictBlackWhite(
            Bitmap bitmap)
        {
            long total = 0;

            for (int y = 0;
                 y < bitmap.Height;
                 y++)
            {
                for (int x = 0;
                     x < bitmap.Width;
                     x++)
                {
                    total += Gray(
                        bitmap.GetPixel(
                            x,
                            y
                        )
                    );
                }
            }

            double average =
                total /
                (double)Math.Max(
                    1,
                    bitmap.Width *
                    bitmap.Height
                );

            int threshold =
                ClampInt(
                    (int)(average - 18),
                    90,
                    225
                );

            for (int y = 0;
                 y < bitmap.Height;
                 y++)
            {
                for (int x = 0;
                     x < bitmap.Width;
                     x++)
                {
                    int gray =
                        Gray(
                            bitmap.GetPixel(
                                x,
                                y
                            )
                        );

                    bitmap.SetPixel(
                        x,
                        y,
                        gray < threshold
                            ? Color.Black
                            : Color.White
                    );
                }
            }
        }

        private static void AddPreparedSegment(
            List<ImageSegment> segments,
            Bitmap source,
            string name,
            Rectangle bounds,
            double weight)
        {
            Rectangle imageBounds =
                new Rectangle(
                    0,
                    0,
                    source.Width,
                    source.Height
                );

            bounds.Intersect(imageBounds);

            if (bounds.Width < 10 ||
                bounds.Height < 10)
            {
                return;
            }

            Bitmap crop =
                source.Clone(
                    bounds,
                    PixelFormat.Format24bppRgb
                );

            segments.Add(
                new ImageSegment
                {
                    Name = name,
                    Bitmap = crop,
                    Bounds = bounds,
                    Weight = weight
                }
            );
        }

        private static bool HasUsableSegments(
            List<ImageSegment> segments)
        {
            if (segments == null ||
                segments.Count < MaxParts)
            {
                return false;
            }

            if (segments[FullPart] == null ||
                segments[FullPart].Bitmap == null)
            {
                return false;
            }

            return true;
        }

        private static void WriteRoute(
            byte[] descriptor,
            int route,
            List<ImageSegment> segments)
        {
            if (descriptor == null ||
                segments == null)
            {
                return;
            }

            int count =
                Math.Min(
                    MaxParts,
                    segments.Count
                );

            for (int index = 0;
                 index < count;
                 index++)
            {
                ImageSegment segment =
                    segments[index];

                if (segment == null ||
                    segment.Bitmap == null)
                {
                    continue;
                }

                ImageFingerprint fingerprint =
                    ImageFingerprint.FromBitmap(
                        segment.Bitmap
                    );

                if (fingerprint == null)
                    continue;

                WritePart(
                    descriptor,
                    route,
                    index,
                    fingerprint,
                    segment.Weight
                );
            }
        }

        private static ImageFingerprint ReadFingerprint(
            byte[] descriptor,
            int route,
            int part)
        {
            return new ImageFingerprint
            {
                Hash =
                    ReadUlong(
                        descriptor,
                        route,
                        part,
                        HashOffset
                    ),

                EdgeHash =
                    ReadUlong(
                        descriptor,
                        route,
                        part,
                        EdgeHashOffset
                    ),

                HorizontalHash =
                    ReadUlong(
                        descriptor,
                        route,
                        part,
                        HorizontalHashOffset
                    ),

                VerticalHash =
                    ReadUlong(
                        descriptor,
                        route,
                        part,
                        VerticalHashOffset
                    ),

                RadialHash =
                    ReadUlong(
                        descriptor,
                        route,
                        part,
                        RadialHashOffset
                    ),

                DarkRatio =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        DarkRatioOffset
                    ),

                EdgeRatio =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        EdgeRatioOffset
                    ),

                AspectRatio =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        AspectRatioOffset
                    ),

                CenterX =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        CenterXOffset
                    ),

                CenterY =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        CenterYOffset
                    ),

                BorderRatio =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        BorderRatioOffset
                    ),

                Symmetry =
                    ReadDouble(
                        descriptor,
                        route,
                        part,
                        SymmetryOffset
                    )
            };
        }

        private static void WritePart(
            byte[] descriptor,
            int route,
            int part,
            ImageFingerprint fingerprint,
            double weight)
        {
            if (!CanReadPart(
                    descriptor,
                    route,
                    part
                ))
            {
                return;
            }

            WriteUlong(
                descriptor,
                route,
                part,
                HashOffset,
                fingerprint.Hash
            );

            WriteUlong(
                descriptor,
                route,
                part,
                EdgeHashOffset,
                fingerprint.EdgeHash
            );

            WriteUlong(
                descriptor,
                route,
                part,
                HorizontalHashOffset,
                fingerprint.HorizontalHash
            );

            WriteUlong(
                descriptor,
                route,
                part,
                VerticalHashOffset,
                fingerprint.VerticalHash
            );

            WriteUlong(
                descriptor,
                route,
                part,
                RadialHashOffset,
                fingerprint.RadialHash
            );

            WriteDouble(
                descriptor,
                route,
                part,
                DarkRatioOffset,
                fingerprint.DarkRatio
            );

            WriteDouble(
                descriptor,
                route,
                part,
                EdgeRatioOffset,
                fingerprint.EdgeRatio
            );

            WriteDouble(
                descriptor,
                route,
                part,
                AspectRatioOffset,
                fingerprint.AspectRatio
            );

            WriteDouble(
                descriptor,
                route,
                part,
                CenterXOffset,
                fingerprint.CenterX
            );

            WriteDouble(
                descriptor,
                route,
                part,
                CenterYOffset,
                fingerprint.CenterY
            );

            WriteDouble(
                descriptor,
                route,
                part,
                BorderRatioOffset,
                fingerprint.BorderRatio
            );

            WriteDouble(
                descriptor,
                route,
                part,
                SymmetryOffset,
                fingerprint.Symmetry
            );

            WriteDouble(
                descriptor,
                route,
                part,
                WeightOffset,
                weight
            );
        }

        private static void WriteUlong(
            byte[] descriptor,
            int route,
            int part,
            int relativeOffset,
            ulong value)
        {
            int offset =
                GetPartOffset(
                    route,
                    part
                ) +
                relativeOffset;

            Array.Copy(
                BitConverter.GetBytes(value),
                0,
                descriptor,
                offset,
                8
            );
        }

        private static void WriteDouble(
            byte[] descriptor,
            int route,
            int part,
            int relativeOffset,
            double value)
        {
            int offset =
                GetPartOffset(
                    route,
                    part
                ) +
                relativeOffset;

            Array.Copy(
                BitConverter.GetBytes(value),
                0,
                descriptor,
                offset,
                8
            );
        }

        private static ulong ReadUlong(
            byte[] descriptor,
            int route,
            int part,
            int relativeOffset)
        {
            return BitConverter.ToUInt64(
                descriptor,
                GetPartOffset(
                    route,
                    part
                ) +
                relativeOffset
            );
        }

        private static double ReadDouble(
            byte[] descriptor,
            int route,
            int part,
            int relativeOffset)
        {
            return BitConverter.ToDouble(
                descriptor,
                GetPartOffset(
                    route,
                    part
                ) +
                relativeOffset
            );
        }

        private static int GetPartOffset(
            int route,
            int part)
        {
            return route * RouteSize +
                   part * PartSize;
        }

        private static bool CanReadPart(
            byte[] descriptor,
            int route,
            int part)
        {
            if (descriptor == null ||
                route < 0 ||
                route >= RouteCount ||
                part < 0 ||
                part >= MaxParts)
            {
                return false;
            }

            int start =
                GetPartOffset(
                    route,
                    part
                );

            return start >= 0 &&
                   start + PartSize <=
                   descriptor.Length;
        }

        private static bool IsValidDescriptor(
            byte[] descriptor)
        {
            if (descriptor == null ||
                descriptor.Length < TotalBytes)
            {
                return false;
            }

            return IsRouteValid(
                       descriptor,
                       CleanRoute
                   ) ||
                   IsRouteValid(
                       descriptor,
                       DirectRoute
                   );
        }

        private static bool IsRouteValid(
            byte[] descriptor,
            int route)
        {
            if (!CanReadPart(
                    descriptor,
                    route,
                    FullPart
                ))
            {
                return false;
            }

            double weight =
                ReadDouble(
                    descriptor,
                    route,
                    FullPart,
                    WeightOffset
                );

            if (weight <= 0 ||
                double.IsNaN(weight) ||
                double.IsInfinity(weight))
            {
                return false;
            }

            return IsFingerprintValid(
                ReadFingerprint(
                    descriptor,
                    route,
                    FullPart
                )
            );
        }

        private static bool IsFingerprintValid(
            ImageFingerprint fingerprint)
        {
            if (fingerprint == null)
                return false;

            bool hasSignature =
                fingerprint.Hash != 0 ||
                fingerprint.EdgeHash != 0 ||
                fingerprint.HorizontalHash != 0 ||
                fingerprint.VerticalHash != 0 ||
                fingerprint.RadialHash != 0;

            return hasSignature &&
                   !double.IsNaN(
                       fingerprint.AspectRatio
                   ) &&
                   !double.IsInfinity(
                       fingerprint.AspectRatio
                   ) &&
                   fingerprint.AspectRatio > 0;
        }

        public Size ReadSize(
            string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) ||
                    !File.Exists(imagePath))
                {
                    return new Size(0, 0);
                }

                using (Bitmap bitmap =
                    new Bitmap(imagePath))
                {
                    return new Size(
                        bitmap.Width,
                        bitmap.Height
                    );
                }
            }
            catch
            {
                return new Size(0, 0);
            }
        }

        public Size ReadSize(
            byte[] descriptorBytes)
        {
            return new Size(
                256,
                256
            );
        }

        private static void CountRoute(
            double score,
            ref int strongRoutes,
            ref int mediumRoutes,
            ref int weakRoutes)
        {
            if (score >= 72)
                strongRoutes++;

            if (score >= 56)
                mediumRoutes++;

            if (score < 34)
                weakRoutes++;
        }

        private static double SecondLargest(
            double first,
            double second,
            double third,
            double fourth)
        {
            double[] values =
            {
                first,
                second,
                third,
                fourth
            };

            Array.Sort(values);

            return values[2];
        }

        private static void CountPart(
            double score,
            ref int strongParts,
            ref int mediumParts,
            ref int weakParts)
        {
            if (score >= 74)
                strongParts++;

            if (score >= 56)
                mediumParts++;

            if (score < 40)
                weakParts++;
        }

        private static void DisposeSegments(
            List<ImageSegment> segments)
        {
            if (segments == null)
                return;

            for (int index = 0;
                 index < segments.Count;
                 index++)
            {
                try
                {
                    if (segments[index] != null &&
                        segments[index].Bitmap != null)
                    {
                        segments[index].Bitmap.Dispose();
                    }
                }
                catch
                {
                }
            }
        }

        private static double NormalizedDifference(
            double first,
            double second)
        {
            if (double.IsNaN(first) ||
                double.IsInfinity(first) ||
                double.IsNaN(second) ||
                double.IsInfinity(second))
            {
                return 1;
            }

            double maximum =
                Math.Max(
                    Math.Abs(first),
                    Math.Abs(second)
                );

            if (maximum <= 0.000001)
                return 0;

            return Math.Abs(
                       first -
                       second
                   ) /
                   maximum;
        }

        private static double Distance(
            double firstX,
            double firstY,
            double secondX,
            double secondY)
        {
            double differenceX =
                firstX -
                secondX;

            double differenceY =
                firstY -
                secondY;

            return Math.Sqrt(
                differenceX *
                differenceX +
                differenceY *
                differenceY
            );
        }

        private static double Maximum(
            double first,
            double second,
            double third,
            double fourth)
        {
            return Math.Max(
                Math.Max(first, second),
                Math.Max(third, fourth)
            );
        }

        private static double Minimum(
            double first,
            double second,
            double third,
            double fourth)
        {
            return Math.Min(
                Math.Min(first, second),
                Math.Min(third, fourth)
            );
        }


        /*
         * SECOND-STAGE REAL IMAGE VERIFIER
         *
         * Descriptor comparison sirf shortlist banata hai.
         * Final ranking query crop aur exported PNG ke actual normalized
         * binary shapes ko compare karke hoti hai.
         */

        public sealed class VerificationTemplate : IDisposable
        {
            internal BinaryShape CleanShape;
            internal BinaryShape DirectShape;

            public bool IsValid
            {
                get
                {
                    return CleanShape != null ||
                           DirectShape != null;
                }
            }

            public void Dispose()
            {
                CleanShape = null;
                DirectShape = null;
            }
        }

        internal sealed class BinaryShape
        {
            public bool[,] Mask;
            public bool[,] Edge;
            public int Size;
            public int Foreground;
            public double AspectRatio;
            public double[] Rows;
            public double[] Columns;
        }

        private const int VerificationSize = 96;

        public VerificationTemplate CreateVerificationTemplate(
            string imagePath)
        {
            var template = new VerificationTemplate();

            if (string.IsNullOrEmpty(imagePath) ||
                !File.Exists(imagePath))
            {
                return template;
            }

            ImagePreprocessResult processed = null;

            try
            {
                using (Bitmap original = new Bitmap(imagePath))
                {
                    processed = ImagePreprocessor.Process(original);

                    if (processed != null &&
                        processed.LineArt != null)
                    {
                        template.CleanShape =
                            BuildVerificationShape(
                                processed.LineArt
                            );
                    }

                    if (processed != null &&
                        processed.CroppedOriginal != null)
                    {
                        template.DirectShape =
                            BuildVerificationShape(
                                processed.CroppedOriginal
                            );
                    }

                    if (template.DirectShape == null)
                    {
                        template.DirectShape =
                            BuildVerificationShape(
                                original
                            );
                    }
                }
            }
            catch
            {
                template.Dispose();
            }
            finally
            {
                if (processed != null)
                    processed.Dispose();
            }

            return template;
        }

        public double VerifyImage(
            VerificationTemplate queryTemplate,
            string candidateImagePath)
        {
            if (queryTemplate == null ||
                !queryTemplate.IsValid ||
                string.IsNullOrEmpty(candidateImagePath) ||
                !File.Exists(candidateImagePath))
            {
                return 0;
            }

            using (VerificationTemplate candidate =
                CreateVerificationTemplate(candidateImagePath))
            {
                if (candidate == null ||
                    !candidate.IsValid)
                {
                    return 0;
                }

                var scores = new List<double>();

                AddVerificationScore(
                    scores,
                    queryTemplate.CleanShape,
                    candidate.CleanShape
                );

                AddVerificationScore(
                    scores,
                    queryTemplate.DirectShape,
                    candidate.DirectShape
                );

                AddVerificationScore(
                    scores,
                    queryTemplate.CleanShape,
                    candidate.DirectShape
                );

                AddVerificationScore(
                    scores,
                    queryTemplate.DirectShape,
                    candidate.CleanShape
                );

                if (scores.Count == 0)
                    return 0;

                scores.Sort();

                double best =
                    scores[scores.Count - 1];

                double second =
                    scores.Count >= 2
                        ? scores[scores.Count - 2]
                        : best;

                double finalScore =
                    best * 0.82 +
                    second * 0.18;

                if (best - second > 28)
                    finalScore *= 0.92;

                return Math.Round(
                    Clamp(finalScore, 0, 100),
                    2
                );
            }
        }

        private static void AddVerificationScore(
            List<double> scores,
            BinaryShape first,
            BinaryShape second)
        {
            if (scores == null ||
                first == null ||
                second == null)
            {
                return;
            }

            scores.Add(
                CompareBinaryShapes(
                    first,
                    second
                )
            );
        }

        private static BinaryShape BuildVerificationShape(
            Bitmap source)
        {
            if (source == null ||
                source.Width <= 0 ||
                source.Height <= 0)
            {
                return null;
            }

            try
            {
                using (Bitmap square =
                    new Bitmap(
                        VerificationSize,
                        VerificationSize,
                        PixelFormat.Format24bppRgb
                    ))
                {
                    using (Graphics graphics =
                        Graphics.FromImage(square))
                    {
                        graphics.Clear(Color.White);
                        graphics.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode =
                            SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode =
                            PixelOffsetMode.HighQuality;

                        int margin = 4;
                        int available =
                            VerificationSize - margin * 2;

                        double scale = Math.Min(
                            available /
                            (double)Math.Max(1, source.Width),
                            available /
                            (double)Math.Max(1, source.Height)
                        );

                        int width = Math.Max(
                            1,
                            (int)Math.Round(
                                source.Width * scale
                            )
                        );

                        int height = Math.Max(
                            1,
                            (int)Math.Round(
                                source.Height * scale
                            )
                        );

                        int left =
                            (VerificationSize - width) / 2;

                        int top =
                            (VerificationSize - height) / 2;

                        graphics.DrawImage(
                            source,
                            new Rectangle(
                                left,
                                top,
                                width,
                                height
                            ),
                            0,
                            0,
                            source.Width,
                            source.Height,
                            GraphicsUnit.Pixel
                        );
                    }

                    bool[,] rawMask =
                        BuildVerificationMask(square);

                    Rectangle bounds =
                        FindVerificationBounds(rawMask);

                    if (bounds.Width < 4 ||
                        bounds.Height < 4)
                    {
                        return null;
                    }

                    bool[,] centered =
                        CenterVerificationMask(
                            rawMask,
                            bounds
                        );

                    return CreateBinaryShape(centered);
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool[,] BuildVerificationMask(
            Bitmap bitmap)
        {
            int[] histogram = new int[256];
            long totalIntensity = 0;
            int totalPixels =
                bitmap.Width * bitmap.Height;

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
            int threshold = 170;

            for (int value = 0; value < 256; value++)
            {
                backgroundCount += histogram[value];

                if (backgroundCount == 0)
                    continue;

                int foregroundCount =
                    totalPixels - backgroundCount;

                if (foregroundCount == 0)
                    break;

                backgroundIntensity +=
                    (long)value * histogram[value];

                double firstMean =
                    backgroundIntensity /
                    (double)backgroundCount;

                double secondMean =
                    (totalIntensity - backgroundIntensity) /
                    (double)foregroundCount;

                double difference =
                    firstMean - secondMean;

                double variance =
                    backgroundCount *
                    (double)foregroundCount *
                    difference * difference;

                if (variance > bestVariance)
                {
                    bestVariance = variance;
                    threshold = value;
                }
            }

            threshold = ClampInt(threshold, 65, 225);

            bool[,] dark =
                new bool[VerificationSize, VerificationSize];

            bool[,] light =
                new bool[VerificationSize, VerificationSize];

            int darkCount = 0;
            int lightCount = 0;

            for (int y = 0; y < VerificationSize; y++)
            {
                for (int x = 0; x < VerificationSize; x++)
                {
                    int gray = Gray(bitmap.GetPixel(x, y));

                    dark[x, y] = gray <= threshold;
                    light[x, y] = gray >= 255 - threshold;

                    if (dark[x, y]) darkCount++;
                    if (light[x, y]) lightCount++;
                }
            }

            if (darkCount >
                    VerificationSize *
                    VerificationSize * 0.72 &&
                lightCount > 25)
            {
                return light;
            }

            return dark;
        }

        private static Rectangle FindVerificationBounds(
            bool[,] mask)
        {
            int minX = VerificationSize;
            int minY = VerificationSize;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < VerificationSize; y++)
            {
                for (int x = 0; x < VerificationSize; x++)
                {
                    if (!mask[x, y])
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
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

        private static bool[,] CenterVerificationMask(
            bool[,] source,
            Rectangle bounds)
        {
            bool[,] result =
                new bool[VerificationSize, VerificationSize];

            double scale = Math.Min(
                (VerificationSize - 8.0) /
                Math.Max(1, bounds.Width),
                (VerificationSize - 8.0) /
                Math.Max(1, bounds.Height)
            );

            int width = Math.Max(
                1,
                (int)Math.Round(bounds.Width * scale)
            );

            int height = Math.Max(
                1,
                (int)Math.Round(bounds.Height * scale)
            );

            int left =
                (VerificationSize - width) / 2;

            int top =
                (VerificationSize - height) / 2;

            for (int y = 0; y < height; y++)
            {
                int sourceY = ClampInt(
                    bounds.Top +
                    (int)(y /
                    Math.Max(0.0001, scale)),
                    bounds.Top,
                    bounds.Bottom - 1
                );

                for (int x = 0; x < width; x++)
                {
                    int sourceX = ClampInt(
                        bounds.Left +
                        (int)(x /
                        Math.Max(0.0001, scale)),
                        bounds.Left,
                        bounds.Right - 1
                    );

                    result[left + x, top + y] =
                        source[sourceX, sourceY];
                }
            }

            return result;
        }

        private static BinaryShape CreateBinaryShape(
            bool[,] mask)
        {
            var shape = new BinaryShape
            {
                Mask = mask,
                Edge = new bool[
                    VerificationSize,
                    VerificationSize
                ],
                Size = VerificationSize,
                Rows = new double[VerificationSize],
                Columns = new double[VerificationSize]
            };

            int minX = VerificationSize;
            int minY = VerificationSize;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < VerificationSize; y++)
            {
                for (int x = 0; x < VerificationSize; x++)
                {
                    if (!mask[x, y])
                        continue;

                    shape.Foreground++;
                    shape.Rows[y]++;
                    shape.Columns[x]++;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;

                    if (x == 0 ||
                        y == 0 ||
                        x == VerificationSize - 1 ||
                        y == VerificationSize - 1 ||
                        !mask[Math.Max(0, x - 1), y] ||
                        !mask[Math.Min(
                            VerificationSize - 1,
                            x + 1
                        ), y] ||
                        !mask[x, Math.Max(0, y - 1)] ||
                        !mask[x, Math.Min(
                            VerificationSize - 1,
                            y + 1
                        )])
                    {
                        shape.Edge[x, y] = true;
                    }
                }
            }

            for (int i = 0; i < VerificationSize; i++)
            {
                shape.Rows[i] /= VerificationSize;
                shape.Columns[i] /= VerificationSize;
            }

            if (maxX >= minX && maxY >= minY)
            {
                shape.AspectRatio =
                    (maxX - minX + 1) /
                    (double)Math.Max(
                        1,
                        maxY - minY + 1
                    );
            }

            if (shape.Foreground < 10)
                return null;

            return shape;
        }

        private static double CompareBinaryShapes(
            BinaryShape query,
            BinaryShape candidate)
        {
            double best = 0;

            int[] angles = { -6, 0, 6 };

            for (int mirrorIndex = 0;
                 mirrorIndex < 2;
                 mirrorIndex++)
            {
                bool mirror = mirrorIndex == 1;

                for (int angleIndex = 0;
                     angleIndex < angles.Length;
                     angleIndex++)
                {
                    BinaryShape transformed =
                        TransformBinaryShape(
                            candidate,
                            angles[angleIndex],
                            mirror
                        );

                    if (transformed == null)
                        continue;

                    for (int shiftY = -3;
                         shiftY <= 3;
                         shiftY += 3)
                    {
                        for (int shiftX = -3;
                             shiftX <= 3;
                             shiftX += 3)
                        {
                            double score =
                                CompareBinaryAtShift(
                                    query,
                                    transformed,
                                    shiftX,
                                    shiftY
                                );

                            if (score > best)
                                best = score;
                        }
                    }
                }
            }

            return Clamp(best, 0, 100);
        }

        private static BinaryShape TransformBinaryShape(
            BinaryShape source,
            int angleDegrees,
            bool mirror)
        {
            if (source == null || source.Mask == null)
                return null;

            bool[,] transformed =
                new bool[VerificationSize, VerificationSize];

            double angle =
                angleDegrees * Math.PI / 180.0;

            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            double center =
                (VerificationSize - 1) / 2.0;

            for (int y = 0; y < VerificationSize; y++)
            {
                for (int x = 0; x < VerificationSize; x++)
                {
                    double targetX = x - center;
                    double targetY = y - center;

                    double sourceX =
                        targetX * cosine +
                        targetY * sine;

                    double sourceY =
                        -targetX * sine +
                        targetY * cosine;

                    if (mirror)
                        sourceX = -sourceX;

                    int readX =
                        (int)Math.Round(sourceX + center);

                    int readY =
                        (int)Math.Round(sourceY + center);

                    if (readX >= 0 &&
                        readX < VerificationSize &&
                        readY >= 0 &&
                        readY < VerificationSize)
                    {
                        transformed[x, y] =
                            source.Mask[readX, readY];
                    }
                }
            }

            return CreateBinaryShape(transformed);
        }

        private static double CompareBinaryAtShift(
            BinaryShape first,
            BinaryShape second,
            int shiftX,
            int shiftY)
        {
            int intersection = 0;
            int union = 0;
            int firstCount = 0;
            int secondCount = 0;
            int edgeIntersection = 0;
            int firstEdgeCount = 0;
            int secondEdgeCount = 0;

            for (int y = 0; y < VerificationSize; y++)
            {
                int secondY = y - shiftY;

                for (int x = 0; x < VerificationSize; x++)
                {
                    int secondX = x - shiftX;

                    bool firstPixel =
                        first.Mask[x, y];

                    bool secondPixel =
                        secondX >= 0 &&
                        secondX < VerificationSize &&
                        secondY >= 0 &&
                        secondY < VerificationSize &&
                        second.Mask[secondX, secondY];

                    if (firstPixel) firstCount++;
                    if (secondPixel) secondCount++;

                    if (firstPixel && secondPixel)
                        intersection++;

                    if (firstPixel || secondPixel)
                        union++;

                    bool firstEdge =
                        first.Edge[x, y];

                    bool secondEdge =
                        secondX >= 0 &&
                        secondX < VerificationSize &&
                        secondY >= 0 &&
                        secondY < VerificationSize &&
                        second.Edge[secondX, secondY];

                    if (firstEdge) firstEdgeCount++;
                    if (secondEdge) secondEdgeCount++;

                    if (firstEdge && secondEdge)
                        edgeIntersection++;
                }
            }

            double dice =
                2.0 * intersection /
                Math.Max(1.0, firstCount + secondCount);

            double iou =
                intersection /
                (double)Math.Max(1, union);

            double edgeDice =
                2.0 * edgeIntersection /
                Math.Max(
                    1.0,
                    firstEdgeCount + secondEdgeCount
                );

            double rowSimilarity =
                ProfileSimilarity(
                    first.Rows,
                    second.Rows,
                    shiftY
                );

            double columnSimilarity =
                ProfileSimilarity(
                    first.Columns,
                    second.Columns,
                    shiftX
                );

            double profile =
                (rowSimilarity + columnSimilarity) / 2.0;

            double aspect =
                Math.Min(
                    first.AspectRatio,
                    second.AspectRatio
                ) /
                Math.Max(
                    0.0001,
                    Math.Max(
                        first.AspectRatio,
                        second.AspectRatio
                    )
                );

            double score =
                dice * 0.34 +
                iou * 0.25 +
                edgeDice * 0.20 +
                profile * 0.16 +
                aspect * 0.05;

            if (dice < 0.32)
                score *= 0.68;
            else if (dice < 0.46)
                score *= 0.84;

            if (edgeDice < 0.22)
                score *= 0.80;

            return score * 100.0;
        }

        private static double ProfileSimilarity(
            double[] first,
            double[] second,
            int shift)
        {
            if (first == null || second == null)
                return 0;

            double difference = 0;
            double maximum = 0;

            for (int index = 0;
                 index < VerificationSize;
                 index++)
            {
                int secondIndex = index - shift;

                double secondValue =
                    secondIndex >= 0 &&
                    secondIndex < VerificationSize
                        ? second[secondIndex]
                        : 0;

                difference += Math.Abs(
                    first[index] - secondValue
                );

                maximum += Math.Max(
                    first[index],
                    secondValue
                );
            }

            if (maximum <= 0.000001)
                return 1.0;

            return Clamp(
                1.0 - difference / maximum,
                0,
                1
            );
        }

        private static int Gray(
            Color color)
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

        private static double Clamp(
            double value,
            double minimum,
            double maximum)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                return minimum;
            }

            if (value < minimum)
                return minimum;

            if (value > maximum)
                return maximum;

            return value;
        }
    }
}
