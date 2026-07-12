using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CDRPhotoMatchPro.Core;
using CDRPhotoMatchPro.Data;
using CDRPhotoMatchPro.Imaging;

namespace CDRPhotoMatchPro.UI
{
    public sealed class MainForm : Form
    {
        private readonly string AppRoot =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                ),
                "CDRPhotoMatchPro"
            );

        private string DbPath
        {
            get
            {
                return Path.Combine(
                    AppRoot,
                    "cdr_index.sqlite"
                );
            }
        }

        private string CachePath
        {
            get
            {
                return Path.Combine(
                    AppRoot,
                    "thumb_cache"
                );
            }
        }

        private string QueryDebugPath
        {
            get
            {
                return Path.Combine(
                    AppRoot,
                    "query_debug"
                );
            }
        }

        private TabControl tabs;
        private TabControl previewTabs;

        private TextBox imagePath;
        private TextBox scanRoot;
        private TextBox thresholdBox;

        private DataGridView grid;

        private PictureBox originalPreview;
        private PictureBox cropPreview;
        private PictureBox lineArtPreview;
        private PictureBox resultPreview;

        private Label status;
        private CancellationTokenSource cts;

        private Button openCdrBtn;
        private Button openFolderBtn;
        private Button copyPathBtn;

        public MainForm()
        {
            Text = "CDR Photo Match Pro";
            Width = 1250;
            Height = 720;
            StartPosition =
                FormStartPosition.CenterScreen;

            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(CachePath);
            Directory.CreateDirectory(QueryDebugPath);

            using (var db =
                new Database(DbPath))
            {
            }

            BuildUi();

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                DisposePicture(
                    originalPreview
                );

                DisposePicture(
                    cropPreview
                );

                DisposePicture(
                    lineArtPreview
                );

                DisposePicture(
                    resultPreview
                );

                if (cts != null)
                {
                    cts.Dispose();
                    cts = null;
                }
            }

            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            tabs =
                new TabControl
                {
                    Dock = DockStyle.Fill
                };

            Controls.Add(tabs);

            BuildSearchTab();
            BuildScanTab();
            BuildIndexTab();
            BuildSettingsTab();

            status =
                new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 28,
                    Text = "Ready",
                    BorderStyle =
                        BorderStyle.Fixed3D
                };

            Controls.Add(status);
        }

        private void BuildSearchTab()
        {
            var page =
                new TabPage("Search");

            tabs.TabPages.Add(page);

            var top =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 46
                };

            page.Controls.Add(top);

            imagePath =
                new TextBox
                {
                    Left = 10,
                    Top = 12,
                    Width = 620
                };

            top.Controls.Add(imagePath);

            var browse =
                new Button
                {
                    Text = "Browse Image",
                    Left = 640,
                    Top = 10,
                    Width = 110
                };

            browse.Click +=
                delegate
                {
                    PickImage();
                };

            top.Controls.Add(browse);

            var search =
                new Button
                {
                    Text = "Search",
                    Left = 760,
                    Top = 10,
                    Width = 90
                };

            search.Click +=
                delegate
                {
                    SearchImage();
                };

            top.Controls.Add(search);

            var bottom =
                new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 42
                };

            page.Controls.Add(bottom);

            openCdrBtn =
                new Button
                {
                    Text = "Open CDR",
                    Left = 10,
                    Top = 8,
                    Width = 110
                };

            openCdrBtn.Click +=
                delegate
                {
                    OpenSelectedCdr();
                };

            bottom.Controls.Add(openCdrBtn);

            openFolderBtn =
                new Button
                {
                    Text = "Open Folder",
                    Left = 130,
                    Top = 8,
                    Width = 120
                };

            openFolderBtn.Click +=
                delegate
                {
                    OpenSelectedFolder();
                };

            bottom.Controls.Add(openFolderBtn);

            copyPathBtn =
                new Button
                {
                    Text = "Copy Full Path",
                    Left = 260,
                    Top = 8,
                    Width = 130
                };

            copyPathBtn.Click +=
                delegate
                {
                    CopySelectedPath();
                };

            bottom.Controls.Add(copyPathBtn);

            previewTabs =
                new TabControl
                {
                    Dock = DockStyle.Right,
                    Width = 350
                };

            page.Controls.Add(previewTabs);

            originalPreview =
                CreatePreviewPictureBox();

            cropPreview =
                CreatePreviewPictureBox();

            lineArtPreview =
                CreatePreviewPictureBox();

            resultPreview =
                CreatePreviewPictureBox();

            AddPreviewTab(
                "Original JPG",
                originalPreview
            );

            AddPreviewTab(
                "Extracted Crop",
                cropPreview
            );

            AddPreviewTab(
                "Line Art",
                lineArtPreview
            );

            AddPreviewTab(
                "Result Preview",
                resultPreview
            );

            grid =
                new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AutoGenerateColumns = false,
                    AllowUserToAddRows = false,
                    SelectionMode =
                        DataGridViewSelectionMode
                            .FullRowSelect,
                    MultiSelect = false,
                    AutoSizeRowsMode =
                        DataGridViewAutoSizeRowsMode
                            .None
                };

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Match %",
                    DataPropertyName =
                        "MatchPercent",
                    Width = 80
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "CDR File",
                    DataPropertyName =
                        "CdrFileName",
                    Width = 140
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText =
                        "Full CDR Path",
                    DataPropertyName =
                        "CdrPath",
                    Width = 430
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Page",
                    DataPropertyName =
                        "PageNumber",
                    Width = 55
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Design No",
                    DataPropertyName =
                        "DesignNumber",
                    Width = 75
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Mode",
                    DataPropertyName =
                        "ExportMode",
                    Width = 80
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Shapes",
                    DataPropertyName =
                        "ShapeCount",
                    Width = 60
                }
            );

            grid.DoubleClick +=
                OnGridDoubleClick;

            grid.SelectionChanged +=
                OnGridSelectionChanged;

            page.Controls.Add(grid);
        }

        private PictureBox
            CreatePreviewPictureBox()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode =
                    PictureBoxSizeMode.Zoom,
                BorderStyle =
                    BorderStyle.FixedSingle,
                BackColor = Color.White
            };
        }

        private void AddPreviewTab(
            string title,
            PictureBox pictureBox)
        {
            var page =
                new TabPage(title);

            page.Controls.Add(pictureBox);
            previewTabs.TabPages.Add(page);
        }

        private void BuildScanTab()
        {
            var page =
                new TabPage("Scan");

            tabs.TabPages.Add(page);

            scanRoot =
                new TextBox
                {
                    Left = 20,
                    Top = 25,
                    Width = 500,
                    Text = "D:\\"
                };

            page.Controls.Add(scanRoot);

            var start =
                new Button
                {
                    Text =
                        "Start Incremental Scan",
                    Left = 540,
                    Top = 23,
                    Width = 160
                };

            start.Click +=
                delegate
                {
                    StartScan(false);
                };

            page.Controls.Add(start);

            var rescan =
                new Button
                {
                    Text = "Full Rescan",
                    Left = 710,
                    Top = 23,
                    Width = 110
                };

            rescan.Click +=
                delegate
                {
                    StartScan(true);
                };

            page.Controls.Add(rescan);

            var cancel =
                new Button
                {
                    Text = "Cancel",
                    Left = 830,
                    Top = 23,
                    Width = 90
                };

            cancel.Click +=
                delegate
                {
                    if (cts != null)
                        cts.Cancel();
                };

            page.Controls.Add(cancel);

            page.Controls.Add(
                new Label
                {
                    Left = 20,
                    Top = 75,
                    Width = 950,
                    Height = 140,
                    Text =
                        "This scans CDR files recursively, exports design thumbnails, stores full CDR path + page number + design number."
                }
            );
        }

        private void BuildIndexTab()
        {
            var page =
                new TabPage("Index");

            tabs.TabPages.Add(page);

            var openDb =
                new Button
                {
                    Text =
                        "Open Database Folder",
                    Left = 20,
                    Top = 25,
                    Width = 180
                };

            openDb.Click +=
                delegate
                {
                    Process.Start(AppRoot);
                };

            page.Controls.Add(openDb);

            var openCache =
                new Button
                {
                    Text =
                        "Open Thumbnail Cache",
                    Left = 220,
                    Top = 25,
                    Width = 180
                };

            openCache.Click +=
                delegate
                {
                    Process.Start(CachePath);
                };

            page.Controls.Add(openCache);

            var openQueryDebug =
                new Button
                {
                    Text =
                        "Open Query Debug",
                    Left = 420,
                    Top = 25,
                    Width = 160
                };

            openQueryDebug.Click +=
                delegate
                {
                    Directory.CreateDirectory(
                        QueryDebugPath
                    );

                    Process.Start(
                        QueryDebugPath
                    );
                };

            page.Controls.Add(
                openQueryDebug
            );

            var count =
                new Button
                {
                    Text =
                        "Show Indexed Count",
                    Left = 590,
                    Top = 25,
                    Width = 150
                };

            count.Click +=
                delegate
                {
                    using (var db =
                        new Database(DbPath))
                    {
                        MessageBox.Show(
                            "Indexed designs: " +
                            db.LoadDesigns().Count
                        );
                    }
                };

            page.Controls.Add(count);
        }

        private void BuildSettingsTab()
        {
            var page =
                new TabPage("Settings");

            tabs.TabPages.Add(page);

            page.Controls.Add(
                new Label
                {
                    Left = 20,
                    Top = 28,
                    Width = 180,
                    Text =
                        "Minimum match %"
                }
            );

            thresholdBox =
                new TextBox
                {
                    Left = 210,
                    Top = 24,
                    Width = 80,
                    Text = "45"
                };

            page.Controls.Add(thresholdBox);

            page.Controls.Add(
                new Label
                {
                    Left = 20,
                    Top = 65,
                    Width = 850,
                    Text =
                        "45 recommended. Lower value = more possible matches. Higher value = stricter exact match."
                }
            );
        }

        private void PickImage()
        {
            using (var ofd =
                new OpenFileDialog
                {
                    Filter =
                        "Images|*.jpg;*.jpeg;*.png;*.bmp"
                })
            {
                if (ofd.ShowDialog(this) ==
                    DialogResult.OK)
                {
                    LoadSelectedQueryImage(
                        ofd.FileName
                    );
                }
            }
        }

        private void LoadSelectedQueryImage(
            string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !File.Exists(path))
            {
                return;
            }

            imagePath.Text = path;

            SetPictureFromFile(
                originalPreview,
                path
            );

            ClearPicture(cropPreview);
            ClearPicture(lineArtPreview);
            ClearPicture(resultPreview);

            previewTabs.SelectedIndex = 0;

            status.Text =
                "Image selected. Press Search.";
        }

        private void SearchImage()
        {
            if (!File.Exists(imagePath.Text))
            {
                MessageBox.Show(
                    "Select a valid image."
                );

                return;
            }

            status.Text =
                "Extracting jewellery and removing background...";

            Application.DoEvents();

            string originalDebugPath =
                Path.Combine(
                    QueryDebugPath,
                    "01_original.png"
                );

            string cropDebugPath =
                Path.Combine(
                    QueryDebugPath,
                    "02_extracted_crop.png"
                );

            string silhouetteDebugPath =
                Path.Combine(
                    QueryDebugPath,
                    "03_silhouette.png"
                );

            string lineArtDebugPath =
                Path.Combine(
                    QueryDebugPath,
                    "04_line_art.png"
                );

            string preprocessMethod =
                "UNKNOWN";

            double preprocessConfidence = 0;
            bool usedFallback = true;

            ImagePreprocessResult processed = null;

            try
            {
                Directory.CreateDirectory(
                    QueryDebugPath
                );

                using (Bitmap source =
                    new Bitmap(imagePath.Text))
                {
                    SaveBitmapSafe(
                        source,
                        originalDebugPath
                    );

                    processed =
                        ImagePreprocessor.Process(
                            source
                        );
                }

                if (processed != null)
                {
                    preprocessMethod =
                        string.IsNullOrEmpty(
                            processed.Method
                        )
                            ? "UNKNOWN"
                            : processed.Method;

                    preprocessConfidence =
                        processed.Confidence;

                    usedFallback =
                        processed.UsedFallback;

                    if (processed.CroppedOriginal !=
                        null)
                    {
                        SaveBitmapSafe(
                            processed.CroppedOriginal,
                            cropDebugPath
                        );

                        SetPictureFromBitmap(
                            cropPreview,
                            processed.CroppedOriginal
                        );
                    }
                    else
                    {
                        ClearPicture(
                            cropPreview
                        );
                    }

                    if (processed.Silhouette !=
                        null)
                    {
                        SaveBitmapSafe(
                            processed.Silhouette,
                            silhouetteDebugPath
                        );
                    }

                    if (processed.LineArt != null)
                    {
                        SaveBitmapSafe(
                            processed.LineArt,
                            lineArtDebugPath
                        );

                        SetPictureFromBitmap(
                            lineArtPreview,
                            processed.LineArt
                        );
                    }
                    else
                    {
                        ClearPicture(
                            lineArtPreview
                        );
                    }
                }

                previewTabs.SelectedIndex = 2;
            }
            catch (Exception ex)
            {
                ClearPicture(cropPreview);
                ClearPicture(lineArtPreview);

                preprocessMethod =
                    "PREPROCESS-ERROR";

                preprocessConfidence = 0;
                usedFallback = true;

                MessageBox.Show(
                    "Preprocessing error:\r\n" +
                    ex.Message
                );
            }
            finally
            {
                if (processed != null)
                    processed.Dispose();
            }

            status.Text =
                "Extracted: " +
                preprocessMethod +
                " | Confidence: " +
                (
                    preprocessConfidence *
                    100.0
                ).ToString("0.0") +
                "%" +
                (
                    usedFallback
                        ? " | FALLBACK USED"
                        : ""
                ) +
                " | Searching...";

            Application.DoEvents();

            double threshold;

            if (!double.TryParse(
                    thresholdBox.Text,
                    out threshold
                ))
            {
                threshold = 60;
            }

            if (threshold < 60)
                threshold = 60;

            var matcher =
                new ImageMatcher();

            var results =
                new List<MatchResult>();

            byte[] query =
                matcher.ExtractDescriptorBytes(
                    imagePath.Text
                );

            using (var db =
                new Database(DbPath))
            {
                var designs =
                    db.LoadDesigns();

                foreach (var design in designs)
                {
                    if (design.Descriptor == null ||
                        design.Descriptor.Length == 0)
                    {
                        continue;
                    }

                    double score =
                        matcher.Compare(
                            query,
                            design.Descriptor
                        );

                    string mode =
                        design.ExportMode == null
                            ? ""
                            : design.ExportMode
                                .ToUpperInvariant();

                    if (mode ==
                        "FULL-PAGE-HD")
                    {
                        score += 5;
                    }

                    if (mode == "OBJECT-HD" &&
                        design.ShapeCount <= 1)
                    {
                        score -= 12;
                    }

                    if (mode == "GROUP-HD")
                        score += 3;

                    if (score > 100)
                        score = 100;

                    if (score < 0)
                        score = 0;

                    results.Add(
                        new MatchResult
                        {
                            MatchPercent =
                                Math.Round(
                                    score,
                                    2
                                ),

                            CdrFileName =
                                design.FileName,

                            FullFolderPath =
                                design.FolderPath,

                            CdrPath =
                                design.CdrPath,

                            PageNumber =
                                design.PageNumber,

                            DesignNumber =
                                design.DesignNumber,

                            ObjectNumber =
                                design.DesignNumber,

                            ThumbnailPath =
                                design.ThumbnailPath,

                            PngPath =
                                design.PngPath,

                            ExportMode =
                                design.ExportMode,

                            ShapeCount =
                                design.ShapeCount
                        }
                    );
                }
            }

            var top =
                results
                    .GroupBy(
                        item =>
                            (
                                item.CdrPath ?? ""
                            ) +
                            "|" +
                            item.PageNumber
                    )
                    .Select(
                        group =>
                            group
                                .OrderByDescending(
                                    item =>
                                        item.MatchPercent
                                )
                                .ThenByDescending(
                                    item =>
                                        item.ShapeCount
                                )
                                .First()
                    )
                    .OrderByDescending(
                        item =>
                            item.MatchPercent
                    )
                    .Take(50)
                    .ToList();

            grid.DataSource = top;

            if (top.Count == 0)
            {
                status.Text =
                    "NO RESULT | Extracted: " +
                    preprocessMethod +
                    " | Confidence: " +
                    (
                        preprocessConfidence *
                        100.0
                    ).ToString("0.0") +
                    "%";

                previewTabs.SelectedIndex = 2;
            }
            else
            {
                status.Text =
                    "Extracted: " +
                    preprocessMethod +
                    " | Confidence: " +
                    (
                        preprocessConfidence *
                        100.0
                    ).ToString("0.0") +
                    "% | Best match: " +
                    top[0].MatchPercent +
                    "% | " +
                    top[0].CdrPath +
                    " | Page " +
                    top[0].PageNumber +
                    " | Design " +
                    top[0].DesignNumber +
                    " | Mode " +
                    top[0].ExportMode;

                /*
                 * Line Art tab par hi rehne do,
                 * taaki extraction clearly dikhe.
                 * Result Preview tab user manually
                 * click kar sakta hai.
                 */

                previewTabs.SelectedIndex = 2;
            }
        }

        private async void StartScan(
            bool full)
        {
            if (!Directory.Exists(
                    scanRoot.Text
                ))
            {
                MessageBox.Show(
                    "Scan folder not found."
                );

                return;
            }

            if (cts != null)
            {
                cts.Dispose();
                cts = null;
            }

            cts =
                new CancellationTokenSource();

            var indexer =
                new Indexer(
                    DbPath,
                    CachePath
                );

            var progress =
                new Progress<IndexProgress>(
                    item =>
                    {
                        status.Text =
                            item.CurrentFile +
                            "/" +
                            item.TotalFiles +
                            " " +
                            item.Message;
                    }
                );

            try
            {
                status.Text =
                    full
                        ? "Full rescan started..."
                        : "Incremental scan started...";

                await indexer.RunAsync(
                    scanRoot.Text,
                    full,
                    progress,
                    cts.Token
                );

                status.Text =
                    "Indexing complete";
            }
            catch (
                OperationCanceledException
            )
            {
                status.Text =
                    "Indexing cancelled";
            }
            catch (Exception ex)
            {
                status.Text =
                    "Indexing error";

                MessageBox.Show(
                    ex.ToString()
                );
            }
        }

        private MatchResult SelectedItem()
        {
            return grid.CurrentRow == null
                ? null
                : grid.CurrentRow
                    .DataBoundItem
                    as MatchResult;
        }

        private void OnGridSelectionChanged(
            object sender,
            EventArgs e)
        {
            var item =
                SelectedItem();

            if (item == null)
                return;

            string image =
                item.PngPath;

            if (string.IsNullOrEmpty(image))
                image = item.ThumbnailPath;

            if (File.Exists(image))
            {
                SetPictureFromFile(
                    resultPreview,
                    image
                );
            }
            else
            {
                ClearPicture(
                    resultPreview
                );
            }
        }

        private void OpenSelectedCdr()
        {
            var item =
                SelectedItem();

            if (item != null &&
                File.Exists(item.CdrPath))
            {
                Process.Start(
                    item.CdrPath
                );
            }
        }

        private void OpenSelectedFolder()
        {
            var item =
                SelectedItem();

            if (item == null)
                return;

            string folder =
                item.FullFolderPath;

            if (string.IsNullOrEmpty(folder) &&
                !string.IsNullOrEmpty(
                    item.CdrPath
                ))
            {
                folder =
                    Path.GetDirectoryName(
                        item.CdrPath
                    );
            }

            if (Directory.Exists(folder))
            {
                Process.Start(folder);
            }
        }

        private void CopySelectedPath()
        {
            var item =
                SelectedItem();

            if (item != null &&
                !string.IsNullOrEmpty(
                    item.CdrPath
                ))
            {
                Clipboard.SetText(
                    item.CdrPath
                );

                status.Text =
                    "Copied: " +
                    item.CdrPath;
            }
        }

        private void OnGridDoubleClick(
            object sender,
            EventArgs e)
        {
            OpenSelectedCdr();
        }

        private void OnDragEnter(
            object sender,
            DragEventArgs e)
        {
            if (e.Data.GetDataPresent(
                    DataFormats.FileDrop
                ))
            {
                e.Effect =
                    DragDropEffects.Copy;
            }
        }

        private void OnDragDrop(
            object sender,
            DragEventArgs e)
        {
            var files =
                (string[])e.Data.GetData(
                    DataFormats.FileDrop
                );

            if (files != null &&
                files.Length > 0)
            {
                LoadSelectedQueryImage(
                    files[0]
                );

                tabs.SelectedIndex = 0;
            }
        }

        private static void SaveBitmapSafe(
            Bitmap bitmap,
            string path)
        {
            if (bitmap == null ||
                string.IsNullOrEmpty(path))
            {
                return;
            }

            string directory =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(
                    directory
                ))
            {
                Directory.CreateDirectory(
                    directory
                );
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }

            bitmap.Save(
                path,
                ImageFormat.Png
            );
        }

        private static void
            SetPictureFromFile(
                PictureBox pictureBox,
                string path)
        {
            if (pictureBox == null)
                return;

            ClearPicture(pictureBox);

            if (string.IsNullOrEmpty(path) ||
                !File.Exists(path))
            {
                return;
            }

            try
            {
                using (Image source =
                    Image.FromFile(path))
                {
                    pictureBox.Image =
                        new Bitmap(source);
                }
            }
            catch
            {
                ClearPicture(pictureBox);
            }
        }

        private static void
            SetPictureFromBitmap(
                PictureBox pictureBox,
                Bitmap bitmap)
        {
            if (pictureBox == null)
                return;

            ClearPicture(pictureBox);

            if (bitmap == null)
                return;

            try
            {
                pictureBox.Image =
                    new Bitmap(bitmap);
            }
            catch
            {
                ClearPicture(pictureBox);
            }
        }

        private static void ClearPicture(
            PictureBox pictureBox)
        {
            if (pictureBox == null)
                return;

            Image previous =
                pictureBox.Image;

            pictureBox.Image = null;
            pictureBox.ImageLocation = null;

            if (previous != null)
            {
                try
                {
                    previous.Dispose();
                }
                catch
                {
                }
            }
        }

        private static void DisposePicture(
            PictureBox pictureBox)
        {
            if (pictureBox == null)
                return;

            ClearPicture(pictureBox);

            try
            {
                pictureBox.Dispose();
            }
            catch
            {
            }
        }
    }
}
