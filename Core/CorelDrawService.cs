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
                string baseName = SafeName(Path.GetFileNameWithoutExtension(cdrPath));

                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    page.Activate();

                    List<ShapeBox> shapes = GetUsableShapes(page);

                    // Debug: ab pata chalega Corel X4 shapes read kar raha hai ya nahi.
                    MessageBox.Show(
                        "CDR: " + fileName + "\n" +
                        "Page: " + p + "\n" +
                        "Shapes detected: " + shapes.Count
                    );

                    int exportedOnPage = 0;

                    // Pehle simple aur reliable test:
                    // har usable shape ko export try karo.
                    // Isse confirm hoga Corel export chal raha hai ya nahi.
                    for (int i = 0; i < shapes.Count; i++)
                    {
                        int designNo = i + 1;
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_d" + designNo + ".jpg");

                        bool ok = ExportOneShape(doc, shapes[i].Shape, outFile);

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
                                ExportMode = "single-shape-debug",
                                ShapeCount = 1
                            });

                            exportedOnPage++;
                        }
                    }

                    // Agar single shape export bhi fail ho, to full page export try karo.
                    if (exportedOnPage == 0)
                    {
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_page.jpg");

                        bool ok = ExportWholePage(doc, page, outFile);

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
                                ExportMode = "page-fallback-debug",
                                ShapeCount = shapes.Count
                            });

                            exportedOnPage++;
                        }
                    }

                    if (exportedOnPage == 0)
                    {
                        MessageBox.Show(
                            "Export failed completely.\n\n" +
                            "CDR: " + cdrPath + "\n" +
                            "Page: " + p + "\n" +
                            "Shapes detected: " + shapes.Count
                        );
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

                    list.Add(new ShapeBox
                    {
                        Index = i,
                        Width = w,
                        Height = h,
                        Shape = shape
                    });
                }
                catch { }
            }

            return list;
        }

        private bool ExportOneShape(dynamic doc, dynamic shape, string outFile)
        {
            try
            {
                try { Clipboard.Clear(); } catch { }
                try { doc.ClearSelection(); } catch { }
                try { _app.ActiveDocument.ClearSelection(); } catch { }

                try { shape.CreateSelection(); } catch { return false; }

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

        private bool ExportWholePage(dynamic doc, dynamic page, string outFile)
        {
            try
            {
                try { Clipboard.Clear(); } catch { }
                try { doc.ClearSelection(); } catch { }
                try { _app.ActiveDocument.ClearSelection(); } catch { }

                try { page.Shapes.All().CreateSelection(); } catch { return false; }

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

                    return File.Exists(outFile) && new FileInfo(outFile).Length > 1000;
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
