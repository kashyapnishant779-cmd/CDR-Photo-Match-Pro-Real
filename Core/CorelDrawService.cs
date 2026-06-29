using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CDRPhotoMatchPro.Core
{
    public sealed class CorelDrawService : IDisposable
    {
        private dynamic _app;
        private string _debugLog;

        private sealed class ShapeBox
        {
            public int Index;
            public double Width;
            public double Height;
            public dynamic Shape;
        }

        public CorelDrawService()
        {
            Type type = Type.GetTypeFromProgID("CorelDRAW.Application.14");

            if (type == null)
                type = Type.GetTypeFromProgID("CorelDRAW.Application");

            if (type == null)
                throw new InvalidOperationException("CorelDRAW X4 COM not found.");

            _app = Activator.CreateInstance(type);
            Thread.Sleep(3000);

            try { _app.Visible = true; } catch { }
        }

        public IEnumerable<DesignRecord> ExportDesigns(string cdrPath, string cacheRoot)
        {
            var results = new List<DesignRecord>();
            Directory.CreateDirectory(cacheRoot);

            _debugLog = Path.Combine(cacheRoot, "export_debug.txt");
            WriteLog("START NEW ENGINE: " + cdrPath);

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

                    MessageBox.Show(
                        "CDR: " + fileName + "\n" +
                        "Page: " + p + "\n" +
                        "Shapes detected: " + shapes.Count
                    );

                    int exportedOnPage = 0;

                    for (int i = 0; i < shapes.Count; i++)
                    {
                        int designNo = i + 1;
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_d" + designNo + ".jpg");

                        bool ok = ExportShapeX4(doc, shapes[i].Shape, outFile);

                        if (ok && IsValidImage(outFile))
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
                                ExportMode = "x4-direct-exportbitmap",
                                ShapeCount = 1
                            });

                            exportedOnPage++;
                        }
                    }

                    if (exportedOnPage == 0)
                    {
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_page.jpg");

                        bool ok = ExportPageX4(doc, page, outFile);

                        if (ok && IsValidImage(outFile))
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
                                ExportMode = "x4-page-exportbitmap",
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
                            "Shapes detected: " + shapes.Count + "\n\n" +
                            "Debug log:\n" + _debugLog
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("OPEN/EXPORT ERROR: " + ex);
                MessageBox.Show("CDR open/export error:\n\n" + ex);
            }
            finally
            {
                try { if (doc != null) doc.Close(); } catch { }
            }

            WriteLog("RESULTS: " + results.Count);
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

        private bool ExportShapeX4(dynamic doc, dynamic shape, string outFile)
        {
            try
            {
                ClearSelection(doc);

                shape.CreateSelection();
                Application.DoEvents();
                Thread.Sleep(500);

                return ExportSelectedToTempDoc(doc, outFile);
            }
            catch (Exception ex)
            {
                WriteLog("ExportShapeX4 failed: " + ex);
                return false;
            }
        }

        private bool ExportPageX4(dynamic doc, dynamic page, string outFile)
        {
            try
            {
                ClearSelection(doc);

                page.Shapes.All().CreateSelection();
                Application.DoEvents();
                Thread.Sleep(500);

                return ExportSelectedToTempDoc(doc, outFile);
            }
            catch (Exception ex)
            {
                WriteLog("ExportPageX4 failed: " + ex);
                return false;
            }
        }

        private bool ExportSelectedToTempDoc(dynamic sourceDoc, string outFile)
        {
            dynamic tempDoc = null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outFile));
                try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }

                WriteLog("TEMP EXPORT START: " + outFile);

                try
                {
                    _app.ActiveSelection.Copy();
                    WriteLog("Selection copied");
                }
                catch (Exception ex)
                {
                    WriteLog("Selection copy failed: " + ex.Message);
                    return false;
                }

                Application.DoEvents();
                Thread.Sleep(700);

                tempDoc = _app.CreateDocument();
                Application.DoEvents();
                Thread.Sleep(700);

                try
                {
                    tempDoc.ActiveLayer.Paste();
                    WriteLog("Pasted in temp document");
                }
                catch (Exception ex)
                {
                    WriteLog("Temp paste failed: " + ex.Message);
                    try { tempDoc.Close(); } catch { }
                    return false;
                }

                Application.DoEvents();
                Thread.Sleep(1000);

                try
                {
                    tempDoc.ActivePage.Shapes.All().CreateSelection();
                    WriteLog("Temp page selected");
                }
                catch { }

                bool ok = ExportCurrentPageBitmapX4(tempDoc, outFile);

                try { tempDoc.Close(); } catch { }

                if (ok)
                {
                    WriteLog("TEMP EXPORT OK");
                    return true;
                }

                WriteLog("TEMP EXPORT FAILED");
                return false;
            }
            catch (Exception ex)
            {
                WriteLog("ExportSelectedToTempDoc failed: " + ex);
                try { if (tempDoc != null) tempDoc.Close(); } catch { }
                return false;
            }
        }

        private bool ExportCurrentPageBitmapX4(dynamic doc, string outFile)
        {
            int[] filters = new int[] { 772, 774 };

            foreach (int filter in filters)
            {
                try
                {
                    try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }

                    WriteLog("ExportBitmap X4 EXACT start filter=" + filter);

                    dynamic exp = doc.ExportBitmap(
                        outFile,
                        filter,
                        1,
                        4,
                        1200,
                        1200,
                        300,
                        300
                    );

                    try { exp.Finish(); } catch { }

                    Application.DoEvents();
                    Thread.Sleep(2000);

                    if (IsValidImage(outFile))
                    {
                        WriteLog("ExportBitmap X4 EXACT OK: " + outFile);
                        return true;
                    }

                    WriteLog("ExportBitmap X4 EXACT invalid image filter=" + filter);
                }
                catch (Exception ex)
                {
                    WriteLog("ExportBitmap X4 EXACT failed filter=" + filter + " : " + ex);
                }
            }

            return false;
        }

        private void ClearSelection(dynamic doc)
        {
            try { doc.ClearSelection(); } catch { }
            try { _app.ActiveDocument.ClearSelection(); } catch { }
        }

        private bool IsValidImage(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 1000;
            }
            catch
            {
                return false;
            }
        }

        private void WriteLog(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(_debugLog))
                    return;

                File.AppendAllText(
                    _debugLog,
                    DateTime.Now.ToString("HH:mm:ss") + " | " + text + Environment.NewLine,
                    Encoding.UTF8
                );
            }
            catch { }
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
            try
            {
                if (_app != null)
                {
                    try { _app.Quit(); } catch { }
                }
            }
            catch { }
            finally
            {
                _app = null;
            }
        }
    }
}
