using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CDRPhotoMatchPro.Core
{
    public sealed class CorelDrawService : IDisposable
    {
        private dynamic _app;
        private string _logFile;

        private const int HD_SIZE = 2200;
        private const int THUMB_SIZE = 420;

        private sealed class ShapeInfo
        {
            public int Index;
            public RectangleF Box;
            public int Type;
            public int ChildCount;
            public bool IsExistingGroup;
        }

        public CorelDrawService()
        {
            var type =
                Type.GetTypeFromProgID("CorelDRAW.Application.14") ??
                Type.GetTypeFromProgID("CorelDRAW.Application");

            if (type == null)
                throw new InvalidOperationException("CorelDRAW COM not found.");

            _app = Activator.CreateInstance(type);
            _app.Visible = true;
        }

        public IEnumerable<DesignRecord> ExportDesigns(
            string cdrPath,
            string cacheRoot)
        {
            var results = new List<DesignRecord>();

            Directory.CreateDirectory(cacheRoot);
            CleanOldImages(cacheRoot);

            _logFile = Path.Combine(cacheRoot, "export_debug.txt");
            Log("START MIXED CDR EXPORT ENGINE: " + cdrPath);

            dynamic doc = null;

            try
            {
                doc = _app.OpenDocument(cdrPath, 0);

                int pageCount = Convert.ToInt32(doc.Pages.Count);

                for (int pageNo = 1; pageNo <= pageCount; pageNo++)
                {
                    dynamic page = doc.Pages[pageNo];
                    page.Activate();

                    int shapeCount = Convert.ToInt32(page.Shapes.Count);

                    Log(
                        "PAGE=" + pageNo +
                        " TOP-LEVEL-SHAPES=" + shapeCount
                    );

                    List<ShapeInfo> all =
                        CollectTopLevelShapes(page, shapeCount, pageNo);

                    Log("ACCEPTED-TOP-LEVEL-SHAPES=" + all.Count);

                    int designNo = 1;
                    var consumed = new HashSet<int>();

                    // STEP 1:
                    // CorelDRAW me jo actual GROUP already bana hua hai,
                    // usko ek complete design maan kar seedha export karo.
                    for (int i = 0; i < all.Count; i++)
                    {
                        ShapeInfo info = all[i];

                        if (!info.IsExistingGroup)
                            continue;

                        if (ExportSingleTopLevelShape(
                                page,
                                info,
                                cdrPath,
                                cacheRoot,
                                pageNo,
                                designNo,
                                "COREL-GROUP-HD",
                                Math.Max(1, info.ChildCount),
                                results))
                        {
                            consumed.Add(info.Index);
                            designNo++;
                        }
                    }

                    // STEP 2:
                    // Ungrouped / combined / curve / normal top-level objects ko
                    // proximity ke basis par complete jewellery components me jodo.
                    var loose = new List<ShapeInfo>();

                    for (int i = 0; i < all.Count; i++)
                    {
                        ShapeInfo info = all[i];

                        if (!consumed.Contains(info.Index))
                            loose.Add(info);
                    }

                    List<List<ShapeInfo>> components =
                        BuildLooseComponents(loose);

                    Log("LOOSE-COMPONENTS=" + components.Count);

                    for (int componentNo = 0;
                         componentNo < components.Count;
                         componentNo++)
                    {
                        List<ShapeInfo> component =
                            components[componentNo];

                        if (component == null ||
                            component.Count == 0)
                        {
                            continue;
                        }

                        if (component.Count == 1)
                        {
                            ShapeInfo info = component[0];

                            if (ExportSingleTopLevelShape(
                                    page,
                                    info,
                                    cdrPath,
                                    cacheRoot,
                                    pageNo,
                                    designNo,
                                    "OBJECT-HD",
                                    1,
                                    results))
                            {
                                consumed.Add(info.Index);
                                designNo++;
                            }

                            continue;
                        }

                        var indexes = new List<int>();

                        for (int i = 0; i < component.Count; i++)
                            indexes.Add(component[i].Index);

                        if (ExportSelectedIndexes(
                                page,
                                indexes,
                                cdrPath,
                                cacheRoot,
                                pageNo,
                                designNo,
                                "SMART-GROUP-HD",
                                component.Count,
                                "smart" + (componentNo + 1),
                                results))
                        {
                            for (int i = 0; i < component.Count; i++)
                                consumed.Add(component[i].Index);

                            designNo++;
                        }
                    }

                    Log(
                        "PAGE COMPLETE page=" + pageNo +
                        " designs=" + (designNo - 1) +
                        " consumedTopLevel=" + consumed.Count +
                        "/" + all.Count
                    );
                }
            }
            catch (Exception ex)
            {
                Log("MAIN FAILED: " + ex);
            }
            finally
            {
                try
                {
                    if (doc != null)
                        doc.Close();
                }
                catch
                {
                }
            }

            Log("RESULTS=" + results.Count);
            return results;
        }

        private List<ShapeInfo> CollectTopLevelShapes(
            dynamic page,
            int shapeCount,
            int pageNo)
        {
            var list = new List<ShapeInfo>();

            for (int i = 1; i <= shapeCount; i++)
            {
                try
                {
                    dynamic shape = page.Shapes[i];
                    string rejectReason;

                    if (!IsUsableShape(shape, out rejectReason))
                    {
                        Log(
                            "SHAPE REJECT page=" + pageNo +
                            " shape=" + i +
                            " reason=" + rejectReason
                        );

                        continue;
                    }

                    RectangleF box = GetShapeBox(shape);

                    if (!IsPotentialDesignBox(box, out rejectReason))
                    {
                        Log(
                            "SHAPE REJECT page=" + pageNo +
                            " shape=" + i +
                            " reason=" + rejectReason +
                            " left=" + box.Left.ToString("0.###") +
                            " top=" + box.Top.ToString("0.###") +
                            " width=" + box.Width.ToString("0.###") +
                            " height=" + box.Height.ToString("0.###")
                        );

                        continue;
                    }

                    int type = GetShapeType(shape);
                    int childCount = GetChildShapeCount(shape);

                    // CorelDRAW X4 me real group normally child Shapes expose karta hai.
                    // Type value par akela depend nahi karte, kyunki mixed/old CDR me
                    // COM type handling kabhi-kabhi inconsistent ho sakti hai.
                    bool isExistingGroup = childCount > 0;

                    Log(
                        "SHAPE ACCEPT page=" + pageNo +
                        " shape=" + i +
                        " type=" + type +
                        " children=" + childCount +
                        " existingGroup=" + isExistingGroup +
                        " left=" + box.Left.ToString("0.###") +
                        " top=" + box.Top.ToString("0.###") +
                        " width=" + box.Width.ToString("0.###") +
                        " height=" + box.Height.ToString("0.###")
                    );

                    list.Add(
                        new ShapeInfo
                        {
                            Index = i,
                            Box = box,
                            Type = type,
                            ChildCount = childCount,
                            IsExistingGroup = isExistingGroup
                        }
                    );
                }
                catch (Exception ex)
                {
                    Log(
                        "SHAPE READ FAILED page=" + pageNo +
                        " shape=" + i +
                        " error=" + ex.Message
                    );
                }
            }

            return list;
        }

        private List<List<ShapeInfo>> BuildLooseComponents(
            List<ShapeInfo> shapes)
        {
            var result = new List<List<ShapeInfo>>();

            if (shapes == null || shapes.Count == 0)
                return result;

            bool[] used = new bool[shapes.Count];

            for (int i = 0; i < shapes.Count; i++)
            {
                if (used[i])
                    continue;

                var component = new List<ShapeInfo>();
                var queue = new Queue<int>();

                used[i] = true;
                queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    ShapeInfo currentInfo = shapes[current];

                    component.Add(currentInfo);

                    for (int j = 0; j < shapes.Count; j++)
                    {
                        if (used[j])
                            continue;

                        if (!ShouldJoinLooseShapes(
                                currentInfo.Box,
                                shapes[j].Box))
                        {
                            continue;
                        }

                        used[j] = true;
                        queue.Enqueue(j);
                    }
                }

                // Accidental chain-merging se poora page ek component na bane.
                // Agar component bahut bada/door-door hai to usko vertical gaps se split karo.
                List<List<ShapeInfo>> split =
                    SplitOversizedLooseComponent(component);

                for (int s = 0; s < split.Count; s++)
                {
                    if (split[s].Count > 0)
                        result.Add(split[s]);
                }
            }

            return result;
        }

        private bool ShouldJoinLooseShapes(
            RectangleF a,
            RectangleF b)
        {
            RectangleF intersection =
                RectangleF.Intersect(a, b);

            if (!intersection.IsEmpty)
            {
                float intersectionArea =
                    intersection.Width * intersection.Height;

                float smallerArea =
                    Math.Min(
                        Math.Max(0.0001f, a.Width * a.Height),
                        Math.Max(0.0001f, b.Width * b.Height)
                    );

                if (intersectionArea / smallerArea >= 0.18f)
                    return true;
            }

            float horizontalGap = AxisGap(
                a.Left,
                a.Right,
                b.Left,
                b.Right
            );

            float verticalGap = AxisGap(
                a.Top,
                a.Bottom,
                b.Top,
                b.Bottom
            );

            float horizontalOverlap = OverlapAmount(
                a.Left,
                a.Right,
                b.Left,
                b.Right
            );

            float verticalOverlap = OverlapAmount(
                a.Top,
                a.Bottom,
                b.Top,
                b.Bottom
            );

            float smallerWidth =
                Math.Max(0.0001f, Math.Min(a.Width, b.Width));

            float smallerHeight =
                Math.Max(0.0001f, Math.Min(a.Height, b.Height));

            double horizontalOverlapRatio =
                horizontalOverlap / smallerWidth;

            double verticalOverlapRatio =
                verticalOverlap / smallerHeight;

            float aCenterX = a.Left + a.Width / 2f;
            float bCenterX = b.Left + b.Width / 2f;
            float centerDifferenceX =
                Math.Abs(aCenterX - bCenterX);

            float aCenterY = a.Top + a.Height / 2f;
            float bCenterY = b.Top + b.Height / 2f;
            float centerDifferenceY =
                Math.Abs(aCenterY - bCenterY);

            float largerHeight = Math.Max(a.Height, b.Height);
            float largerWidth = Math.Max(a.Width, b.Width);

            // Jewellery ke top-main-drop parts:
            // same vertical line + overlap + small vertical gap.
            bool verticalChain =
                centerDifferenceX <=
                    Math.Max(1.0f, smallerWidth * 0.65f) &&
                horizontalOverlapRatio >= 0.20 &&
                verticalGap <=
                    Math.Max(1.8f, largerHeight * 0.65f);

            if (verticalChain)
                return true;

            // Side-by-side mirrored pieces ko tabhi jodo jab woh bahut kareeb hon
            // aur vertical alignment strong ho.
            bool horizontalPair =
                centerDifferenceY <=
                    Math.Max(0.8f, smallerHeight * 0.40f) &&
                verticalOverlapRatio >= 0.50 &&
                horizontalGap <=
                    Math.Max(0.9f, largerWidth * 0.20f);

            if (horizontalPair)
                return true;

            return false;
        }

        private List<List<ShapeInfo>> SplitOversizedLooseComponent(
            List<ShapeInfo> component)
        {
            var result = new List<List<ShapeInfo>>();

            if (component == null || component.Count == 0)
                return result;

            if (component.Count <= 16)
            {
                result.Add(component);
                return result;
            }

            var ordered = new List<ShapeInfo>(component);

            ordered.Sort(
                delegate(ShapeInfo x, ShapeInfo y)
                {
                    float xCenter =
                        x.Box.Left + x.Box.Width / 2f;

                    float yCenter =
                        y.Box.Left + y.Box.Width / 2f;

                    int compare = xCenter.CompareTo(yCenter);

                    if (compare != 0)
                        return compare;

                    return x.Box.Top.CompareTo(y.Box.Top);
                }
            );

            var widths = new List<float>();

            for (int i = 0; i < ordered.Count; i++)
                widths.Add(Math.Max(0.1f, ordered[i].Box.Width));

            widths.Sort();
            float medianWidth = widths[widths.Count / 2];
            float splitGap = Math.Max(3.0f, medianWidth * 1.6f);

            var current = new List<ShapeInfo>();
            float previousRight = float.MinValue;

            for (int i = 0; i < ordered.Count; i++)
            {
                ShapeInfo info = ordered[i];

                if (current.Count > 0 &&
                    info.Box.Left - previousRight > splitGap)
                {
                    result.Add(current);
                    current = new List<ShapeInfo>();
                }

                current.Add(info);

                if (info.Box.Right > previousRight)
                    previousRight = info.Box.Right;
            }

            if (current.Count > 0)
                result.Add(current);

            return result;
        }

        private bool ExportSingleTopLevelShape(
            dynamic page,
            ShapeInfo info,
            string cdrPath,
            string cacheRoot,
            int pageNo,
            int designNo,
            string mode,
            int shapeCount,
            List<DesignRecord> results)
        {
            try
            {
                dynamic shape = page.Shapes[info.Index];

                try
                {
                    _app.ActiveDocument.ClearSelection();
                }
                catch
                {
                }

                shape.CreateSelection();
                Thread.Sleep(120);

                string tag =
                    mode == "COREL-GROUP-HD"
                        ? "corelgroup" + info.Index
                        : "obj" + info.Index;

                string baseName =
                    SafeName(
                        Path.GetFileNameWithoutExtension(cdrPath)
                    ) +
                    "_p" + pageNo +
                    "_" + tag;

                string pngPath =
                    Path.Combine(cacheRoot, baseName + "_HD.png");

                string thumbPath =
                    Path.Combine(cacheRoot, baseName + "_thumb.jpg");

                CopySelectedOrShape(shape);
                Thread.Sleep(220);

                if (!SaveClipboardArtwork(pngPath, thumbPath))
                {
                    Log(
                        "EXPORT FAILED page=" + pageNo +
                        " shape=" + info.Index +
                        " mode=" + mode
                    );

                    return false;
                }

                if (!IsUsefulExport(pngPath))
                {
                    SafeDelete(pngPath);
                    SafeDelete(thumbPath);

                    Log(
                        "EXPORT REJECTED page=" + pageNo +
                        " shape=" + info.Index +
                        " mode=" + mode
                    );

                    return false;
                }

                results.Add(
                    CreateRecord(
                        cdrPath,
                        thumbPath,
                        pngPath,
                        pageNo,
                        designNo,
                        mode,
                        shapeCount
                    )
                );

                Log(
                    "EXPORT OK page=" + pageNo +
                    " design=" + designNo +
                    " shape=" + info.Index +
                    " mode=" + mode +
                    " shapes=" + shapeCount
                );

                return true;
            }
            catch (Exception ex)
            {
                Log(
                    "EXPORT FAILED page=" + pageNo +
                    " shape=" + info.Index +
                    " mode=" + mode +
                    " error=" + ex.Message
                );

                return false;
            }
        }

        private bool ExportSelectedIndexes(
            dynamic page,
            List<int> indexes,
            string cdrPath,
            string cacheRoot,
            int pageNo,
            int designNo,
            string mode,
            int shapeCount,
            string tag,
            List<DesignRecord> results)
        {
            try
            {
                if (!SelectIndexes(page, indexes))
                    return false;

                string baseName =
                    SafeName(
                        Path.GetFileNameWithoutExtension(cdrPath)
                    ) +
                    "_p" + pageNo +
                    "_" + tag;

                string pngPath =
                    Path.Combine(cacheRoot, baseName + "_HD.png");

                string thumbPath =
                    Path.Combine(cacheRoot, baseName + "_thumb.jpg");

                CopyActiveSelection();
                Thread.Sleep(220);

                if (!SaveClipboardArtwork(pngPath, thumbPath))
                {
                    Log(
                        "SELECTION EXPORT FAILED page=" + pageNo +
                        " mode=" + mode +
                        " shapes=" + shapeCount
                    );

                    return false;
                }

                if (!IsUsefulExport(pngPath))
                {
                    SafeDelete(pngPath);
                    SafeDelete(thumbPath);

                    Log(
                        "SELECTION REJECTED page=" + pageNo +
                        " mode=" + mode +
                        " shapes=" + shapeCount
                    );

                    return false;
                }

                results.Add(
                    CreateRecord(
                        cdrPath,
                        thumbPath,
                        pngPath,
                        pageNo,
                        designNo,
                        mode,
                        shapeCount
                    )
                );

                Log(
                    "SELECTION OK page=" + pageNo +
                    " design=" + designNo +
                    " mode=" + mode +
                    " shapes=" + shapeCount
                );

                return true;
            }
            catch (Exception ex)
            {
                Log(
                    "SELECTION FAILED page=" + pageNo +
                    " mode=" + mode +
                    " shapes=" + shapeCount +
                    " error=" + ex.Message
                );

                return false;
            }
        }

        private bool SelectIndexes(
            dynamic page,
            List<int> indexes)
        {
            try
            {
                _app.ActiveDocument.ClearSelection();
            }
            catch
            {
            }

            bool any = false;

            for (int i = 0; i < indexes.Count; i++)
            {
                try
                {
                    dynamic shape = page.Shapes[indexes[i]];

                    if (!any)
                    {
                        shape.CreateSelection();
                        any = true;
                    }
                    else
                    {
                        try
                        {
                            shape.AddToSelection();
                        }
                        catch
                        {
                            try
                            {
                                shape.Selected = true;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return any;
        }

        private bool IsUsableShape(
            dynamic shape,
            out string rejectReason)
        {
            rejectReason = "";

            try
            {
                int type = Convert.ToInt32(shape.Type);

                // CorelDRAW guideline
                if (type == 6)
                {
                    rejectReason = "GUIDELINE type=6";
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("SHAPE TYPE READ WARNING: " + ex.Message);
            }

            try
            {
                bool visible = Convert.ToBoolean(shape.Visible);

                if (!visible)
                {
                    rejectReason = "HIDDEN visible=false";
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("SHAPE VISIBLE READ WARNING: " + ex.Message);
            }

            return true;
        }

        private bool IsPotentialDesignBox(
            RectangleF box,
            out string rejectReason)
        {
            rejectReason = "";

            if (box.Width <= 0.002f ||
                box.Height <= 0.002f)
            {
                rejectReason = "MICROSCOPIC";
                return false;
            }

            float ratio =
                box.Width / Math.Max(0.001f, box.Height);

            if (ratio > 30f || ratio < 0.033f)
            {
                rejectReason =
                    "EXTREME_RATIO ratio=" +
                    ratio.ToString("0.###");

                return false;
            }

            return true;
        }

        private int GetShapeType(dynamic shape)
        {
            try
            {
                return Convert.ToInt32(shape.Type);
            }
            catch
            {
                return -1;
            }
        }

        private int GetChildShapeCount(dynamic shape)
        {
            try
            {
                return Convert.ToInt32(shape.Shapes.Count);
            }
            catch
            {
                return 0;
            }
        }

        private bool IsUsefulExport(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return false;

                using (Bitmap bmp = new Bitmap(imagePath))
                {
                    if (bmp.Width < 35 ||
                        bmp.Height < 35)
                    {
                        return false;
                    }

                    int dark = 0;
                    int white = 0;
                    int edgeChanges = 0;
                    int total = 0;

                    int stepX =
                        Math.Max(1, bmp.Width / 80);

                    int stepY =
                        Math.Max(1, bmp.Height / 80);

                    int previous = -1;

                    for (int y = 0;
                         y < bmp.Height;
                         y += stepY)
                    {
                        for (int x = 0;
                             x < bmp.Width;
                             x += stepX)
                        {
                            Color c = bmp.GetPixel(x, y);

                            int brightness =
                                (c.R + c.G + c.B) / 3;

                            if (brightness < 70)
                                dark++;

                            if (brightness > 245)
                                white++;

                            if (previous >= 0 &&
                                Math.Abs(brightness - previous) > 45)
                            {
                                edgeChanges++;
                            }

                            previous = brightness;
                            total++;
                        }
                    }

                    if (total == 0)
                        return false;

                    double darkRatio =
                        dark / (double)total;

                    double whiteRatio =
                        white / (double)total;

                    double edgeRatio =
                        edgeChanges / (double)total;

                    if (whiteRatio > 0.997)
                        return false;

                    if (darkRatio > 0.94 &&
                        edgeRatio < 0.04)
                    {
                        return false;
                    }

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private float AxisGap(
            float a1,
            float a2,
            float b1,
            float b2)
        {
            if (a2 < b1)
                return b1 - a2;

            if (b2 < a1)
                return a1 - b2;

            return 0;
        }

        private float OverlapAmount(
            float a1,
            float a2,
            float b1,
            float b2)
        {
            return Math.Max(
                0,
                Math.Min(a2, b2) -
                Math.Max(a1, b1)
            );
        }

        private void CleanOldImages(string cacheRoot)
        {
            try
            {
                foreach (
                    string file in
                    Directory.GetFiles(cacheRoot, "*.jpg"))
                {
                    File.Delete(file);
                }

                foreach (
                    string file in
                    Directory.GetFiles(cacheRoot, "*.png"))
                {
                    File.Delete(file);
                }

                string oldLog =
                    Path.Combine(cacheRoot, "export_debug.txt");

                if (File.Exists(oldLog))
                    File.Delete(oldLog);
            }
            catch
            {
            }
        }

        private void CopySelectedOrShape(
            dynamic shape)
        {
            try
            {
                CopyActiveSelection();
                return;
            }
            catch
            {
            }

            try
            {
                shape.Copy();
                return;
            }
            catch
            {
            }

            throw new InvalidOperationException("Copy failed.");
        }

        private void CopyActiveSelection()
        {
            try
            {
                _app.ActiveSelection.Copy();
                return;
            }
            catch
            {
            }

            try
            {
                _app.ActiveDocument.Selection.Copy();
                return;
            }
            catch
            {
            }

            try
            {
                _app.ActiveDocument.ActiveSelection.Copy();
                return;
            }
            catch
            {
            }

            throw new InvalidOperationException(
                "Active selection copy failed."
            );
        }

        private bool SaveClipboardArtwork(
            string pngPath,
            string thumbPath)
        {
            try
            {
                Image image = Clipboard.GetImage();

                if (image != null)
                {
                    SaveFit(
                        image,
                        pngPath,
                        HD_SIZE,
                        ImageFormat.Png
                    );

                    SaveFit(
                        image,
                        thumbPath,
                        THUMB_SIZE,
                        ImageFormat.Jpeg
                    );

                    image.Dispose();
                    return true;
                }

                using (
                    Metafile metafile =
                        GetEnhancedMetafileFromClipboard())
                {
                    if (metafile == null)
                        return false;

                    SaveFit(
                        metafile,
                        pngPath,
                        HD_SIZE,
                        ImageFormat.Png
                    );

                    SaveFit(
                        metafile,
                        thumbPath,
                        THUMB_SIZE,
                        ImageFormat.Jpeg
                    );

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("SAVE FAILED: " + ex.Message);
                return false;
            }
        }

        private void SaveFit(
            Image source,
            string outputPath,
            int maxSize,
            ImageFormat format)
        {
            int sourceWidth =
                source.Width <= 0
                    ? maxSize
                    : source.Width;

            int sourceHeight =
                source.Height <= 0
                    ? maxSize
                    : source.Height;

            double scale = Math.Min(
                maxSize / (double)sourceWidth,
                maxSize / (double)sourceHeight
            );

            if (scale <= 0)
                scale = 1;

            int width = Math.Max(
                1,
                (int)(sourceWidth * scale)
            );

            int height = Math.Max(
                1,
                (int)(sourceHeight * scale)
            );

            using (
                Bitmap bitmap =
                    new Bitmap(
                        width,
                        height,
                        PixelFormat.Format24bppRgb
                    ))
            {
                using (
                    Graphics graphics =
                        Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);

                    graphics.InterpolationMode =
                        System.Drawing.Drawing2D
                            .InterpolationMode
                            .HighQualityBicubic;

                    graphics.SmoothingMode =
                        System.Drawing.Drawing2D
                            .SmoothingMode
                            .HighQuality;

                    graphics.PixelOffsetMode =
                        System.Drawing.Drawing2D
                            .PixelOffsetMode
                            .HighQuality;

                    graphics.DrawImage(
                        source,
                        0,
                        0,
                        width,
                        height
                    );
                }

                bitmap.Save(outputPath, format);
            }
        }

        private RectangleF GetShapeBox(
            dynamic shape)
        {
            float x = ToFloat(
                GetAny(
                    shape,
                    "LeftX",
                    "PositionX",
                    "CenterX"
                )
            );

            float y = ToFloat(
                GetAny(
                    shape,
                    "TopY",
                    "PositionY",
                    "CenterY"
                )
            );

            float width = Math.Abs(
                ToFloat(
                    GetAny(
                        shape,
                        "SizeWidth",
                        "Width"
                    )
                )
            );

            float height = Math.Abs(
                ToFloat(
                    GetAny(
                        shape,
                        "SizeHeight",
                        "Height"
                    )
                )
            );

            if (width <= 0)
                width = 1;

            if (height <= 0)
                height = 1;

            return new RectangleF(x, y, width, height);
        }

        private DesignRecord CreateRecord(
            string cdrPath,
            string thumbPath,
            string pngPath,
            int pageNo,
            int designNo,
            string mode,
            int shapeCount)
        {
            var record =
                (DesignRecord)Activator.CreateInstance(
                    typeof(DesignRecord),
                    true
                );

            SetAny(
                record,
                new[]
                {
                    "CdrPath",
                    "FilePath",
                    "FullPath",
                    "Path"
                },
                cdrPath
            );

            SetAny(
                record,
                new[]
                {
                    "ThumbnailPath",
                    "ThumbPath"
                },
                thumbPath
            );

            SetAny(
                record,
                new[]
                {
                    "PngPath",
                    "PreviewPath",
                    "ImagePath"
                },
                pngPath
            );

            SetAny(
                record,
                new[]
                {
                    "PageNumber",
                    "PageNo",
                    "Page"
                },
                pageNo
            );

            SetAny(
                record,
                new[]
                {
                    "DesignNumber",
                    "DesignNo",
                    "ObjectNumber",
                    "ObjectNo",
                    "ShapeNumber",
                    "ShapeNo"
                },
                designNo
            );

            SetAny(
                record,
                new[]
                {
                    "FileName",
                    "CdrFileName",
                    "Name"
                },
                Path.GetFileName(cdrPath)
            );

            SetAny(
                record,
                new[]
                {
                    "FolderPath",
                    "FullFolderPath"
                },
                Path.GetDirectoryName(cdrPath)
            );

            SetAny(
                record,
                new[]
                {
                    "ExportMode",
                    "Mode"
                },
                mode
            );

            SetAny(
                record,
                new[]
                {
                    "ShapeCount",
                    "Shapes"
                },
                shapeCount
            );

            return record;
        }

        private object GetAny(
            object obj,
            params string[] names)
        {
            foreach (string name in names)
            {
                try
                {
                    return obj
                        .GetType()
                        .InvokeMember(
                            name,
                            BindingFlags.GetProperty,
                            null,
                            obj,
                            null
                        );
                }
                catch
                {
                }
            }

            return 0;
        }

        private float ToFloat(object value)
        {
            try
            {
                return Convert.ToSingle(value);
            }
            catch
            {
                return 0;
            }
        }

        private void SetAny(
            object obj,
            string[] names,
            object value)
        {
            Type type = obj.GetType();

            foreach (string name in names)
            {
                var property =
                    type.GetProperty(
                        name,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance
                    );

                if (property != null &&
                    property.CanWrite)
                {
                    property.SetValue(
                        obj,
                        Convert.ChangeType(
                            value,
                            Nullable.GetUnderlyingType(
                                property.PropertyType
                            ) ??
                            property.PropertyType
                        ),
                        null
                    );

                    return;
                }

                var field =
                    type.GetField(
                        name,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance
                    );

                if (field != null)
                {
                    field.SetValue(
                        obj,
                        Convert.ChangeType(
                            value,
                            Nullable.GetUnderlyingType(
                                field.FieldType
                            ) ??
                            field.FieldType
                        )
                    );

                    return;
                }
            }
        }

        private Metafile GetEnhancedMetafileFromClipboard()
        {
            const uint CF_ENHMETAFILE = 14;

            if (!OpenClipboard(IntPtr.Zero))
                return null;

            try
            {
                IntPtr handle =
                    GetClipboardData(CF_ENHMETAFILE);

                if (handle == IntPtr.Zero)
                    return null;

                IntPtr copy =
                    CopyEnhMetaFile(
                        handle,
                        IntPtr.Zero
                    );

                if (copy == IntPtr.Zero)
                    return null;

                return new Metafile(copy, true);
            }
            finally
            {
                CloseClipboard();
            }
        }

        private string SafeName(string name)
        {
            foreach (
                char character in
                Path.GetInvalidFileNameChars())
            {
                name = name.Replace(character, '_');
            }

            return name;
        }

        private void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    _logFile,
                    DateTime.Now.ToString("HH:mm:ss") +
                    " - " +
                    message +
                    Environment.NewLine
                );
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            try
            {
                if (_app != null)
                    _app.Quit();
            }
            catch
            {
            }

            _app = null;
        }

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(
            IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(
            uint uFormat);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CopyEnhMetaFile(
            IntPtr hemfSrc,
            IntPtr lpszFile);
    }
}
