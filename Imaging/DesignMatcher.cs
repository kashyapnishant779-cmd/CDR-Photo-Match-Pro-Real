using System;
using System.Collections.Generic;
using System.Drawing;

namespace CDRPhotoMatchPro.Imaging
{
    public sealed class DesignMatchScore
    {
        public double FinalScore { get; set; }
        public double FullScore { get; set; }
        public double PartScore { get; set; }
        public int GoodParts { get; set; }
        public string Reason { get; set; }
    }

    public static class DesignMatcher
    {
        public static DesignMatchScore Compare(Bitmap queryPhoto, Bitmap candidatePreview)
        {
            List<ImageSegment> qParts = ImageSegmenter.Segment(queryPhoto);
            List<ImageSegment> cParts = ImageSegmenter.Segment(candidatePreview);

            ImageFingerprint qFull = ImageFingerprint.FromBitmap(qParts[0].Bitmap);
            ImageFingerprint cFull = ImageFingerprint.FromBitmap(cParts[0].Bitmap);

            double fullScore = ImageFingerprint.Compare(qFull, cFull);

            double weighted = 0;
            double weightSum = 0;
            int goodParts = 0;

            for (int i = 0; i < qParts.Count; i++)
            {
                ImageSegment qp = qParts[i];
                double best = 0;

                for (int j = 0; j < cParts.Count; j++)
                {
                    ImageSegment cp = cParts[j];

                    ImageFingerprint qf = ImageFingerprint.FromBitmap(qp.Bitmap);
                    ImageFingerprint cf = ImageFingerprint.FromBitmap(cp.Bitmap);

                    double s = ImageFingerprint.Compare(qf, cf);

                    if (s > best)
                        best = s;
                }

                weighted += best * qp.Weight;
                weightSum += qp.Weight;

                if (best >= 72)
                    goodParts++;
            }

            double partScore = weightSum > 0 ? weighted / weightSum : 0;

            double final = fullScore * 0.60 + partScore * 0.40;

            if (goodParts <= 1 && final > 68)
                final = 58;

            if (fullScore < 45 && partScore > 78)
                final = 55;

            if (fullScore < 35)
                final = Math.Min(final, 50);

            if (final > 100) final = 100;
            if (final < 0) final = 0;

            return new DesignMatchScore
            {
                FinalScore = final,
                FullScore = fullScore,
                PartScore = partScore,
                GoodParts = goodParts,
                Reason = "FULL=" + fullScore.ToString("0.0") +
                         ", PART=" + partScore.ToString("0.0") +
                         ", GOOD_PARTS=" + goodParts
            };
        }
    }
}
