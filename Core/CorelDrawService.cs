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

                    string outFile = Path.Combine(
                        cacheRoot,
                        Path.GetFileNameWithoutExtension(cdrPath) + "_p" + p + ".jpg"
                    );

                    bool ok = ExportPageByClipboard(page, outFile);

                    if (ok && File.Exists(outFile))
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
                    else
                    {
                        MessageBox.Show("Page export failed:\n" + outFile);
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

        private bool ExportPageByClipboard(dynamic page, string outFile)
        {
            bool success = false;
            Exception error = null;

            Thread t = new Thread(() =>
            {
                try
                {
                    Clipboard.Clear();

                    page.Shapes.All().CreateSelection();

                    try
                    {
                        _app.ActiveSelection.Copy();
                    }
                    catch
                    {
                        page.Shapes.All().CreateSelection();
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
                MessageBox.Show("Clipboard export error:\n\n" + error.ToString());

            return success;
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
