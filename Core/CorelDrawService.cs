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
                        dynamic shape = null;

                        try
                        {
                            shape = page.Shapes[s];

                            double w = SafeDouble(shape.SizeWidth);
                            double h = SafeDouble(shape.SizeHeight);

                            if (w <= 0 || h <= 0)
                                continue;

                            double big = Math.Max(w, h);
                            double small = Math.Min(w, h);

                            // Bahut chhote stone/dot/line ignore
                            if (big < 8 || small < 2)
                                continue;

                            // Bahut patli strip/line ignore
                            if (big / small > 8)
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
            bool success = false;
            Exception error = null;

            Thread t = new Thread(() =>
            {
                try
                {
                    Clipboard.Clear();

                    try { _app.ActiveDocument.ClearSelection(); } catch { }

                    shape.CreateSelection();

                    try
                    {
                        _app.ActiveSelection.Copy();
                    }
                    catch
                    {
                        SendKeys.SendWait("^c");
                    }

                    for (int i = 0; i < 20; i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(250);

                        if (Clipboard.ContainsImage())
                        {
                            using (Image img = Clipboard.GetImage())
                            {
                                if (img != null)
                                {
                                    img.Save(outFile, ImageFormat.Jpeg);
                                    success = File.Exists(outFile);
                                    return;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            if (!success && error != null)
                MessageBox.Show("Shape export error:\n\n" + error.ToString());

            return success;
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
