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
            Log("START NEW ENGINE - NO COREL EXPORTBITMAP");

            dynamic doc = null;

            try
            {
                doc = _app.OpenDocument(cdrPath, 0);
                Log("Document opened: " + cdrPath);

                int pageCount = Convert.ToInt32(doc.Pages.Count);
                Log("Pages detected: " + pageCount);

                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    page.Activate();

                    int shapeCount = Convert.ToInt32(page.Shapes.Count);
                    Log("Page " + p + " shapes detected: " + shapeCount);

                    for (int s = 1; s <= shapeCount; s++)
                    {
                        try
                        {
                            dynamic shape = page.Shapes[s];

                            string outFile = Path.Combine(
                                cacheRoot,
                                SafeName(Path.GetFileNameWithoutExtension(cdrPath)) +
                                "_p" + p + "_s" + s + ".jpg"
                            );

                            Log("TEMP EXPORT START page=" + p + " shape=" + s);

                            shape.CreateSelection();
                            Thread.Sleep(120);

                            shape.Copy();
                            Log("Selection copied");

                            Thread.Sleep(250);

                            bool saved = SaveClipboardArtworkAsJpg(outFile, 900, 900);

                            if (!saved || !File.Exists(outFile))
                            {
                                Log("Clipboard JPG failed page=" + p + " shape=" + s);
                                continue;
                            }

                            Log("JPG created: " + outFile);

                            results.Add(CreateRecord(cdrPath, outFile, p, s));
                        }
                        catch (Exception exShape)
                        {
                            Log("Shape failed p=" + p + " s=" + s + " : " + exShape.Message);
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

            Log("Results: " + results.Count);
            return results;
        }

        private DesignRecord CreateRecord(string cdrPath, string thumbPath, int pageNo, int shapeNo)
        {
            var rec = (DesignRecord)Activator.CreateInstance(typeof(DesignRecord), true);

            SetAny(rec, new[] { "CdrPath", "FilePath", "FullPath", "Path" }, cdrPath);
            SetAny(rec, new[] { "ThumbnailPath", "ThumbPath", "PreviewPath", "ImagePath" }, thumbPath);
            SetAny(rec, new[] { "PageNumber", "PageNo", "Page" }, pageNo);
            SetAny(rec, new[] { "ShapeNumber", "ShapeNo", "ShapeIndex", "ObjectNumber", "ObjectNo" }, shapeNo);
            SetAny(rec, new[] { "FileName", "CdrFileName", "Name" }, Path.GetFileName(cdrPath));

            return rec;
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

        private bool SaveClipboardArtworkAsJpg(string outputPath, int maxWidth, int maxHeight)
        {
            try
            {
                Image img = Clipboard.GetImage();

                if (img != null)
                {
                    SaveImageFit(img, outputPath, maxWidth, maxHeight);
                    img.Dispose();
                    return true;
                }

                using (Metafile mf = GetEnhancedMetafileFromClipboard())
                {
                    if (mf == null)
                        return false;

                    SaveImageFit(mf, outputPath, maxWidth, maxHeight);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("SaveClipboardArtworkAsJpg failed: " + ex.Message);
                return false;
            }
        }

        private void SaveImageFit(Image source, string outputPath, int maxWidth, int maxHeight)
        {
            int w = source.Width;
            int h = source.Height;

            if (w <= 0) w = maxWidth;
            if (h <= 0) h = maxHeight;

            double ratio = Math.Min((double)maxWidth / w, (double)maxHeight / h);
            if (ratio <= 0 || ratio > 1.0) ratio = 1.0;

            int newW = Math.Max(1, (int)(w * ratio));
            int newH = Math.Max(1, (int)(h * ratio));

            using (Bitmap bmp = new Bitmap(newW, newH))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(source, 0, 0, newW, newH);
                }

                bmp.Save(outputPath, ImageFormat.Jpeg);
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
