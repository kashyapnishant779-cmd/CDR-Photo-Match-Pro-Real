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

                    string outFile = Path.Combine(
                        cacheRoot,
                        Path.GetFileNameWithoutExtension(cdrPath) + "_p" + p + ".jpg"
                    );

                    try
                    {
                        dynamic export = doc.ExportBitmap(
                            outFile,
                            774,
                            1,
                            1200,
                            1200
                        );

                        export.Finish();

                        if (File.Exists(outFile))
                        {
                            results.Add(new DesignRecord
                            {
                                CdrPath = cdrPath,
                                FileName = fileName,
                                FolderPath = folderPath,
                                PageNumber = p,
                                ObjectNumber = 0,
                                ThumbnailPath = outFile,
                                PngPath = outFile
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Page export error:\n\n" +
                            ex.ToString()
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("CDR open/export error:\n" + ex.Message);
            }
            finally
            {
                try { if (doc != null) doc.Close(); } catch { }
            }

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
