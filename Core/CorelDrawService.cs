using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

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

        public IEnumerable<ExportedDesign> ExportDesigns(string cdrPath, string cacheRoot)
        {
            var results = new List<ExportedDesign>();
            Directory.CreateDirectory(cacheRoot);

            dynamic doc = null;

            try
            {
                doc = _app.OpenDocument(cdrPath);
                int pageCount = Convert.ToInt32(doc.Pages.Count);

                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    page.Activate();

                    int shapeCount = 0;
                    try { shapeCount = Convert.ToInt32(page.Shapes.Count); } catch { shapeCount = 0; }

                    if (shapeCount <= 0)
                    {
                        string pageFile = Path.Combine(cacheRoot, Safe(Path.GetFileNameWithoutExtension(cdrPath)) + "_p" + p + "_page.png");
                        if (ExportCurrentPage(pageFile))
                        {
                            results.Add(new ExportedDesign { PageNumber = p, ObjectNumber = 0, PngPath = pageFile });
                        }
                        continue;
                    }

                    for (int i = 1; i <= shapeCount; i++)
                    {
                        string outFile = Path.Combine(cacheRoot, Safe(Path.GetFileNameWithoutExtension(cdrPath)) + "_p" + p + "_o" + i + ".png");

                        try
                        {
                            dynamic shape = page.Shapes[i];
                            if (ExportOneShape(shape, outFile))
                            {
                                results.Add(new ExportedDesign
                                {
                                    PageNumber = p,
                                    ObjectNumber = i,
                                    PngPath = outFile
                                });
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (results.Count == 0)
                    {
                        string pageFile = Path.Combine(cacheRoot, Safe(Path.GetFileNameWithoutExtension(cdrPath)) + "_p" + p + "_page.png");
                        if (ExportCurrentPage(pageFile))
                        {
                            results.Add(new ExportedDesign { PageNumber = p, ObjectNumber = 0, PngPath = pageFile });
                        }
                    }
                }
            }
            finally
            {
                if (doc != null)
                {
                    try { doc.Close(); } catch { }
                    try { Marshal.ReleaseComObject(doc); } catch { }
                }
            }

            return results;
        }

        private bool ExportOneShape(dynamic shape, string outFile)
        {
            try
            {
                if (File.Exists(outFile)) File.Delete(outFile);

                shape.CreateSelection();

                dynamic sel = _app.ActiveSelection;
                if (sel == null) return false;

                try
                {
                    sel.ExportBitmap(outFile, 5, 0, 0, 800, 800).Finish();
                }
                catch
                {
                    try
                    {
                        sel.Export(outFile, 774, 0);
                    }
                    catch
                    {
                        return false;
                    }
                }

                return File.Exists(outFile) && new FileInfo(outFile).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool ExportCurrentPage(string outFile)
        {
            try
            {
                if (File.Exists(outFile)) File.Delete(outFile);

                try
                {
                    _app.ActiveDocument.ExportBitmap(outFile, 5, 0, 0, 1200, 1200).Finish();
                }
                catch
                {
                    try
                    {
                        _app.ActiveDocument.Export(outFile, 774, 0);
                    }
                    catch
                    {
                        return false;
                    }
                }

                return File.Exists(outFile) && new FileInfo(outFile).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string Safe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        public void OpenCdr(string path)
        {
            _app.Visible = true;
            _app.OpenDocument(path);
        }

        public void Dispose()
        {
            if (_app != null)
            {
                try { _app.Quit(); } catch { }
                try { Marshal.ReleaseComObject(_app); } catch { }
                _app = null;
            }
        }
    }

    public sealed class ExportedDesign
    {
        public int PageNumber;
        public int ObjectNumber;
        public string PngPath;
    }
}
