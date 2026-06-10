using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CSharp.RuntimeBinder;

namespace CDRPhotoMatchPro.Core
{
    public sealed class CorelDrawService : IDisposable
    {
        private dynamic _app;
        public CorelDrawService()
        {
            var type = Type.GetTypeFromProgID("CorelDRAW.Application");
            if (type == null) throw new InvalidOperationException("CorelDRAW COM not found. Install CorelDRAW X4 or newer.");
            _app = Activator.CreateInstance(type);
            _app.Visible = false;
        }
        public IEnumerable<ExportedDesign> ExportDesigns(string cdrPath, string cacheRoot)
        {
            Directory.CreateDirectory(cacheRoot);
            dynamic doc = null;
            try
            {
                doc = _app.OpenDocument(cdrPath, 0);
                int pageCount = Convert.ToInt32(doc.Pages.Count);
                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p]; page.Activate();
                    int objIndex = 0;
                    try
                    {
                        foreach (dynamic shape in page.Shapes)
                        {
                            objIndex++;
                            string outFile = Path.Combine(cacheRoot, Safe(Path.GetFileNameWithoutExtension(cdrPath)) + "_p" + p + "_o" + objIndex + ".png");
                            ExportShape(shape, outFile);
                            if (File.Exists(outFile)) yield return new ExportedDesign { PageNumber = p, ObjectNumber = objIndex, PngPath = outFile };
                        }
                    }
                    catch (RuntimeBinderException)
                    {
                        string outFile = Path.Combine(cacheRoot, Safe(Path.GetFileNameWithoutExtension(cdrPath)) + "_p" + p + "_page.png");
                        page.Export(outFile, 774, 0);
                        if (File.Exists(outFile)) yield return new ExportedDesign { PageNumber = p, ObjectNumber = 0, PngPath = outFile };
                    }
                }
            }
            finally
            {
                if (doc != null) { try { doc.Close(); } catch { } }
            }
        }
        private void ExportShape(dynamic shape, string outFile)
        {
            try { shape.CreateSelection(); _app.ActiveSelection.Export(outFile, 774, 0); }
            catch { try { shape.Export(outFile, 774, 0); } catch { } }
        }
        private static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }
        public void OpenCdr(string path) { _app.Visible = true; _app.OpenDocument(path, 0); }
        public void Dispose() { if (_app != null) { try { _app.Quit(); } catch { } _app = null; } }
    }
    public sealed class ExportedDesign { public int PageNumber; public int ObjectNumber; public string PngPath; }
}
