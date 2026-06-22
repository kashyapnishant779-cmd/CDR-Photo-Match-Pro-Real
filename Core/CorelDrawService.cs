private bool ExportGroupByClipboard(dynamic doc, List<ShapeBox> group, string outFile)
{
    try
    {
        try { Clipboard.Clear(); } catch { }
        try { doc.ClearSelection(); } catch { }
        try { _app.ActiveDocument.ClearSelection(); } catch { }

        int selected = 0;

        for (int i = 0; i < group.Count; i++)
        {
            try
            {
                if (selected == 0)
                    group[i].Shape.CreateSelection();
                else
                    group[i].Shape.AddToSelection();

                selected++;
            }
            catch { }
        }

        Application.DoEvents();
        Thread.Sleep(300);

        if (selected <= 0)
        {
            MessageBox.Show("No shapes selected for export.");
            return false;
        }

        // Pehle Corel native export try karo
        if (ExportSelectionNative(doc, outFile))
            return true;

        // Agar native fail ho to clipboard fallback
        try
        {
            SendKeys.SendWait("^c");
            Application.DoEvents();
            Thread.Sleep(500);
        }
        catch { }

        bool ok = SaveClipboardImage(outFile);

        if (!ok)
            MessageBox.Show("Export failed:\nSelected shapes: " + selected + "\nClipboard image: No\nFile: " + outFile);

        return ok;
    }
    catch (Exception ex)
    {
        MessageBox.Show("ExportGroup error:\n" + ex.Message);
        return false;
    }
}

private bool ExportPageByClipboard(dynamic doc, dynamic page, string outFile)
{
    try
    {
        try { Clipboard.Clear(); } catch { }
        try { doc.ClearSelection(); } catch { }

        try { page.Shapes.All().CreateSelection(); }
        catch { }

        Application.DoEvents();
        Thread.Sleep(300);

        if (ExportSelectionNative(doc, outFile))
            return true;

        try
        {
            SendKeys.SendWait("^c");
            Application.DoEvents();
            Thread.Sleep(500);
        }
        catch { }

        bool ok = SaveClipboardImage(outFile);

        if (!ok)
            MessageBox.Show("Page export failed:\nClipboard image: No\nFile: " + outFile);

        return ok;
    }
    catch (Exception ex)
    {
        MessageBox.Show("ExportPage error:\n" + ex.Message);
        return false;
    }
}

private bool ExportSelectionNative(dynamic doc, string outFile)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));

        // CorelDRAW constants numeric:
        // 774 = JPEG filter
        // 1 = Selection only
        // 2 = RGB color image
        // 1 = Normal anti aliasing
        dynamic filter = doc.ExportBitmap(
            outFile,
            774,
            1,
            2,
            1200,
            1200,
            150,
            150,
            1,
            false,
            true,
            true,
            false
        );

        try { filter.Finish(); } catch { }

        return File.Exists(outFile) && new FileInfo(outFile).Length > 1000;
    }
    catch
    {
        return false;
    }
}
