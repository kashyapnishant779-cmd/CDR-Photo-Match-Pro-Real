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

                    int shapeCount = 0;
                    try { shapeCount = Convert.ToInt32(page.Shapes.Count); } catch { }

                    for (int s = 1; s <= shapeCount; s++)
                    {
                        try
                        {
                            dynamic shape = page.Shapes[s];

                            double w = SafeDouble(shape.SizeWidth);
                            double h = SafeDouble(shape.SizeHeight);

                            if (w <= 0 || h <= 0)
                                continue;

                            double big = Math.Max(w, h);
                            double small = Math.Min(w, h);

                            if (big < 1 || small < 1)
                                continue;

                            if (small > 0 && (big / small) > 80)
                                continue;

                            string outFile = Path.Combine(
                                cacheRoot,
                                Path.GetFileNameWithoutExtension(cdrPath) + "_p" + p + "_o" + s + ".jpg"
                            );

                            bool ok = ExportShapeByClipboard(shape, outFile);

                            if (ok && File.Exists(outFile))
                            {
                                results.Add(new DesignRecord
                                {
                                    CdrPath = cdrPath,
                                    FileName = fileName,
                                    FolderPath = folderPath,
                                    PageNumber = p,
                                    ObjectNumber = s,
                                    ThumbnailPath = outFile,
                                    PngPath = outFile
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("CDR open/export error:\n\n" + ex.ToString());
            }
            finally
            {
                try { if (doc != null) doc.Close(); } catch { }
            }

            return results;
        }

        private bool ExportShapeByClipboard(dynamic shape, string outFile)
        {
            try
            {
                Clipboard.Clear();

                try { _app.ActiveDocument.ClearSelection(); } catch { }

                shape.CreateSelection();

                Application.DoEvents();
                Thread.Sleep(150);

                SendKeys.SendWait("^c");

                for (int i = 0; i < 30; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(200);

                    if (Clipboard.ContainsImage())
                    {
                        using (Image img = Clipboard.GetImage())
                        {
                            if (img != null)
                            {
                                img.Save(outFile, ImageFormat.Jpeg);
                                return File.Exists(outFile);
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private double SafeDouble(object value)
        {
            try { return Convert.ToDouble(value); }
            catch { return 0; }
        }

        public void Dispose()
        {
            try
            {
                if (_app != null)
                {
                    _app.Quit();
                    _app = null;
                }
            }
            catch { }
        }
    }
}
