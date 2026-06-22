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

            List<ShapeBox> boxes = GetUsableShapes(page);
            List<List<ShapeBox>> groups = BuildDesignGroups(boxes);

            int designNo = 0;
            int exportedOnPage = 0;

            foreach (List<ShapeBox> group in groups)
            {
                designNo++;

                string baseName = SafeName(Path.GetFileNameWithoutExtension(cdrPath));
                string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_d" + designNo + ".jpg");

                bool ok = ExportGroupByClipboard(doc, group, outFile);

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
                        ExportMode = "grouped-shapes",
                        ShapeCount = group.Count
                    });

                    exportedOnPage++;
                }
            }

            // Important fix:
            // Agar groups bane par export fail hua, tab bhi page fallback chalega.
            if (exportedOnPage == 0)
            {
                string baseName = SafeName(Path.GetFileNameWithoutExtension(cdrPath));
                string outFile = Path.Combine(cacheRoot, baseName + "_p" + p + "_page.jpg");

                bool ok = ExportPageByClipboard(doc, page, outFile);

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
                        ExportMode = "fallback-page",
                        ShapeCount = boxes.Count
                    });
                }
                else
                {
                    MessageBox.Show(
                        "Export failed for page:\n" +
                        cdrPath + "\n\n" +
                        "Page: " + p + "\n" +
                        "Shapes found: " + boxes.Count + "\n" +
                        "Groups found: " + groups.Count
                    );
                }
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
