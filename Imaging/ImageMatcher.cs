using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class ImageMatcher
    {
        private const int PartSize = 32;
        private const int MaxParts = 6;
        private const int TotalBytes = MaxParts * PartSize;

        private const int FullPart = 0;
        private const int TopPart = 1;
        private const int MiddlePart = 2;
        private const int BottomPart = 3;
        private const int LeftPart = 4;
        private const int RightPart = 5;

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

            try
            {
                using (Bitmap bitmap =
                    new Bitmap(imagePath))
                {
                    List<ImageSegment> segments =
                        ImageSegmenter.Segment(bitmap);

                    int count =
                        Math.Min(
                            MaxParts,
                            segments.Count
                        );

                    for (int i = 0; i < count; i++)
                    {
                        ImageSegment segment =
                            segments[i];

                        if (segment == null ||
                            segment.Bitmap == null)
                        {
                            continue;
                        }

                        ImageFingerprint fingerprint =
                            ImageFingerprint.FromBitmap(
                                segment.Bitmap
                            );

                        WritePart(
                            descriptor,
                            i,
                            fingerprint.Hash,
                            fingerprint.DarkRatio,
                            fingerprint.EdgeRatio,
                            segment.Weight
                        );
                    }

                    DisposeSegments(segments);
                }
            }
            catch
            {
                return new byte[TotalBytes];
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

            double topScore =
                ComparePart(
                    query,
                    candidate,
                    TopPart,
                    TopPart
                );

            double middleScore =
                ComparePart(
                    query,
                    candidate,
                    MiddlePart,
                    MiddlePart
                );

            double bottomScore =
                ComparePart(
                    query,
                    candidate,
                    BottomPart,
                    BottomPart
                );

            double normalLeft =
                ComparePart(
                    query,
                    candidate,
                    LeftPart,
                    LeftPart
                );

            double normalRight =
                ComparePart(
                    query,
                    candidate,
                    RightPart,
                    RightPart
                );

            // Photo mirror ho sakti hai.
            double mirrorLeft =
                ComparePart(
                    query,
                    candidate,
                    LeftPart,
                    RightPart
                );

            double mirrorRight =
                ComparePart(
                    query,
                    candidate,
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

            double verticalScore =
                topScore * 0.27 +
                middleScore * 0.46 +
                bottomScore * 0.27;

            double finalScore =
                fullScore * 0.48 +
                verticalScore * 0.36 +
                sideScore * 0.16;

            int strongParts = 0;
            int mediumParts = 0;

            CountPart(
                fullScore,
                ref strongParts,
                ref mediumParts
            );

            CountPart(
                topScore,
                ref strongParts,
                ref mediumParts
            );

            CountPart(
                middleScore,
                ref strongParts,
                ref mediumParts
            );

            CountPart(
                bottomScore,
                ref strongParts,
                ref mediumParts
            );

            CountPart(
                sideScore,
                ref strongParts,
                ref mediumParts
            );

            double minimumVertical =
                Math.Min(
                    topScore,
                    Math.Min(
                        middleScore,
                        bottomScore
                    )
                );

            double maximumVertical =
                Math.Max(
                    topScore,
                    Math.Max(
                        middleScore,
                        bottomScore
                    )
                );

            // Sirf ek part similar ho to high score mat do.
            if (strongParts <= 1)
                finalScore *= 0.72;

            if (mediumParts <= 2)
                finalScore *= 0.82;

            // Full silhouette weak hai to result ko control karo.
            if (fullScore < 42)
                finalScore *= 0.72;
            else if (fullScore < 52)
                finalScore *= 0.84;

            // Parts me bahut disagreement ho to random group ho sakta hai.
            if (maximumVertical - minimumVertical > 38)
                finalScore *= 0.82;

            // Middle jewellery ka sabse important part hai.
            if (middleScore < 38)
                finalScore *= 0.76;
            else if (middleScore < 50)
                finalScore *= 0.88;

            // Exact/same design boost.
            if (fullScore >= 82 &&
                middleScore >= 78 &&
                verticalScore >= 74)
            {
                finalScore += 7;
            }

            if (fullScore >= 90 &&
                middleScore >= 86 &&
                strongParts >= 4)
            {
                finalScore += 5;
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
            return new Size(256, 256);
        }

        private static double ComparePart(
            byte[] query,
            byte[] candidate,
            int queryPart,
            int candidatePart)
        {
            double queryWeight =
                ReadDouble(
                    query,
                    queryPart,
                    24
                );

            double candidateWeight =
                ReadDouble(
                    candidate,
                    candidatePart,
                    24
                );

            if (queryWeight <= 0 ||
                candidateWeight <= 0)
            {
                return 0;
            }

            ImageFingerprint first =
                new ImageFingerprint
                {
                    Hash =
                        ReadUlong(
                            query,
                            queryPart,
                            0
                        ),

                    DarkRatio =
                        ReadDouble(
                            query,
                            queryPart,
                            8
                        ),

                    EdgeRatio =
                        ReadDouble(
                            query,
                            queryPart,
                            16
                        )
                };

            ImageFingerprint second =
                new ImageFingerprint
                {
                    Hash =
                        ReadUlong(
                            candidate,
                            candidatePart,
                            0
                        ),

                    DarkRatio =
                        ReadDouble(
                            candidate,
                            candidatePart,
                            8
                        ),

                    EdgeRatio =
                        ReadDouble(
                            candidate,
                            candidatePart,
                            16
                        )
                };

            double score =
                ImageFingerprint.Compare(
                    first,
                    second
                );

            // Bahut alag fill/density wale designs par extra penalty.
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

            if (darkDifference > 0.32)
                score *= 0.70;
            else if (darkDifference > 0.22)
                score *= 0.84;

            if (edgeDifference > 0.25)
                score *= 0.76;
            else if (edgeDifference > 0.17)
                score *= 0.88;

            return Clamp(
                score,
                0,
                100
            );
        }

        private static void CountPart(
            double score,
            ref int strongParts,
            ref int mediumParts)
        {
            if (score >= 72)
                strongParts++;

            if (score >= 55)
                mediumParts++;
        }

        private static bool IsValidDescriptor(
            byte[] descriptor)
        {
            if (descriptor == null ||
                descriptor.Length < TotalBytes)
            {
                return false;
            }

            double fullWeight =
                ReadDouble(
                    descriptor,
                    FullPart,
                    24
                );

            return fullWeight > 0;
        }

        private static void WritePart(
            byte[] descriptor,
            int part,
            ulong hash,
            double darkRatio,
            double edgeRatio,
            double weight)
        {
            int offset =
                part * PartSize;

            Array.Copy(
                BitConverter.GetBytes(hash),
                0,
                descriptor,
                offset,
                8
            );

            Array.Copy(
                BitConverter.GetBytes(darkRatio),
                0,
                descriptor,
                offset + 8,
                8
            );

            Array.Copy(
                BitConverter.GetBytes(edgeRatio),
                0,
                descriptor,
                offset + 16,
                8
            );

            Array.Copy(
                BitConverter.GetBytes(weight),
                0,
                descriptor,
                offset + 24,
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

        private static void DisposeSegments(
            List<ImageSegment> segments)
        {
            if (segments == null)
                return;

            for (int i = 0; i < segments.Count; i++)
            {
                try
                {
                    if (segments[i] != null &&
                        segments[i].Bitmap != null)
                    {
                        segments[i].Bitmap.Dispose();
                    }
                }
                catch
                {
                }
            }
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
