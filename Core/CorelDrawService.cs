using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CDRPhotoMatchPro.Core
{
    public sealed class CorelDrawService : IDisposable
    {
        private dynamic _app;

        private sealed class ShapeBox
        {
            public int Index;
            public double Left;
            public double Top;
            public double Right;
            public double Bottom;
            public double Width;
            public double Height;
            public dynamic Shape;
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

        public IEnumerable<DesignRecord> ExportDesigns(string cdrPath, string cacheRoot)
        {
            var results = new List<DesignRecord>();
            Directory.CreateDirectory(cacheRoot);

            dynamic doc = null;

            try
            {
                doc = _app.OpenDocument(cdrPath, 0);

                int pageCount = Convert.ToInt32(doc.Pages.Count);
                string fileName = Path.GetFileName(cdrPath);
                string folderPath = Path.GetDirectoryName(cdrPath);

                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    page.Activate();

                    List<ShapeBox> boxes = GetUsableShapes(page);
                    List<List<ShapeBox>> groups = BuildDesignGroups(boxes);

                    int designNo = 0;

                    foreach (List<ShapeBox> group in groups)
                    {
                        designNo++;

                        string baseName = SafeName(Path.GetFileNameWithoutExtension(cdrPath));
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_d" + designNo + ".jpg");

                        bool ok = ExportGroupByClipboard(doc, group, outFile);

                        if (ok && File.Exists(outFile))
                        {
                            results.Add(new DesignRecord
                            {
                                CdrPath = cdrPath,
                                FileName = fileName,
                                FolderPath = folderPath,
                                PageNumber = p,
                                DesignNumber = designNo,
                                ObjectNumber = designNo,
                                ThumbnailPath = outFile,
                                PngPath = outFile,
                                ExportMode = "grouped-shapes",
                                ShapeCount = group.Count
                            });
                        }
                    }

                    if (designNo == 0)
                    {
                        string baseName = SafeName(Path.GetFileNameWithoutExtension(cdrPath));
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_page.jpg");

                        bool ok = ExportPageByClipboard(doc, page, outFile);

                        if (ok && File.Exists(outFile))
                        {
                            results.Add(new DesignRecord
                            {
                                CdrPath = cdrPath,
                                FileName = fileName,
                                FolderPath = folderPath,
                                PageNumber = p,
                                DesignNumber = 1,
                                ObjectNumber = 1,
                                ThumbnailPath = outFile,
                                PngPath = outFile,
                                ExportMode = "fallback-page",
                                ShapeCount = 0
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("CDR open/export error:\n\n" + ex);
            }
            finally
            {
                try { if (doc != null) doc.Close(); } catch { }
            }

            return results;
        }

        private List<ShapeBox> GetUsableShapes(dynamic page)
        {
            var list = new List<ShapeBox>();

            int count = 0;
            try { count = Convert.ToInt32(page.Shapes.Count); } catch { }

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    dynamic shape = page.Shapes[i];

                    double w = SafeDouble(shape.SizeWidth);
                    double h = SafeDouble(shape.SizeHeight);

                    if (w <= 0 || h <= 0)
                        continue;

                    double big = Math.Max(w, h);
                    double small = Math.Min(w, h);

                    if (big < 0.5 || small < 0.05)
                        continue;

                    if (small > 0 && big / small > 150)
                        continue;

                    double left = SafeDouble(shape.LeftX);
                    double top = SafeDouble(shape.TopY);
                    double right = left + w;
                    double bottom = top - h;

                    list.Add(new ShapeBox
                    {
                        Index = i,
                        Left = Math.Min(left, right),
                        Right = Math.Max(left, right),
                        Top = Math.Max(top, bottom),
                        Bottom = Math.Min(top, bottom),
                        Width = w,
                        Height = h,
                        Shape = shape
                    });
                }
                catch { }
            }

            return list;
        }

        private List<List<ShapeBox>> BuildDesignGroups(List<ShapeBox> shapes)
        {
            var groups = new List<List<ShapeBox>>();
            var used = new bool[shapes.Count];

            for (int i = 0; i < shapes.Count; i++)
            {
                if (used[i]) continue;

                var group = new List<ShapeBox>();
                var queue = new Queue<int>();

                queue.Enqueue(i);
                used[i] = true;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    ShapeBox a = shapes[cur];
                    group.Add(a);

                    for (int j = 0; j < shapes.Count; j++)
                    {
                        if (used[j]) continue;

                        ShapeBox b = shapes[j];

                        if (IsNear(a, b))
                        {
                            used[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                if (IsValidDesignGroup(group))
                    groups.Add(group);
            }

            return groups;
        }

        private bool IsNear(ShapeBox a, ShapeBox b)
        {
            double gapX = 0;

            if (a.Right < b.Left) gapX = b.Left - a.Right;
            else if (b.Right < a.Left) gapX = a.Left - b.Right;

            double gapY = 0;

            if (a.Top < b.Bottom) gapY = b.Bottom - a.Top;
            else if (b.Top < a.Bottom) gapY = a.Bottom - b.Top;

            double avgSize = (Math.Max(a.Width, a.Height) + Math.Max(b.Width, b.Height)) / 2.0;
            double tolerance = Math.Max(3.0, avgSize * 0.45);

            return gapX <= tolerance && gapY <= tolerance;
        }

        private bool IsValidDesignGroup(List<ShapeBox> group)
        {
            if (group == null || group.Count == 0)
                return false;

            double left = double.MaxValue;
            double right = double.MinValue;
            double top = double.MinValue;
            double bottom = double.MaxValue;

            foreach (ShapeBox s in group)
            {
                left = Math.Min(left, s.Left);
                right = Math.Max(right, s.Right);
                top = Math.Max(top, s.Top);
                bottom = Math.Min(bottom, s.Bottom);
            }

            double w = right - left;
            double h = top - bottom;

            if (w <= 0 || h <= 0)
                return false;

            double big = Math.Max(w, h);
            double small = Math.Min(w, h);

            if (big < 2 || small < 2)
                return false;

            if (small > 0 && big / small > 60)
                return false;

            return true;
        }

        private bool ExportGroupByClipboard(dynamic doc, List<ShapeBox> group, string outFile)
        {
            try
            {
                try { Clipboard.Clear(); } catch { }
                try { doc.ClearSelection(); } catch { }
                try { _app.ActiveDocument.ClearSelection(); } catch { }

                int selected = 0;

                for (int i = 0; i < group.Count; i++)
                {
                    try
                    {
                        if (selected == 0)
                            group[i].Shape.CreateSelection();
                        else
                            group[i].Shape.AddToSelection();

                        selected++;
                    }
                    catch { }
                }

                Application.DoEvents();
                Thread.Sleep(300);

                if (selected <= 0)
                    return false;

                if (ExportSelectionNative(doc, outFile))
                    return true;

                try
                {
                    SendKeys.SendWait("^c");
                    Application.DoEvents();
                    Thread.Sleep(500);
                }
                catch { }

                return SaveClipboardImage(outFile);
            }
            catch
            {
                return false;
            }
        }

        private bool ExportPageByClipboard(dynamic doc, dynamic page, string outFile)
        {
            try
            {
                try { Clipboard.Clear(); } catch { }
                try { doc.ClearSelection(); } catch { }

                try { page.Shapes.All().CreateSelection(); } catch { }

                Application.DoEvents();
                Thread.Sleep(300);

                if (ExportSelectionNative(doc, outFile))
                    return true;

                try
                {
                    SendKeys.SendWait("^c");
                    Application.DoEvents();
                    Thread.Sleep(500);
                }
                catch { }

                return SaveClipboardImage(outFile);
            }
            catch
            {
                return false;
            }
        }

        private bool ExportSelectionNative(dynamic doc, string outFile)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outFile));

                dynamic filter = doc.ExportBitmap(
                    outFile,
                    774,
                    1,
                    2,
                    1200,
                    1200,
                    150,
                    150,
                    1,
                    false,
                    true,
                    true,
                    false
                );

                try { filter.Finish(); } catch { }

                return File.Exists(outFile) && new FileInfo(outFile).Length > 1000;
            }
            catch
            {
                return false;
            }
        }

        private bool SaveClipboardImage(string outFile)
        {
            for (int i = 0; i < 40; i++)
            {
                Application.DoEvents();
                Thread.Sleep(150);

                if (!Clipboard.ContainsImage())
                    continue;

                using (Image img = Clipboard.GetImage())
                {
                    if (img == null)
                        continue;

                    using (Bitmap bmp = new Bitmap(img))
                    {
                        bmp.Save(outFile, ImageFormat.Jpeg);
                    }

                    return File.Exists(outFile);
                }
            }

            return false;
        }

        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s;
        }

        private double SafeDouble(object value)
        {
            try { return Convert.ToDouble(value); }
            catch { return 0; }
        }

        public void Dispose()
        {
            try { _app = null; } catch { }
        }
    }
}
