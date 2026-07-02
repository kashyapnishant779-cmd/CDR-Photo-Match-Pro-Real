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

        public IEnumerable<DesignRecord> ExportDesigns(string cdrPath, string cacheRoot)
        {
            var results = new List<DesignRecord>();
            Directory.CreateDirectory(cacheRoot);

            _logFile = Path.Combine(cacheRoot, "export_debug.txt");
            Log("START HD GROUP ENGINE: " + cdrPath);

            dynamic doc = null;

            try
            {
                doc = _app.OpenDocument(cdrPath, 0);

                int pageCount = Convert.ToInt32(doc.Pages.Count);

                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    page.Activate();

                    int shapeCount = Convert.ToInt32(page.Shapes.Count);
                    Log("Page " + p + " shapes: " + shapeCount);

                    List<List<int>> groups = BuildGroups(page, shapeCount);

                    int designNo = 1;

                    foreach (var group in groups)
                    {
                        try
                        {
                            string baseName =
                                SafeName(Path.GetFileNameWithoutExtension(cdrPath)) +
                                "_p" + p + "_d" + designNo;

                            string pngFile = Path.Combine(cacheRoot, baseName + "_HD.png");
                            string thumbFile = Path.Combine(cacheRoot, baseName + "_thumb.jpg");

                            SelectGroup(page, group);
                            Thread.Sleep(150);

                            page.Shapes[group[0]].Copy();
                            Log("Copied group page=" + p + " design=" + designNo + " shapes=" + group.Count);

                            Thread.Sleep(300);

                            bool ok = SaveClipboardArtwork(pngFile, thumbFile);

                            if (ok && File.Exists(pngFile) && File.Exists(thumbFile))
                            {
                                results.Add(CreateRecord(
                                    cdrPath,
                                    thumbFile,
                                    pngFile,
                                    p,
                                    designNo,
                                    "HD-GROUP",
                                    group.Count
                                ));

                                Log("OK: " + pngFile);
                                designNo++;
                            }
                            else
                            {
                                Log("FAILED group page=" + p + " design=" + designNo);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("Group failed: " + ex.Message);
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
                catch { }
            }

            Log("RESULTS: " + results.Count);
            return results;
        }

        private List<List<int>> BuildGroups(dynamic page, int shapeCount)
        {
            var groups = new List<List<int>>();
            var used = new bool[shapeCount + 1];

            for (int i = 1; i <= shapeCount; i++)
            {
                if (used[i]) continue;

                var g = new List<int>();
                g.Add(i);
                used[i] = true;

                RectangleF boxA = GetShapeBox(page.Shapes[i]);

                for (int j = i + 1; j <= shapeCount; j++)
                {
                    if (used[j]) continue;

                    RectangleF boxB = GetShapeBox(page.Shapes[j]);

                    if (IsNear(boxA, boxB))
                    {
                        g.Add(j);
                        used[j] = true;
                        boxA = Union(boxA, boxB);
                    }
                }

                groups.Add(g);
            }

            return groups;
        }

        private RectangleF GetShapeBox(dynamic shape)
        {
            float x = ToFloat(GetAny(shape, "LeftX", "PositionX", "CenterX"));
            float y = ToFloat(GetAny(shape, "TopY", "PositionY", "CenterY"));
            float w = Math.Abs(ToFloat(GetAny(shape, "SizeWidth", "Width")));
            float h = Math.Abs(ToFloat(GetAny(shape, "SizeHeight", "Height")));

            if (w <= 0) w = 1;
            if (h <= 0) h = 1;

            return new RectangleF(x, y, w, h);
        }

        private bool IsNear(RectangleF a, RectangleF b)
        {
            RectangleF aa = a;
            aa.Inflate(Math.Max(a.Width, a.Height) * 0.45f, Math.Max(a.Width, a.Height) * 0.45f);
            return aa.IntersectsWith(b);
        }

        private RectangleF Union(RectangleF a, RectangleF b)
        {
            float x1 = Math.Min(a.Left, b.Left);
            float y1 = Math.Min(a.Top, b.Top);
            float x2 = Math.Max(a.Right, b.Right);
            float y2 = Math.Max(a.Bottom, b.Bottom);
            return RectangleF.FromLTRB(x1, y1, x2, y2);
        }

        private void SelectGroup(dynamic page, List<int> group)
        {
            try { _app.ActiveDocument.ClearSelection(); } catch { }

            bool first = true;

            foreach (int idx in group)
            {
                dynamic sh = page.Shapes[idx];

                if (first)
                {
                    sh.CreateSelection();
                    first = false;
                }
                else
                {
                    try { sh.AddToSelection(); }
                    catch
                    {
                        try { sh.Selected = true; } catch { }
                    }
                }
            }
        }

        private bool SaveClipboardArtwork(string pngPath, string thumbPath)
        {
            try
            {
                Image img = Clipboard.GetImage();

                if (img != null)
                {
                    SaveFit(img, pngPath, HD_SIZE, ImageFormat.Png);
                    SaveFit(img, thumbPath, THUMB_SIZE, ImageFormat.Jpeg);
                    img.Dispose();
                    return true;
                }

                using (Metafile mf = GetEnhancedMetafileFromClipboard())
                {
                    if (mf == null)
                        return false;

                    SaveFit(mf, pngPath, HD_SIZE, ImageFormat.Png);
                    SaveFit(mf, thumbPath, THUMB_SIZE, ImageFormat.Jpeg);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("SaveClipboardArtwork failed: " + ex.Message);
                return false;
            }
        }

        private void CopyActiveSelection()
{
    try
    {
        _app.ActiveSelection.Copy();
        return;
    }
    catch { }

    try
    {
        _app.ActiveDocument.Selection.Copy();
        return;
    }
    catch { }

    try
    {
        _app.ActiveDocument.ActiveSelection.Copy();
        return;
    }
    catch { }

    throw new InvalidOperationException("Active selection copy failed.");
}

private void SaveFit(Image source, string outputPath, int maxSize, ImageFormat format)
{
                
            int sw = source.Width <= 0 ? maxSize : source.Width;
            int sh = source.Height <= 0 ? maxSize : source.Height;

            double scale = Math.Min(maxSize / (double)sw, maxSize / (double)sh);
            if (scale <= 0) scale = 1;

            int w = Math.Max(1, (int)(sw * scale));
            int h = Math.Max(1, (int)(sh * scale));

            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(source, 0, 0, w, h);
                }

                bmp.Save(outputPath, format);
            }
        }

        private DesignRecord CreateRecord(string cdrPath, string thumbPath, string pngPath, int pageNo, int designNo, string mode, int shapeCount)
        {
            var rec = (DesignRecord)Activator.CreateInstance(typeof(DesignRecord), true);

            SetAny(rec, new[] { "CdrPath", "FilePath", "FullPath", "Path" }, cdrPath);
            SetAny(rec, new[] { "ThumbnailPath", "ThumbPath" }, thumbPath);
            SetAny(rec, new[] { "PngPath", "PreviewPath", "ImagePath" }, pngPath);
            SetAny(rec, new[] { "PageNumber", "PageNo", "Page" }, pageNo);
            SetAny(rec, new[] { "DesignNumber", "DesignNo", "ObjectNumber", "ObjectNo", "ShapeNumber", "ShapeNo" }, designNo);
            SetAny(rec, new[] { "FileName", "CdrFileName", "Name" }, Path.GetFileName(cdrPath));
            SetAny(rec, new[] { "FolderPath", "FullFolderPath" }, Path.GetDirectoryName(cdrPath));
            SetAny(rec, new[] { "ExportMode", "Mode" }, mode);
            SetAny(rec, new[] { "ShapeCount", "Shapes" }, shapeCount);

            return rec;
        }

        private object GetAny(object obj, params string[] names)
        {
            foreach (string name in names)
            {
                try
                {
                    var p = obj.GetType().GetProperty(name);
                    if (p != null) return p.GetValue(obj, null);
                }
                catch { }

                try
                {
                    return obj.GetType().InvokeMember(name, BindingFlags.GetProperty, null, obj, null);
                }
                catch { }
            }

            return 0;
        }

        private float ToFloat(object v)
        {
            try { return Convert.ToSingle(v); }
            catch { return 0; }
        }

        private void SetAny(object obj, string[] names, object value)
        {
            Type t = obj.GetType();

            foreach (string name in names)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(obj, Convert.ChangeType(value, Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType), null);
                    return;
                }

                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(obj, Convert.ChangeType(value, Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType));
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
                IntPtr h = GetClipboardData(CF_ENHMETAFILE);
                if (h == IntPtr.Zero)
                    return null;

                IntPtr copy = CopyEnhMetaFile(h, IntPtr.Zero);
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
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name;
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(_logFile, DateTime.Now.ToString("HH:mm:ss") + " - " + msg + Environment.NewLine);
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (_app != null)
                    _app.Quit();
            }
            catch { }

            _app = null;
        }

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CopyEnhMetaFile(IntPtr hemfSrc, IntPtr lpszFile);
    }
}
