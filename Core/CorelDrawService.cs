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
            Log("START SMART JEWELLERY ENGINE: " + cdrPath);

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

                    List<ShapeInfo> usable =
                        CollectUsableShapes(page, shapeCount);

                    Log("USABLE-SHAPES=" + usable.Count);

                    List<List<int>> groups =
                        BuildSmartGroups(usable);

                    Log("SMART-GROUPS=" + groups.Count);

                    int designNo = 1;
                    var groupedIndexes = new HashSet<int>();

                    // Complete nearby design groups
                    for (int groupNo = 0;
                         groupNo < groups.Count;
                         groupNo++)
                    {
                        List<int> group = groups[groupNo];

                        if (group == null || group.Count < 2)
                            continue;

                        try
                        {
                            if (!SelectIndexes(page, group))
                                continue;

                            string baseName =
                                SafeName(
                                    Path.GetFileNameWithoutExtension(cdrPath)
                                ) +
                                "_p" + pageNo +
                                "_group" + (groupNo + 1);

                            string pngPath = Path.Combine(
                                cacheRoot,
                                baseName + "_HD.png"
                            );

                            string thumbPath = Path.Combine(
                                cacheRoot,
                                baseName + "_thumb.jpg"
                            );

                            CopyActiveSelection();
                            Thread.Sleep(250);

                            if (!SaveClipboardArtwork(
                                    pngPath,
                                    thumbPath))
                            {
                                Log(
                                    "GROUP EXPORT FAILED group=" +
                                    (groupNo + 1)
                                );

                                continue;
                            }

                            if (!IsUsefulExport(pngPath))
                            {
                                SafeDelete(pngPath);
                                SafeDelete(thumbPath);

                                Log(
                                    "GROUP REJECTED group=" +
                                    (groupNo + 1)
                                );

                                continue;
                            }

                            results.Add(
                                CreateRecord(
                                    cdrPath,
                                    thumbPath,
                                    pngPath,
                                    pageNo,
                                    designNo,
                                    "GROUP-HD",
                                    group.Count
                                )
                            );

                            for (int i = 0; i < group.Count; i++)
                                groupedIndexes.Add(group[i]);

                            Log(
                                "GROUP OK page=" + pageNo +
                                " design=" + designNo +
                                " shapes=" + group.Count
                            );

                            designNo++;
                        }
                        catch (Exception ex)
                        {
                            Log(
                                "GROUP FAILED page=" + pageNo +
                                " group=" + (groupNo + 1) +
                                " error=" + ex.Message
                            );
                        }
                    }

                    // Single objects fallback
                    for (int i = 0; i < usable.Count; i++)
                    {
                        ShapeInfo info = usable[i];

                        try
                        {
                            // Jo object already proper group me export hua,
                            // uska single duplicate sirf tab rakhen jab group chhota ho.
                            if (groupedIndexes.Contains(info.Index))
                                continue;

                            dynamic shape = page.Shapes[info.Index];

                            try
                            {
                                _app.ActiveDocument.ClearSelection();
                            }
                            catch
                            {
                            }

                            shape.CreateSelection();
                            Thread.Sleep(130);

                            string baseName =
                                SafeName(
                                    Path.GetFileNameWithoutExtension(cdrPath)
                                ) +
                                "_p" + pageNo +
                                "_obj" + info.Index;

                            string pngPath = Path.Combine(
                                cacheRoot,
                                baseName + "_HD.png"
                            );

                            string thumbPath = Path.Combine(
                                cacheRoot,
                                baseName + "_thumb.jpg"
                            );

                            CopySelectedOrShape(shape);
                            Thread.Sleep(220);

                            if (!SaveClipboardArtwork(
                                    pngPath,
                                    thumbPath))
                            {
                                continue;
                            }

                            if (!IsUsefulExport(pngPath))
                            {
                                SafeDelete(pngPath);
                                SafeDelete(thumbPath);
                                continue;
                            }

                            results.Add(
                                CreateRecord(
                                    cdrPath,
                                    thumbPath,
                                    pngPath,
                                    pageNo,
                                    designNo,
                                    "OBJECT-HD",
                                    1
                                )
                            );

                            Log(
                                "OBJECT OK page=" + pageNo +
                                " shape=" + info.Index +
                                " design=" + designNo
                            );

                            designNo++;
                        }
                        catch (Exception ex)
                        {
                            Log(
                                "OBJECT FAILED page=" + pageNo +
                                " shape=" + info.Index +
                                " error=" + ex.Message
                            );
                        }
                    }
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

        private List<ShapeInfo> CollectUsableShapes(
            dynamic page,
            int shapeCount)
        {
            var list = new List<ShapeInfo>();

            for (int i = 1; i <= shapeCount; i++)
            {
                try
                {
                    dynamic shape = page.Shapes[i];

                    if (!IsUsableShape(shape))
                        continue;

                    RectangleF box = GetShapeBox(shape);

                    if (!IsUsefulBox(box))
                        continue;

                    list.Add(
                        new ShapeInfo
                        {
                            Index = i,
                            Box = box
                        }
                    );
                }
                catch
                {
                }
            }

            return list;
        }

        private List<List<int>> BuildSmartGroups(
            List<ShapeInfo> shapes)
        {
            var groups = new List<List<int>>();

            if (shapes == null || shapes.Count == 0)
                return groups;

            bool[] used = new bool[shapes.Count];

            float medianSize = GetMedianSize(shapes);
            float joinGap = Math.Max(1.5f, medianSize * 0.55f);

            for (int start = 0;
                 start < shapes.Count;
                 start++)
            {
                if (used[start])
                    continue;

                var component = new List<int>();
                var queue = new Queue<int>();

                queue.Enqueue(start);
                used[start] = true;

                RectangleF groupBox = shapes[start].Box;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    ShapeInfo a = shapes[current];

                    component.Add(a.Index);
                    groupBox = Union(groupBox, a.Box);

                    for (int j = 0; j < shapes.Count; j++)
                    {
                        if (used[j])
                            continue;

                        ShapeInfo b = shapes[j];

                        if (ShouldJoin(
                                a.Box,
                                b.Box,
                                joinGap))
                        {
                            used[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                // Bahut bada component aksar kai designs ko jod deta hai.
                // Usko rows/columns ke gap se split karte hain.
                List<List<int>> split =
                    SplitOversizedComponent(
                        component,
                        shapes,
                        medianSize
                    );

                for (int i = 0; i < split.Count; i++)
                {
                    if (split[i].Count > 0)
                        groups.Add(split[i]);
                }
            }

            return groups;
        }

        private bool ShouldJoin(
            RectangleF a,
            RectangleF b,
            float gap)
        {
            RectangleF expandedA = a;
            expandedA.Inflate(gap, gap);

            if (expandedA.IntersectsWith(b))
                return true;

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

            bool horizontalOverlap =
                OverlapAmount(
                    a.Left,
                    a.Right,
                    b.Left,
                    b.Right
                ) >
                Math.Min(a.Width, b.Width) * 0.18f;

            bool verticalOverlap =
                OverlapAmount(
                    a.Top,
                    a.Bottom,
                    b.Top,
                    b.Bottom
                ) >
                Math.Min(a.Height, b.Height) * 0.18f;

            // Pendant ke vertically stacked pieces
            if (horizontalOverlap &&
                verticalGap <= gap * 1.7f)
            {
                return true;
            }

            // Necklace/ring ke side-by-side connected pieces
            if (verticalOverlap &&
                horizontalGap <= gap * 1.7f)
            {
                return true;
            }

            return false;
        }

        private List<List<int>> SplitOversizedComponent(
            List<int> component,
            List<ShapeInfo> allShapes,
            float medianSize)
        {
            var result = new List<List<int>>();

            if (component.Count <= 12)
            {
                result.Add(component);
                return result;
            }

            var infos = new List<ShapeInfo>();

            for (int i = 0; i < component.Count; i++)
            {
                ShapeInfo found =
                    FindShapeInfo(allShapes, component[i]);

                if (found != null)
                    infos.Add(found);
            }

            infos.Sort(
                delegate(ShapeInfo x, ShapeInfo y)
                {
                    int topCompare =
                        x.Box.Top.CompareTo(y.Box.Top);

                    if (topCompare != 0)
                        return topCompare;

                    return x.Box.Left.CompareTo(y.Box.Left);
                }
            );

            float splitGap = Math.Max(
                3f,
                medianSize * 1.5f
            );

            var current = new List<int>();
            float previousBottom = float.MinValue;

            for (int i = 0; i < infos.Count; i++)
            {
                ShapeInfo info = infos[i];

                if (current.Count > 0 &&
                    info.Box.Top - previousBottom > splitGap)
                {
                    result.Add(current);
                    current = new List<int>();
                }

                current.Add(info.Index);

                if (info.Box.Bottom > previousBottom)
                    previousBottom = info.Box.Bottom;
            }

            if (current.Count > 0)
                result.Add(current);

            return result;
        }

        private ShapeInfo FindShapeInfo(
            List<ShapeInfo> list,
            int index)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Index == index)
                    return list[i];
            }

            return null;
        }

        private float GetMedianSize(
            List<ShapeInfo> shapes)
        {
            var sizes = new List<float>();

            for (int i = 0; i < shapes.Count; i++)
            {
                float value = Math.Max(
                    shapes[i].Box.Width,
                    shapes[i].Box.Height
                );

                if (value > 0)
                    sizes.Add(value);
            }

            if (sizes.Count == 0)
                return 5f;

            sizes.Sort();

            return sizes[sizes.Count / 2];
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

        private bool IsUsableShape(dynamic shape)
        {
            try
            {
                int type = Convert.ToInt32(shape.Type);

                // Guideline
                if (type == 6)
                    return false;
            }
            catch
            {
            }

            try
            {
                bool visible =
                    Convert.ToBoolean(shape.Visible);

                if (!visible)
                    return false;
            }
            catch
            {
            }

            return true;
        }

        private bool IsUsefulBox(RectangleF box)
        {
            if (box.Width < 0.5f ||
                box.Height < 0.5f)
            {
                return false;
            }

            float ratio =
                box.Width /
                Math.Max(0.001f, box.Height);

            if (ratio > 12f || ratio < 0.08f)
                return false;

            float area =
                box.Width * box.Height;

            if (area < 0.5f)
                return false;

            return true;
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
                                Math.Abs(
                                    brightness - previous
                                ) > 45)
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

                    // Solid rectangle/polygon
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

        private RectangleF Union(
            RectangleF a,
            RectangleF b)
        {
            return RectangleF.FromLTRB(
                Math.Min(a.Left, b.Left),
                Math.Min(a.Top, b.Top),
                Math.Max(a.Right, b.Right),
                Math.Max(a.Bottom, b.Bottom)
            );
        }

        private void CleanOldImages(string cacheRoot)
        {
            try
            {
                foreach (
                    string file in
                    Directory.GetFiles(
                        cacheRoot,
                        "*.jpg"))
                {
                    File.Delete(file);
                }

                foreach (
                    string file in
                    Directory.GetFiles(
                        cacheRoot,
                        "*.png"))
                {
                    File.Delete(file);
                }
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

            throw new InvalidOperationException(
                "Copy failed."
            );
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
                Image image =
                    Clipboard.GetImage();

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
                Log(
                    "SAVE FAILED: " +
                    ex.Message
                );

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

            return new RectangleF(
                x,
                y,
                width,
                height
            );
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
                name =
                    name.Replace(character, '_');
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
