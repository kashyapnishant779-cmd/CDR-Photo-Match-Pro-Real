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

            Log("START OBJECT ONLY ENGINE: " + cdrPath);

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
                        "Page " + pageNo +
                        " top-level shapes: " + shapeCount
                    );

                    int designNo = 1;

                    for (int shapeNo = 1;
                         shapeNo <= shapeCount;
                         shapeNo++)
                    {
                        try
                        {
                            dynamic shape = page.Shapes[shapeNo];

                            if (!IsUsableShape(shape))
                            {
                                Log(
                                    "SKIP shape=" + shapeNo +
                                    " unusable"
                                );

                                continue;
                            }

                            RectangleF box = GetShapeBox(shape);

                            if (!IsUsefulBox(box))
                            {
                                Log(
                                    "SKIP shape=" + shapeNo +
                                    " box=" +
                                    box.Width + "x" + box.Height
                                );

                                continue;
                            }

                            try
                            {
                                _app.ActiveDocument.ClearSelection();
                            }
                            catch
                            {
                            }

                            shape.CreateSelection();
                            Thread.Sleep(150);

                            string baseName =
                                SafeName(
                                    Path.GetFileNameWithoutExtension(
                                        cdrPath
                                    )
                                ) +
                                "_p" + pageNo +
                                "_obj" + shapeNo;

                            string pngPath = Path.Combine(
                                cacheRoot,
                                baseName + "_HD.png"
                            );

                            string thumbPath = Path.Combine(
                                cacheRoot,
                                baseName + "_thumb.jpg"
                            );

                            CopySelectedOrShape(shape);
                            Thread.Sleep(250);

                            if (!SaveClipboardArtwork(
                                    pngPath,
                                    thumbPath))
                            {
                                Log(
                                    "EXPORT FAILED shape=" +
                                    shapeNo
                                );

                                continue;
                            }

                            if (!IsUsefulExport(pngPath))
                            {
                                SafeDelete(pngPath);
                                SafeDelete(thumbPath);

                                Log(
                                    "DELETE BAD EXPORT shape=" +
                                    shapeNo
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
                                    "OBJECT-HD",
                                    1
                                )
                            );

                            Log(
                                "OBJECT OK page=" + pageNo +
                                " shape=" + shapeNo +
                                " design=" + designNo
                            );

                            designNo++;
                        }
                        catch (Exception ex)
                        {
                            Log(
                                "OBJECT FAILED page=" +
                                pageNo +
                                " shape=" +
                                shapeNo +
                                " error=" +
                                ex.Message
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

            Log("RESULTS: " + results.Count);

            return results;
        }

        private bool IsUsableShape(dynamic shape)
        {
            try
            {
                int type = Convert.ToInt32(shape.Type);

                // CorelDRAW guideline
                if (type == 6)
                    return false;
            }
            catch
            {
            }

            try
            {
                bool visible = Convert.ToBoolean(shape.Visible);

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
            if (box.Width < 2f || box.Height < 2f)
                return false;

            float ratio =
                box.Width / Math.Max(0.001f, box.Height);

            if (ratio > 7f || ratio < 0.14f)
                return false;

            float area = box.Width * box.Height;

            if (area < 4f)
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
                    if (bmp.Width < 40 || bmp.Height < 40)
                        return false;

                    double ratio =
                        bmp.Width /
                        (double)Math.Max(1, bmp.Height);

                    if (ratio > 7.0 || ratio < 0.14)
                        return false;

                    int dark = 0;
                    int nearWhite = 0;
                    int total = 0;

                    int stepX = Math.Max(1, bmp.Width / 80);
                    int stepY = Math.Max(1, bmp.Height / 80);

                    for (int y = 0;
                         y < bmp.Height;
                         y += stepY)
                    {
                        for (int x = 0;
                             x < bmp.Width;
                             x += stepX)
                        {
                            Color c = bmp.GetPixel(x, y);

                            int bright =
                                (c.R + c.G + c.B) / 3;

                            int max =
                                Math.Max(
                                    c.R,
                                    Math.Max(c.G, c.B)
                                );

                            int min =
                                Math.Min(
                                    c.R,
                                    Math.Min(c.G, c.B)
                                );

                            int saturation = max - min;

                            if (bright < 70)
                                dark++;

                            if (bright > 245 &&
                                saturation < 15)
                            {
                                nearWhite++;
                            }

                            total++;
                        }
                    }

                    if (total == 0)
                        return false;

                    double darkRatio =
                        dark / (double)total;

                    double whiteRatio =
                        nearWhite / (double)total;

                    // Almost completely empty
                    if (whiteRatio > 0.995)
                        return false;

                    // Almost completely solid black rectangle/polygon
                    if (darkRatio > 0.92)
                        return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
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
            }
            catch
            {
            }
        }

        private void CopySelectedOrShape(dynamic shape)
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
                Log(
                    "SaveClipboardArtwork failed: " +
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

        private RectangleF GetShapeBox(dynamic shape)
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
                var property = type.GetProperty(
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

                var field = type.GetField(
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
