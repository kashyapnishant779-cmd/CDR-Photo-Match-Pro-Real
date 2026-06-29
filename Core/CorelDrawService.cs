using System.Reflection;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

    Exception lastError = null;

    for (int i = 1; i <= 5; i++)
    {
        try
        {
            _app = Activator.CreateInstance(type);

            Thread.Sleep(3000);

            try
            {
                _app.Visible = true;
            }
            catch { }

            // COM connection test
            string version = Convert.ToString(_app.Version);

            WriteLog("CorelDRAW Version : " + version);
            WriteLog("COM Connected Successfully");

            return;
        }
        catch (Exception ex)
        {
            lastError = ex;
            WriteLog("CreateInstance Try " + i + " Failed : " + ex);

            try
            {
                _app = null;
            }
            catch { }

            Thread.Sleep(3000);
        }
    }

    throw new InvalidOperationException(
        "CorelDRAW COM start failed after 5 attempts.",
        lastError);
} 

        public IEnumerable<DesignRecord> ExportDesigns(string cdrPath, string cacheRoot)
        {
            var results = new List<DesignRecord>();
            Directory.CreateDirectory(cacheRoot);

            _debugLog = Path.Combine(cacheRoot, "export_debug.txt");
            WriteLog("START: " + cdrPath);

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

                        bool ok = ExportOneShape(doc, shapes[i].Shape, outFile);

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
                                ExportMode = "x4-debug-export",
                                ShapeCount = 1
                            });

                            exportedOnPage++;
                        }
                    }

                    if (exportedOnPage == 0)
                    {
                        string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_page.jpg");

                        bool ok = ExportWholePage(doc, page, outFile);

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
                                ExportMode = "x4-page-debug-export",
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

        private bool ExportOneShape(dynamic doc, dynamic shape, string outFile)
        {
            try
            {
                ClearSelection(doc);

                try { shape.CreateSelection(); }
                catch (Exception ex)
                {
                    WriteLog("CreateSelection failed: " + ex.Message);
                    return false;
                }

                Application.DoEvents();
                Thread.Sleep(400);

                return ExportSelectionAllMethods(doc, outFile);
            }
            catch (Exception ex)
            {
                WriteLog("ExportOneShape failed: " + ex);
                return false;
            }
        }

        private bool ExportWholePage(dynamic doc, dynamic page, string outFile)
        {
            try
            {
                ClearSelection(doc);

                try { page.Shapes.All().CreateSelection(); }
                catch (Exception ex)
                {
                    WriteLog("Page CreateSelection failed: " + ex.Message);
                    return false;
                }

                Application.DoEvents();
                Thread.Sleep(400);

                return ExportSelectionAllMethods(doc, outFile);
            }
            catch (Exception ex)
            {
                WriteLog("ExportWholePage failed: " + ex);
                return false;
            }
        }
         private bool ExportSelectionAllMethods(dynamic doc, string outFile)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outFile));

    try
    {
        dynamic sel = _app.ActiveSelection;
        int selCount = 0;
        try { selCount = Convert.ToInt32(sel.Shapes.Count); } catch { }

        WriteLog("Selection check count=" + selCount);

        if (selCount <= 0)
        {
            WriteLog("No active selection before export");
            return false;
        }

        if (ExportByTempDocument(doc, outFile))
            return true;

        WriteLog("Temp document export failed, trying current page fallback");

        return ExportCurrentPageX4(doc, outFile);
    }
    catch (Exception ex)
    {
        WriteLog("ExportSelectionAllMethods failed: " + ex);
        return false;
    }
}

private bool ExportByTempDocument(dynamic sourceDoc, string outFile)
{
    dynamic tempDoc = null;

    try
    {
        WriteLog("Temp document export start");

        try { sourceDoc.Application.ActiveSelection.Copy(); }
        catch { _app.ActiveSelection.Copy(); }

        Application.DoEvents();
        Thread.Sleep(500);

        tempDoc = sourceDoc.Application.CreateDocument();

        Application.DoEvents();
        Thread.Sleep(500);

        tempDoc.ActiveLayer.Paste();

        Application.DoEvents();
        Thread.Sleep(800);

        try { tempDoc.ActivePage.Shapes.All().CreateSelection(); } catch { }

        bool ok = ExportCurrentPageX4(tempDoc, outFile);

        try { tempDoc.Close(); } catch { }

        if (ok)
        {
            WriteLog("Temp document export OK");
            return true;
        }

        WriteLog("Temp document export invalid image");
        return false;
    }
    catch (Exception ex)
    {
        WriteLog("ExportByTempDocument failed: " + ex);
        try { if (tempDoc != null) tempDoc.Close(); } catch { }
        return false;
    }
}

private bool ExportCurrentPageX4(dynamic doc, string outFile)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }

        WriteLog("ExportBitmap current page start");

        dynamic exp = doc.ExportBitmap(
            outFile,
            772,
            2,
            4,
            1200,
            1200,
            96,
            96
        );

        try { exp.Finish(); } catch { }

        Application.DoEvents();
        Thread.Sleep(1500);

        if (IsValidImage(outFile))
        {
            WriteLog("ExportBitmap current page OK: " + outFile);
            return true;
        }

        WriteLog("ExportBitmap current page invalid image");
    }
    catch (Exception ex)
    {
        WriteLog("ExportCurrentPageX4 failed: " + ex);
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
                File.AppendAllText(_debugLog, DateTime.Now.ToString("HH:mm:ss") + " | " + text + Environment.NewLine, Encoding.UTF8);
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
