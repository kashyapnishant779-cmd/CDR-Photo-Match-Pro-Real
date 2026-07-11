using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        /*
         * Exact ImageSegmenter order:
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
         * Har part ka descriptor:
         *
         *  0  = Hash                 ulong   8 bytes
         *  8  = EdgeHash             ulong   8 bytes
         * 16  = HorizontalHash       ulong   8 bytes
         * 24  = VerticalHash         ulong   8 bytes
         * 32  = RadialHash           ulong   8 bytes
         *
         * 40  = DarkRatio            double  8 bytes
         * 48  = EdgeRatio            double  8 bytes
         * 56  = AspectRatio          double  8 bytes
         * 64  = CenterX              double  8 bytes
         * 72  = CenterY              double  8 bytes
         * 80  = BorderRatio          double  8 bytes
         * 88  = Symmetry             double  8 bytes
         * 96  = Segment Weight       double  8 bytes
         *
         * Total = 104 bytes per part
         */

        private const int PartSize = 104;
        private const int TotalBytes =
            MaxParts * PartSize;

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

        public static double Compare(
            string queryImagePath,
            string databaseImagePath)
        {
            ImageMatcher matcher =
                new ImageMatcher();

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

            List<ImageSegment> segments = null;

            try
            {
                using (Bitmap bitmap =
                    new Bitmap(imagePath))
                {
                    segments =
                        ImageSegmenter.Segment(
                            bitmap
                        );

                    if (segments == null)
                        return descriptor;

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
                            index,
                            fingerprint,
                            segment.Weight
                        );
                    }
                }
            }
            catch
            {
                return new byte[TotalBytes];
            }
            finally
            {
                DisposeSegments(segments);
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

            double fullScore =
                ComparePart(
                    query,
                    candidate,
                    FullPart,
                    FullPart
                );

            double centerScore =
                ComparePart(
                    query,
                    candidate,
                    CenterPart,
                    CenterPart
                );

            double normalLeftScore =
                ComparePart(
                    query,
                    candidate,
                    LeftPart,
                    LeftPart
                );

            double normalRightScore =
                ComparePart(
                    query,
                    candidate,
                    RightPart,
                    RightPart
                );

            /*
             * Customer photo mirror ho sakti hai.
             * Isliye LEFT ko RIGHT aur RIGHT ko LEFT se bhi compare karte hain.
             */

            double mirrorLeftScore =
                ComparePart(
                    query,
                    candidate,
                    LeftPart,
                    RightPart
                );

            double mirrorRightScore =
                ComparePart(
                    query,
                    candidate,
                    RightPart,
                    LeftPart
                );

            double normalSideScore =
                (
                    normalLeftScore +
                    normalRightScore
                ) / 2.0;

            double mirrorSideScore =
                (
                    mirrorLeftScore +
                    mirrorRightScore
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
                    TopPart,
                    TopPart
                );

            double bottomScore =
                ComparePart(
                    query,
                    candidate,
                    BottomPart,
                    BottomPart
                );

            double verticalScore =
                (
                    topScore +
                    bottomScore
                ) / 2.0;

            /*
             * Jewellery matching priority:
             *
             * FULL   = complete silhouette
             * CENTER = main design identity
             * SIDES  = side structure/details
             * TOP/BOTTOM = vertical structure
             */

            double finalScore =
                fullScore * 0.36 +
                centerScore * 0.28 +
                sideScore * 0.20 +
                verticalScore * 0.16;

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

            CountPart(
                normalLeftScore,
                ref strongParts,
                ref mediumParts,
                ref weakParts
            );

            CountPart(
                normalRightScore,
                ref strongParts,
                ref mediumParts,
                ref weakParts
            );

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
             * Agar mirrored sides better hain to strong/medium count me
             * mirrored side evidence bhi consider karna hai.
             */

            if (mirrorSideScore >
                normalSideScore + 4.0)
            {
                strongParts = 0;
                mediumParts = 0;
                weakParts = 0;

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

                CountPart(
                    mirrorLeftScore,
                    ref strongParts,
                    ref mediumParts,
                    ref weakParts
                );

                CountPart(
                    mirrorRightScore,
                    ref strongParts,
                    ref mediumParts,
                    ref weakParts
                );

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
            }

            /*
             * Main silhouette weak ho to unrelated result ko control karo.
             */

            if (fullScore < 34)
            {
                finalScore *= 0.58;
            }
            else if (fullScore < 44)
            {
                finalScore *= 0.72;
            }
            else if (fullScore < 54)
            {
                finalScore *= 0.86;
            }

            /*
             * CENTER jewellery identity ka important evidence hai.
             */

            if (centerScore < 32)
            {
                finalScore *= 0.60;
            }
            else if (centerScore < 43)
            {
                finalScore *= 0.74;
            }
            else if (centerScore < 54)
            {
                finalScore *= 0.88;
            }

            /*
             * Sirf ek ya do similar parts ki wajah se high match mat do.
             */

            if (strongParts == 0)
            {
                finalScore *= 0.62;
            }
            else if (strongParts == 1)
            {
                finalScore *= 0.76;
            }
            else if (strongParts == 2)
            {
                finalScore *= 0.89;
            }

            if (mediumParts <= 1)
            {
                finalScore *= 0.70;
            }
            else if (mediumParts == 2)
            {
                finalScore *= 0.84;
            }

            if (weakParts >= 4)
            {
                finalScore *= 0.74;
            }
            else if (weakParts == 3)
            {
                finalScore *= 0.86;
            }

            /*
             * Parts ke scores me bahut disagreement ho to design random
             * ya partial similarity ho sakti hai.
             */

            double maximumScore =
                Maximum(
                    fullScore,
                    centerScore,
                    sideScore,
                    verticalScore
                );

            double minimumScore =
                Minimum(
                    fullScore,
                    centerScore,
                    sideScore,
                    verticalScore
                );

            double scoreSpread =
                maximumScore -
                minimumScore;

            if (scoreSpread > 48)
            {
                finalScore *= 0.74;
            }
            else if (scoreSpread > 36)
            {
                finalScore *= 0.84;
            }
            else if (scoreSpread > 27)
            {
                finalScore *= 0.92;
            }

            /*
             * Full aur center ke beech bahut difference ho to exact design
             * hone ka evidence kam hai.
             */

            double mainDifference =
                Math.Abs(
                    fullScore -
                    centerScore
                );

            if (mainDifference > 38)
            {
                finalScore *= 0.78;
            }
            else if (mainDifference > 27)
            {
                finalScore *= 0.88;
            }

            /*
             * Side structure bilkul weak ho to ring/pendant silhouette ke
             * random similar center ko high result na mile.
             */

            if (sideScore < 28)
            {
                finalScore *= 0.76;
            }
            else if (sideScore < 40)
            {
                finalScore *= 0.88;
            }

            /*
             * Conservative score caps:
             *
             * 90%+ ke liye multiple independent parts strong hone chahiye.
             */

            if (strongParts < 3 &&
                finalScore > 79)
            {
                finalScore = 79;
            }

            if (strongParts < 4 &&
                finalScore > 86)
            {
                finalScore = 86;
            }

            if (strongParts < 5 &&
                finalScore > 92)
            {
                finalScore = 92;
            }

            /*
             * Same/exact design boost sirf tab:
             * FULL + CENTER + multiple parts genuinely strong hon.
             */

            if (fullScore >= 82 &&
                centerScore >= 82 &&
                sideScore >= 72 &&
                verticalScore >= 68 &&
                strongParts >= 4)
            {
                finalScore += 3.5;
            }

            if (fullScore >= 89 &&
                centerScore >= 87 &&
                sideScore >= 80 &&
                verticalScore >= 76 &&
                strongParts >= 5)
            {
                finalScore += 3.0;
            }

            if (fullScore >= 94 &&
                centerScore >= 92 &&
                sideScore >= 86 &&
                verticalScore >= 82 &&
                strongParts >= 6)
            {
                finalScore += 2.0;
            }

            finalScore =
                Clamp(
                    finalScore,
                    0,
                    100
                );

            return Math.Round(
                finalScore,
                2
            );
        }

        public Size ReadSize(string imagePath)
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

        private static double ComparePart(
            byte[] query,
            byte[] candidate,
            int queryPart,
            int candidatePart)
        {
            if (!CanReadPart(
                    query,
                    queryPart
                ) ||
                !CanReadPart(
                    candidate,
                    candidatePart
                ))
            {
                return 0;
            }

            double queryWeight =
                ReadDouble(
                    query,
                    queryPart,
                    WeightOffset
                );

            double candidateWeight =
                ReadDouble(
                    candidate,
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
                    queryPart
                );

            ImageFingerprint second =
                ReadFingerprint(
                    candidate,
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
             * Segment weight very different ho to query/candidate crop ka
             * meaningful area alag ho sakta hai.
             */

            double weightDifference =
                NormalizedDifference(
                    queryWeight,
                    candidateWeight
                );

            if (weightDifference > 0.60)
            {
                score *= 0.76;
            }
            else if (weightDifference > 0.42)
            {
                score *= 0.86;
            }
            else if (weightDifference > 0.26)
            {
                score *= 0.94;
            }

            /*
             * Shape geometry ki extra strict checking.
             */

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

            if (aspectDifference > 0.48)
            {
                score *= 0.66;
            }
            else if (aspectDifference > 0.34)
            {
                score *= 0.78;
            }
            else if (aspectDifference > 0.22)
            {
                score *= 0.89;
            }

            if (darkDifference > 0.28)
            {
                score *= 0.68;
            }
            else if (darkDifference > 0.19)
            {
                score *= 0.82;
            }
            else if (darkDifference > 0.12)
            {
                score *= 0.92;
            }

            if (edgeDifference > 0.21)
            {
                score *= 0.70;
            }
            else if (edgeDifference > 0.14)
            {
                score *= 0.84;
            }
            else if (edgeDifference > 0.09)
            {
                score *= 0.93;
            }

            if (borderDifference > 0.36)
            {
                score *= 0.78;
            }
            else if (borderDifference > 0.24)
            {
                score *= 0.89;
            }

            if (symmetryDifference > 0.42)
            {
                score *= 0.84;
            }
            else if (symmetryDifference > 0.28)
            {
                score *= 0.93;
            }

            if (centerDistance > 0.34)
            {
                score *= 0.78;
            }
            else if (centerDistance > 0.23)
            {
                score *= 0.89;
            }

            return Clamp(
                score,
                0,
                100
            );
        }

        private static ImageFingerprint ReadFingerprint(
            byte[] descriptor,
            int part)
        {
            return new ImageFingerprint
            {
                Hash =
                    ReadUlong(
                        descriptor,
                        part,
                        HashOffset
                    ),

                EdgeHash =
                    ReadUlong(
                        descriptor,
                        part,
                        EdgeHashOffset
                    ),

                HorizontalHash =
                    ReadUlong(
                        descriptor,
                        part,
                        HorizontalHashOffset
                    ),

                VerticalHash =
                    ReadUlong(
                        descriptor,
                        part,
                        VerticalHashOffset
                    ),

                RadialHash =
                    ReadUlong(
                        descriptor,
                        part,
                        RadialHashOffset
                    ),

                DarkRatio =
                    ReadDouble(
                        descriptor,
                        part,
                        DarkRatioOffset
                    ),

                EdgeRatio =
                    ReadDouble(
                        descriptor,
                        part,
                        EdgeRatioOffset
                    ),

                AspectRatio =
                    ReadDouble(
                        descriptor,
                        part,
                        AspectRatioOffset
                    ),

                CenterX =
                    ReadDouble(
                        descriptor,
                        part,
                        CenterXOffset
                    ),

                CenterY =
                    ReadDouble(
                        descriptor,
                        part,
                        CenterYOffset
                    ),

                BorderRatio =
                    ReadDouble(
                        descriptor,
                        part,
                        BorderRatioOffset
                    ),

                Symmetry =
                    ReadDouble(
                        descriptor,
                        part,
                        SymmetryOffset
                    )
            };
        }

        private static void WritePart(
            byte[] descriptor,
            int part,
            ImageFingerprint fingerprint,
            double weight)
        {
            if (descriptor == null ||
                fingerprint == null ||
                !CanReadPart(
                    descriptor,
                    part
                ))
            {
                return;
            }

            WriteUlong(
                descriptor,
                part,
                HashOffset,
                fingerprint.Hash
            );

            WriteUlong(
                descriptor,
                part,
                EdgeHashOffset,
                fingerprint.EdgeHash
            );

            WriteUlong(
                descriptor,
                part,
                HorizontalHashOffset,
                fingerprint.HorizontalHash
            );

            WriteUlong(
                descriptor,
                part,
                VerticalHashOffset,
                fingerprint.VerticalHash
            );

            WriteUlong(
                descriptor,
                part,
                RadialHashOffset,
                fingerprint.RadialHash
            );

            WriteDouble(
                descriptor,
                part,
                DarkRatioOffset,
                fingerprint.DarkRatio
            );

            WriteDouble(
                descriptor,
                part,
                EdgeRatioOffset,
                fingerprint.EdgeRatio
            );

            WriteDouble(
                descriptor,
                part,
                AspectRatioOffset,
                fingerprint.AspectRatio
            );

            WriteDouble(
                descriptor,
                part,
                CenterXOffset,
                fingerprint.CenterX
            );

            WriteDouble(
                descriptor,
                part,
                CenterYOffset,
                fingerprint.CenterY
            );

            WriteDouble(
                descriptor,
                part,
                BorderRatioOffset,
                fingerprint.BorderRatio
            );

            WriteDouble(
                descriptor,
                part,
                SymmetryOffset,
                fingerprint.Symmetry
            );

            WriteDouble(
                descriptor,
                part,
                WeightOffset,
                weight
            );
        }

        private static void WriteUlong(
            byte[] descriptor,
            int part,
            int relativeOffset,
            ulong value)
        {
            int offset =
                part * PartSize +
                relativeOffset;

            byte[] bytes =
                BitConverter.GetBytes(value);

            Array.Copy(
                bytes,
                0,
                descriptor,
                offset,
                8
            );
        }

        private static void WriteDouble(
            byte[] descriptor,
            int part,
            int relativeOffset,
            double value)
        {
            int offset =
                part * PartSize +
                relativeOffset;

            byte[] bytes =
                BitConverter.GetBytes(value);

            Array.Copy(
                bytes,
                0,
                descriptor,
                offset,
                8
            );
        }

        private static ulong ReadUlong(
            byte[] descriptor,
            int part,
            int relativeOffset)
        {
            int offset =
                part * PartSize +
                relativeOffset;

            return BitConverter.ToUInt64(
                descriptor,
                offset
            );
        }

        private static double ReadDouble(
            byte[] descriptor,
            int part,
            int relativeOffset)
        {
            int offset =
                part * PartSize +
                relativeOffset;

            return BitConverter.ToDouble(
                descriptor,
                offset
            );
        }

        private static bool IsValidDescriptor(
            byte[] descriptor)
        {
            if (descriptor == null ||
                descriptor.Length < TotalBytes)
            {
                return false;
            }

            if (!CanReadPart(
                    descriptor,
                    FullPart
                ))
            {
                return false;
            }

            double fullWeight =
                ReadDouble(
                    descriptor,
                    FullPart,
                    WeightOffset
                );

            if (fullWeight <= 0 ||
                double.IsNaN(fullWeight) ||
                double.IsInfinity(fullWeight))
            {
                return false;
            }

            ImageFingerprint full =
                ReadFingerprint(
                    descriptor,
                    FullPart
                );

            return IsFingerprintValid(full);
        }

        private static bool IsFingerprintValid(
            ImageFingerprint fingerprint)
        {
            if (fingerprint == null)
                return false;

            bool hasHash =
                fingerprint.Hash != 0 ||
                fingerprint.EdgeHash != 0 ||
                fingerprint.HorizontalHash != 0 ||
                fingerprint.VerticalHash != 0 ||
                fingerprint.RadialHash != 0;

            if (!hasHash)
                return false;

            if (double.IsNaN(
                    fingerprint.AspectRatio
                ) ||
                double.IsInfinity(
                    fingerprint.AspectRatio
                ) ||
                fingerprint.AspectRatio <= 0)
            {
                return false;
            }

            return true;
        }

        private static bool CanReadPart(
            byte[] descriptor,
            int part)
        {
            if (descriptor == null ||
                part < 0 ||
                part >= MaxParts)
            {
                return false;
            }

            int start =
                part * PartSize;

            int end =
                start + PartSize;

            return start >= 0 &&
                   end <= descriptor.Length;
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
                    ImageSegment segment =
                        segments[index];

                    if (segment != null &&
                        segment.Bitmap != null)
                    {
                        segment.Bitmap.Dispose();
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
                Math.Max(
                    first,
                    second
                ),
                Math.Max(
                    third,
                    fourth
                )
            );
        }

        private static double Minimum(
            double first,
            double second,
            double third,
            double fourth)
        {
            return Math.Min(
                Math.Min(
                    first,
                    second
                ),
                Math.Min(
                    third,
                    fourth
                )
            );
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
