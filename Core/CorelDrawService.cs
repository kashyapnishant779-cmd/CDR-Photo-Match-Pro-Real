using System;
using System.Collections.Generic;
using System.IO;
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
            _app.Visible = false;
        }

        public IEnumerable<ExportedDesign> ExportDesigns(string cdrPath, string cacheRoot)
        {
            var results = new List<ExportedDesign>();
            Directory.CreateDirectory(cacheRoot);

            MessageBox.Show("Export start:\n" + cdrPath);

            dynamic doc = null;

            try
            {
                doc = _app.OpenDocument(cdrPath, 0);
                MessageBox.Show("Document opened");

                int pageCount = Convert.ToInt32(doc.Pages.Count);
                MessageBox.Show("Page count: " + pageCount);

                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    page.Activate();

                    int shapeCount = Convert.ToInt32(page.Shapes.Count);
                    MessageBox.Show("Page " + p + " shapes: " + shapeCount);

                    for (int s = 1; s <= shapeCount; s++)
                    {
                        dynamic shape = page.Shapes[s];

                        string outFile = Path.Combine(
                            cacheRoot,
                            Path.GetFileNameWithoutExtension(cdrPath) + "_p" + p + "_s" + s + ".png"
                        );

                        try
                        {
                            shape.CreateSelection();
                            doc.ExportBitmap(outFile, 2, 1, 0, 800, 800).Finish();

                            if (File.Exists(outFile))
                            {
                                MessageBox.Show("Export OK:\n" + outFile);

                                results.Add(new ExportedDesign
                                {
                                    CdrPath = cdrPath,
                                    PreviewPath = outFile,
                                    PageNumber = p,
                                    ShapeNumber = s
                                });
                            }
                            else
                            {
                                MessageBox.Show("Export file missing:\n" + outFile);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Shape export error:\n" + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("CDR open/export error:\n" + ex.Message);
            }
            finally
            {
                try
                {
                    if (doc != null) doc.Close();
                }
                catch { }
            }

            MessageBox.Show("Export results count: " + results.Count);
            return results;
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
