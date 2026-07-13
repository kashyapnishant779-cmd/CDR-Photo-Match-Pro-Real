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

            double cleanScore =
                CompareRoute(
                    query,
                    candidate,
                    CleanRoute,
                    CleanRoute
                );

            double directScore =
                CompareRoute(
                    query,
                    candidate,
                    DirectRoute,
                    DirectRoute
                );

            double directToCleanScore =
                CompareRoute(
                    query,
                    candidate,
                    DirectRoute,
                    CleanRoute
                );

            double cleanToDirectScore =
                CompareRoute(
                    query,
                    candidate,
                    CleanRoute,
                    DirectRoute
                );

            /*
             * Purane code me Math.Max route ko bahut importance mil rahi thi.
             * Isse ek accidental silhouette match poori ranking ko upar le jaata tha.
             *
             * Ab:
             * - same-route agreement sabse important
             * - cross-route sirf support evidence
             * - ek route high aur baaki weak ho to strong penalty
             */

            double sameRouteAverage =
                (cleanScore + directScore) / 2.0;

            double sameRouteMinimum =
                Math.Min(cleanScore, directScore);

            double crossAverage =
                (
                    directToCleanScore +
                    cleanToDirectScore
                ) / 2.0;

            double crossMinimum =
                Math.Min(
                    directToCleanScore,
                    cleanToDirectScore
                );

            double bestRoute =
                Maximum(
                    cleanScore,
                    directScore,
                    directToCleanScore,
                    cleanToDirectScore
                );

            double finalScore =
                sameRouteAverage * 0.48 +
                sameRouteMinimum * 0.24 +
                crossAverage * 0.18 +
                crossMinimum * 0.10;

            /*
             * False-positive killer:
             * sirf ek route high ho aur baaki agree na karein.
             */

            int routesAbove55 = 0;
            int routesAbove65 = 0;
            int routesBelow30 = 0;

            CountRoute(cleanScore,
                ref routesAbove55,
                ref routesAbove65,
                ref routesBelow30);

            CountRoute(directScore,
                ref routesAbove55,
                ref routesAbove65,
                ref routesBelow30);

            CountRoute(directToCleanScore,
                ref routesAbove55,
                ref routesAbove65,
                ref routesBelow30);

            CountRoute(cleanToDirectScore,
                ref routesAbove55,
                ref routesAbove65,
                ref routesBelow30);

            if (routesAbove55 == 0)
            {
                finalScore *= 0.42;
            }
            else if (routesAbove55 == 1)
            {
                finalScore *= 0.62;
            }
            else if (routesAbove55 == 2)
            {
                finalScore *= 0.80;
            }

            if (routesBelow30 >= 3)
            {
                finalScore *= 0.58;
            }
            else if (routesBelow30 == 2)
            {
                finalScore *= 0.74;
            }

            double routeSpread =
                bestRoute -
                Minimum(
                    cleanScore,
                    directScore,
                    directToCleanScore,
                    cleanToDirectScore
                );

            if (routeSpread > 46)
            {
                finalScore *= 0.58;
            }
            else if (routeSpread > 34)
            {
                finalScore *= 0.73;
            }
            else if (routeSpread > 24)
            {
                finalScore *= 0.86;
            }

            /*
             * Same-route evidence weak ho to customer photo aur
             * CDR silhouette ko galat match hone se roko.
             */

            if (sameRouteMinimum < 22)
            {
                finalScore *= 0.55;
            }
            else if (sameRouteMinimum < 34)
            {
                finalScore *= 0.72;
            }
            else if (sameRouteMinimum < 46)
            {
                finalScore *= 0.88;
            }

            /*
             * High percentage sirf consistent multi-route match ko.
             */

            if (routesAbove65 < 2 &&
                finalScore > 62)
            {
                finalScore = 62;
            }

            if (routesAbove65 < 3 &&
                finalScore > 76)
            {
                finalScore = 76;
            }

            if (routesAbove65 < 4 &&
                finalScore > 88)
            {
                finalScore = 88;
            }

            if (cleanScore >= 80 &&
                directScore >= 72 &&
                crossAverage >= 68 &&
                routeSpread <= 20)
            {
                finalScore += 3.0;
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

            double normalLeft =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    LeftPart,
                    LeftPart
                );

            double normalRight =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    RightPart,
                    RightPart
                );

            double mirrorLeft =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    LeftPart,
                    RightPart
                );

            double mirrorRight =
                ComparePart(
                    query,
                    candidate,
                    queryRoute,
                    candidateRoute,
                    RightPart,
                    LeftPart
                );

            double normalSideScore =
                (
                    normalLeft +
                    normalRight
                ) / 2.0;

            double mirrorSideScore =
                (
                    mirrorLeft +
                    mirrorRight
                ) / 2.0;

            double sideScore =
                Math.Max(
                    normalSideScore,
                    mirrorSideScore
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

            double verticalScore =
                (
                    topScore +
                    bottomScore
                ) / 2.0;

            double finalScore =
                fullScore * 0.30 +
                centerScore * 0.34 +
                sideScore * 0.18 +
                verticalScore * 0.18;

            int strongParts = 0;
            int mediumParts = 0;
            int weakParts = 0;

            CountPart(
                fullScore,
                ref strongParts,
                ref mediumParts,
                ref weakParts
            );

            CountPart(
                centerScore,
                ref strongParts,
                ref mediumParts,
                ref weakParts
            );

            if (mirrorSideScore >
                normalSideScore + 3)
            {
                CountPart(
                    mirrorLeft,
                    ref strongParts,
                    ref mediumParts,
                    ref weakParts
                );

                CountPart(
                    mirrorRight,
                    ref strongParts,
                    ref mediumParts,
                    ref weakParts
                );
            }
            else
            {
                CountPart(
                    normalLeft,
                    ref strongParts,
                    ref mediumParts,
                    ref weakParts
                );

                CountPart(
                    normalRight,
                    ref strongParts,
                    ref mediumParts,
                    ref weakParts
                );
            }

            CountPart(
                topScore,
                ref strongParts,
                ref mediumParts,
                ref weakParts
            );

            CountPart(
                bottomScore,
                ref strongParts,
                ref mediumParts,
                ref weakParts
            );

            /*
             * Main silhouette weak ho to random ornamental design reject.
             */

            if (fullScore < 34)
            {
                finalScore *= 0.40;
            }
            else if (fullScore < 46)
            {
                finalScore *= 0.62;
            }
            else if (fullScore < 58)
            {
                finalScore *= 0.80;
            }

            /*
             * Center design identity.
             */

            if (centerScore < 34)
            {
                finalScore *= 0.38;
            }
            else if (centerScore < 47)
            {
                finalScore *= 0.60;
            }
            else if (centerScore < 60)
            {
                finalScore *= 0.79;
            }

            /*
             * Sirf ek-do matching parts se high score nahi.
             */

            if (strongParts == 0)
            {
                finalScore *= 0.56;
            }
            else if (strongParts == 1)
            {
                finalScore *= 0.72;
            }
            else if (strongParts == 2)
            {
                finalScore *= 0.87;
            }

            if (mediumParts <= 1)
            {
                finalScore *= 0.66;
            }
            else if (mediumParts == 2)
            {
                finalScore *= 0.82;
            }

            if (weakParts >= 4)
            {
                finalScore *= 0.70;
            }
            else if (weakParts == 3)
            {
                finalScore *= 0.84;
            }

            double maximum =
                Maximum(
                    fullScore,
                    centerScore,
                    sideScore,
                    verticalScore
                );

            double minimum =
                Minimum(
                    fullScore,
                    centerScore,
                    sideScore,
                    verticalScore
                );

            double spread =
                maximum -
                minimum;

            if (spread > 48)
            {
                finalScore *= 0.72;
            }
            else if (spread > 36)
            {
                finalScore *= 0.83;
            }
            else if (spread > 27)
            {
                finalScore *= 0.92;
            }

            if (sideScore < 25)
            {
                finalScore *= 0.73;
            }
            else if (sideScore < 38)
            {
                finalScore *= 0.87;
            }

            if (strongParts < 3 &&
                finalScore > 76)
            {
                finalScore = 76;
            }

            if (strongParts < 4 &&
                finalScore > 85)
            {
                finalScore = 85;
            }

            if (strongParts < 5 &&
                finalScore > 92)
            {
                finalScore = 92;
            }

            if (fullScore >= 83 &&
                centerScore >= 81 &&
                sideScore >= 70 &&
                verticalScore >= 66 &&
                strongParts >= 4)
            {
                finalScore += 3;
            }

            if (fullScore >= 90 &&
                centerScore >= 87 &&
                sideScore >= 79 &&
                verticalScore >= 74 &&
                strongParts >= 5)
            {
                finalScore += 3;
            }

            return Clamp(
                finalScore,
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

            double aspectDifference =
                NormalizedDifference(
                    first.AspectRatio,
                    second.AspectRatio
                );

            double darkDifference =
                Math.Abs(
                    first.DarkRatio -
                    second.DarkRatio
                );

            double edgeDifference =
                Math.Abs(
                    first.EdgeRatio -
                    second.EdgeRatio
                );

            double borderDifference =
                Math.Abs(
                    first.BorderRatio -
                    second.BorderRatio
                );

            double symmetryDifference =
                Math.Abs(
                    first.Symmetry -
                    second.Symmetry
                );

            double centerDistance =
                Distance(
                    first.CenterX,
                    first.CenterY,
                    second.CenterX,
                    second.CenterY
                );

            if (aspectDifference > 0.50)
            {
                score *= 0.66;
            }
            else if (aspectDifference > 0.35)
            {
                score *= 0.79;
            }
            else if (aspectDifference > 0.23)
            {
                score *= 0.90;
            }

            if (darkDifference > 0.30)
            {
                score *= 0.70;
            }
            else if (darkDifference > 0.20)
            {
                score *= 0.83;
            }
            else if (darkDifference > 0.13)
            {
                score *= 0.93;
            }

            if (edgeDifference > 0.23)
            {
                score *= 0.70;
            }
            else if (edgeDifference > 0.15)
            {
                score *= 0.84;
            }
            else if (edgeDifference > 0.10)
            {
                score *= 0.94;
            }

            if (borderDifference > 0.38)
            {
                score *= 0.78;
            }
            else if (borderDifference > 0.25)
            {
                score *= 0.89;
            }

            if (symmetryDifference > 0.44)
            {
                score *= 0.84;
            }
            else if (symmetryDifference > 0.30)
            {
                score *= 0.93;
            }

            if (centerDistance > 0.36)
            {
                score *= 0.72;
            }
            else if (centerDistance > 0.24)
            {
                score *= 0.85;
            }

            /*
             * Internal-detail disagreement:
             * bold silhouette aur detailed pendant ka occupancy kabhi-kabhi
             * similar hota hai, lekin edge/radial/projection agree nahi karte.
             */

            double occupancySimilarity =
                1.0 -
                Hamming(
                    first.Hash ^
                    second.Hash
                ) / 64.0;

            double edgeHashSimilarity =
                1.0 -
                Hamming(
                    first.EdgeHash ^
                    second.EdgeHash
                ) / 64.0;

            double horizontalSimilarity =
                1.0 -
                Hamming(
                    first.HorizontalHash ^
                    second.HorizontalHash
                ) / 64.0;

            double verticalSimilarity =
                1.0 -
                Hamming(
                    first.VerticalHash ^
                    second.VerticalHash
                ) / 64.0;

            double radialSimilarity =
                1.0 -
                Hamming(
                    first.RadialHash ^
                    second.RadialHash
                ) / 64.0;

            int structuralAgreements = 0;

            if (occupancySimilarity >= 0.66)
                structuralAgreements++;

            if (edgeHashSimilarity >= 0.64)
                structuralAgreements++;

            if (horizontalSimilarity >= 0.64)
                structuralAgreements++;

            if (verticalSimilarity >= 0.64)
                structuralAgreements++;

            if (radialSimilarity >= 0.64)
                structuralAgreements++;

            if (structuralAgreements <= 1)
            {
                score *= 0.42;
            }
            else if (structuralAgreements == 2)
            {
                score *= 0.62;
            }
            else if (structuralAgreements == 3)
            {
                score *= 0.82;
            }

            if (occupancySimilarity >= 0.72 &&
                edgeHashSimilarity < 0.48)
            {
                score *= 0.58;
            }

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
            ref int routesAbove55,
            ref int routesAbove65,
            ref int routesBelow30)
        {
            if (score >= 55)
                routesAbove55++;

            if (score >= 65)
                routesAbove65++;

            if (score < 30)
                routesBelow30++;
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


        private static int Hamming(
            ulong value)
        {
            int count = 0;

            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
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
