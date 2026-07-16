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

        private string ManualCropPath
        {
            get
            {
                return Path.Combine(
                    QueryDebugPath,
                    "manual_crop.png"
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

        private Button selectAreaButton;
        private Button clearAreaButton;
        private Button searchButton;
        private Button openCdrBtn;
        private Button openFolderBtn;
        private Button copyPathBtn;

        private Bitmap selectedCropBitmap;
        private string selectedCropSourcePath;
        private bool searchInProgress;

        public MainForm()
        {
            Text = "CDR Photo Match Pro";
            Width = 1280;
            Height = 740;
            StartPosition =
                FormStartPosition.CenterScreen;

            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(CachePath);
            Directory.CreateDirectory(QueryDebugPath);

            using (var db = new Database(DbPath))
            {
            }

            BuildUi();

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeSelectedCrop();

                ClearPicture(originalPreview);
                ClearPicture(cropPreview);
                ClearPicture(lineArtPreview);
                ClearPicture(resultPreview);

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
            tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };

            Controls.Add(tabs);

            BuildSearchTab();
            BuildScanTab();
            BuildIndexTab();
            BuildSettingsTab();

            status = new Label
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
            var page = new TabPage("Search");
            tabs.TabPages.Add(page);

            var top = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78
            };

            page.Controls.Add(top);

            imagePath = new TextBox
            {
                Left = 10,
                Top = 12,
                Width = 290,
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Left
            };

            top.Controls.Add(imagePath);

            var browse = new Button
            {
                Text = "Browse Image",
                Left = 310,
                Top = 10,
                Width = 95,
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Left
            };

            browse.Click += delegate
            {
                PickImage();
            };

            top.Controls.Add(browse);

            selectAreaButton = new Button
            {
                Text = "Select Jewellery Area",
                Left = 415,
                Top = 10,
                Width = 130,
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Left
            };

            selectAreaButton.Click += delegate
            {
                SelectJewelleryArea();
            };

            top.Controls.Add(selectAreaButton);

            clearAreaButton = new Button
            {
                Text = "Clear Crop",
                Left = 555,
                Top = 10,
                Width = 65,
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Left
            };

            clearAreaButton.Click += delegate
            {
                ClearManualCrop();
            };

            top.Controls.Add(clearAreaButton);

            searchButton = new Button
            {
                Text = "Search",
                Left = 630,
                Top = 10,
                Width = 75,
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Left
            };

            searchButton.Click += delegate
            {
                SearchImage();
            };

            top.Controls.Add(searchButton);

            AcceptButton = searchButton;

            top.Controls.Add(
                new Label
                {
                    Left = 10,
                    Top = 48,
                    Width = 690,
                    Height = 22,
                    Text =
                        "Best result ke liye Browse Image ke baad Select Jewellery Area dabao aur sirf jewellery ke around box banao."
                }
            );

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 42
            };

            page.Controls.Add(bottom);

            openCdrBtn = new Button
            {
                Text = "Open CDR",
                Left = 10,
                Top = 8,
                Width = 110
            };

            openCdrBtn.Click += delegate
            {
                OpenSelectedCdr();
            };

            bottom.Controls.Add(openCdrBtn);

            openFolderBtn = new Button
            {
                Text = "Open Folder",
                Left = 130,
                Top = 8,
                Width = 120
            };

            openFolderBtn.Click += delegate
            {
                OpenSelectedFolder();
            };

            bottom.Controls.Add(openFolderBtn);

            copyPathBtn = new Button
            {
                Text = "Copy Full Path",
                Left = 260,
                Top = 8,
                Width = 130
            };

            copyPathBtn.Click += delegate
            {
                CopySelectedPath();
            };

            bottom.Controls.Add(copyPathBtn);

            previewTabs = new TabControl
            {
                Dock = DockStyle.Right,
                Width = 370
            };

            page.Controls.Add(previewTabs);

            originalPreview = CreatePreviewPictureBox();
            cropPreview = CreatePreviewPictureBox();
            lineArtPreview = CreatePreviewPictureBox();
            resultPreview = CreatePreviewPictureBox();

            AddPreviewTab(
                "Original JPG",
                originalPreview
            );

            AddPreviewTab(
                "Selected Crop",
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

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                SelectionMode =
                    DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeRowsMode =
                    DataGridViewAutoSizeRowsMode.None
            };

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Match %",
                    DataPropertyName = "MatchPercent",
                    Width = 80
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "CDR File",
                    DataPropertyName = "CdrFileName",
                    Width = 140
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Full CDR Path",
                    DataPropertyName = "CdrPath",
                    Width = 400
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Page",
                    DataPropertyName = "PageNumber",
                    Width = 55
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Design No",
                    DataPropertyName = "DesignNumber",
                    Width = 75
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Mode",
                    DataPropertyName = "ExportMode",
                    Width = 80
                }
            );

            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Shapes",
                    DataPropertyName = "ShapeCount",
                    Width = 60
                }
            );

            grid.DoubleClick += OnGridDoubleClick;
            grid.SelectionChanged +=
                OnGridSelectionChanged;

            page.Controls.Add(grid);
        }

        private static PictureBox CreatePreviewPictureBox()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle =
                    BorderStyle.FixedSingle,
                BackColor = Color.White
            };
        }

        private void AddPreviewTab(
            string title,
            PictureBox pictureBox)
        {
            var page = new TabPage(title);
            page.Controls.Add(pictureBox);
            previewTabs.TabPages.Add(page);
        }

        private void BuildScanTab()
        {
            var page = new TabPage("Scan");
            tabs.TabPages.Add(page);

            scanRoot = new TextBox
            {
                Left = 20,
                Top = 25,
                Width = 500,
                Text = "D:\\"
            };

            page.Controls.Add(scanRoot);

            var start = new Button
            {
                Text = "Start Incremental Scan",
                Left = 415,
                Top = 23,
                Width = 160
            };

            start.Click += delegate
            {
                StartScan(false);
            };

            page.Controls.Add(start);

            var rescan = new Button
            {
                Text = "Full Rescan",
                Left = 710,
                Top = 23,
                Width = 110
            };

            rescan.Click += delegate
            {
                StartScan(true);
            };

            page.Controls.Add(rescan);

            var cancel = new Button
            {
                Text = "Cancel",
                Left = 830,
                Top = 23,
                Width = 90
            };

            cancel.Click += delegate
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
            var page = new TabPage("Index");
            tabs.TabPages.Add(page);

            var openDb = new Button
            {
                Text = "Open Database Folder",
                Left = 20,
                Top = 25,
                Width = 180
            };

            openDb.Click += delegate
            {
                Process.Start(AppRoot);
            };

            page.Controls.Add(openDb);

            var openCache = new Button
            {
                Text = "Open Thumbnail Cache",
                Left = 220,
                Top = 25,
                Width = 180
            };

            openCache.Click += delegate
            {
                Process.Start(CachePath);
            };

            page.Controls.Add(openCache);

            var openDebug = new Button
            {
                Text = "Open Query Debug",
                Left = 420,
                Top = 25,
                Width = 160
            };

            openDebug.Click += delegate
            {
                Directory.CreateDirectory(
                    QueryDebugPath
                );

                Process.Start(QueryDebugPath);
            };

            page.Controls.Add(openDebug);

            var count = new Button
            {
                Text = "Show Indexed Count",
                Left = 590,
                Top = 25,
                Width = 150
            };

            count.Click += delegate
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
            var page = new TabPage("Settings");
            tabs.TabPages.Add(page);

            page.Controls.Add(
                new Label
                {
                    Left = 20,
                    Top = 28,
                    Width = 180,
                    Text = "Minimum match %"
                }
            );

            thresholdBox = new TextBox
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
                    Width = 900,
                    Text =
                        "Abhi sab top results dikhaye jaate hain. Match % ranking check karne ke liye hai."
                }
            );
        }

        private void PickImage()
        {
            using (var dialog =
                new OpenFileDialog
                {
                    Filter =
                        "Images|*.jpg;*.jpeg;*.png;*.bmp"
                })
            {
                if (dialog.ShowDialog(this) ==
                    DialogResult.OK)
                {
                    LoadSelectedQueryImage(
                        dialog.FileName
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

            DisposeSelectedCrop();

            SetPictureFromFile(
                originalPreview,
                path
            );

            ClearPicture(cropPreview);
            ClearPicture(lineArtPreview);
            ClearPicture(resultPreview);

            grid.DataSource = null;

            previewTabs.SelectedIndex = 0;

            status.Text =
                "Image selected. Ab Select Jewellery Area dabao.";
        }

        private void SelectJewelleryArea()
        {
            if (string.IsNullOrEmpty(
                    imagePath.Text
                ) ||
                !File.Exists(imagePath.Text))
            {
                MessageBox.Show(
                    "Pehle valid JPG select karo."
                );

                return;
            }

            Bitmap source = null;

            try
            {
                source =
                    new Bitmap(imagePath.Text);

                using (var dialog =
                    new CropSelectionForm(source))
                {
                    if (dialog.ShowDialog(this) !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    Bitmap crop =
                        dialog.TakeSelectedCrop();

                    if (crop == null)
                    {
                        MessageBox.Show(
                            "Selection nahi bani. Jewellery ke around mouse se box banao."
                        );

                        return;
                    }

                    DisposeSelectedCrop();

                    selectedCropBitmap = crop;
                    selectedCropSourcePath =
                        imagePath.Text;

                    Directory.CreateDirectory(
                        QueryDebugPath
                    );

                    SaveBitmapSafe(
                        selectedCropBitmap,
                        ManualCropPath
                    );

                    SetPictureFromBitmap(
                        cropPreview,
                        selectedCropBitmap
                    );

                    previewTabs.SelectedIndex = 1;

                    status.Text =
                        "Manual jewellery crop ready. Search automatically start ho rahi hai...";

                    BeginInvoke(
                        new Action(
                            delegate
                            {
                                SearchImage();
                            }
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Crop selection error:\r\n" +
                    ex.Message
                );
            }
            finally
            {
                if (source != null)
                    source.Dispose();
            }
        }

        private void ClearManualCrop()
        {
            DisposeSelectedCrop();

            ClearPicture(cropPreview);
            ClearPicture(lineArtPreview);

            try
            {
                if (File.Exists(ManualCropPath))
                    File.Delete(ManualCropPath);
            }
            catch
            {
            }

            status.Text =
                "Manual crop cleared. Original JPG fallback use hogi.";
        }

        private void DisposeSelectedCrop()
        {
            if (selectedCropBitmap != null)
            {
                selectedCropBitmap.Dispose();
                selectedCropBitmap = null;
            }

            selectedCropSourcePath = null;
        }

        private string PrepareSearchImage()
        {
            bool validManualCrop =
                selectedCropBitmap != null &&
                !string.IsNullOrEmpty(
                    selectedCropSourcePath
                ) &&
                string.Equals(
                    selectedCropSourcePath,
                    imagePath.Text,
                    StringComparison.OrdinalIgnoreCase
                );

            if (!validManualCrop)
                return imagePath.Text;

            Directory.CreateDirectory(
                QueryDebugPath
            );

            SaveBitmapSafe(
                selectedCropBitmap,
                ManualCropPath
            );

            return ManualCropPath;
        }

        private void SearchImage()
        {
            if (searchInProgress)
                return;

            searchInProgress = true;

            if (searchButton != null)
                searchButton.Enabled = false;

            try
            {
                if (!File.Exists(imagePath.Text))
                {
                    MessageBox.Show(
                        "Select a valid image."
                    );

                    return;
                }

                string searchImagePath =
                    PrepareSearchImage();

                bool manualCropUsed =
                    string.Equals(
                        searchImagePath,
                        ManualCropPath,
                        StringComparison.OrdinalIgnoreCase
                    );

                status.Text =
                    manualCropUsed
                        ? "Stage 1/2: descriptor shortlist..."
                        : "Stage 1/2: original image shortlist...";

                grid.DataSource = null;
                ClearPicture(resultPreview);
                Application.DoEvents();

                ShowDiagnosticLineArt(
                    searchImagePath
                );

                var matcher = new ImageMatcher();

                byte[] queryDescriptor =
                    matcher.ExtractDescriptorBytes(
                        searchImagePath
                    );

                var fastCandidates =
                    new List<Tuple<DesignRecord, double>>();

                using (var db = new Database(DbPath))
                {
                    var designs = db.LoadDesigns();

                    foreach (var design in designs)
                    {
                        if (design.Descriptor == null ||
                            design.Descriptor.Length == 0)
                        {
                            continue;
                        }

                        double fastScore =
                            matcher.Compare(
                                queryDescriptor,
                                design.Descriptor
                            );

                        fastCandidates.Add(
                            Tuple.Create(
                                design,
                                fastScore
                            )
                        );
                    }
                }

                var shortlist =
                    fastCandidates
                        .OrderByDescending(
                            item => item.Item2
                        )
                        .ThenByDescending(
                            item => item.Item1.ShapeCount
                        )
                        .Take(40)
                        .ToList();

                status.Text =
                    "Stage 2/2: actual PNG verification 0/" +
                    shortlist.Count;

                Application.DoEvents();

                var results =
                    new List<MatchResult>();

                using (ImageMatcher.VerificationTemplate queryTemplate =
                    matcher.CreateVerificationTemplate(
                        searchImagePath
                    ))
                {
                    for (int index = 0;
                         index < shortlist.Count;
                         index++)
                    {
                        DesignRecord design =
                            shortlist[index].Item1;

                        double fastScore =
                            shortlist[index].Item2;

                        string candidatePath =
                            string.IsNullOrEmpty(design.PngPath)
                                ? design.ThumbnailPath
                                : design.PngPath;

                        double realImageScore =
                            matcher.VerifyImage(
                                queryTemplate,
                                candidatePath
                            );

                        double finalScore;

                        if (realImageScore > 0)
                        {
                            finalScore =
                                realImageScore * 0.78 +
                                fastScore * 0.22;

                            if (realImageScore < 30)
                                finalScore *= 0.62;
                            else if (realImageScore < 42)
                                finalScore *= 0.78;
                            else if (realImageScore >= 78 &&
                                     fastScore >= 58)
                                finalScore += 2.0;
                        }
                        else
                        {
                            finalScore =
                                fastScore * 0.72;
                        }

                        string mode =
                            design.ExportMode == null
                                ? ""
                                : design.ExportMode
                                    .ToUpperInvariant();

                        if (mode == "COREL-GROUP-HD")
                            finalScore += 0.75;

                        if (mode == "FULL-PAGE-HD")
                            finalScore -= 4.0;

                        if (mode == "OBJECT-HD" &&
                            design.ShapeCount <= 1)
                        {
                            finalScore -= 3.0;
                        }

                        finalScore = Math.Max(
                            0,
                            Math.Min(100, finalScore)
                        );

                        results.Add(
                            new MatchResult
                            {
                                MatchPercent =
                                    Math.Round(
                                        finalScore,
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

                        status.Text =
                            "Stage 2/2: actual PNG verification " +
                            (index + 1) +
                            "/" +
                            shortlist.Count;

                        if (index % 4 == 0)
                            Application.DoEvents();
                    }
                }

                var top =
                    results
                        .OrderByDescending(
                            item => item.MatchPercent
                        )
                        .ThenByDescending(
                            item => item.ShapeCount
                        )
                        .ThenBy(
                            item => item.CdrPath ?? "",
                            StringComparer.OrdinalIgnoreCase
                        )
                        .ThenBy(
                            item => item.PageNumber
                        )
                        .ThenBy(
                            item => item.DesignNumber
                        )
                        .Take(40)
                        .ToList();

                grid.DataSource = top;

                if (top.Count == 0)
                {
                    status.Text =
                        "NO RESULT | " +
                        (
                            manualCropUsed
                                ? "Manual crop used"
                                : "Original fallback used"
                        );

                    return;
                }

                status.Text =
                    (
                        manualCropUsed
                            ? "Manual crop + real PNG verify"
                            : "Original + real PNG verify"
                    ) +
                    " | Best match: " +
                    top[0].MatchPercent +
                    "% | " +
                    top[0].CdrPath +
                    " | Page " +
                    top[0].PageNumber +
                    " | Design " +
                    top[0].DesignNumber +
                    " | Mode " +
                    top[0].ExportMode;

                previewTabs.SelectedIndex = 3;
            }
            catch (Exception ex)
            {
                status.Text = "Search error";

                MessageBox.Show(
                    "Search error:\r\n" +
                    ex.Message
                );
            }
            finally
            {
                searchInProgress = false;

                if (searchButton != null)
                    searchButton.Enabled = true;
            }
        }

        private void ShowDiagnosticLineArt(
            string searchImagePath)
        {
            if (string.IsNullOrEmpty(
                    searchImagePath
                ) ||
                !File.Exists(searchImagePath))
            {
                ClearPicture(lineArtPreview);
                return;
            }

            ImagePreprocessResult processed = null;

            try
            {
                using (Bitmap source =
                    new Bitmap(searchImagePath))
                {
                    processed =
                        ImagePreprocessor.Process(
                            source
                        );
                }

                if (processed != null &&
                    processed.LineArt != null)
                {
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
            catch
            {
                ClearPicture(lineArtPreview);
            }
            finally
            {
                if (processed != null)
                    processed.Dispose();
            }
        }

        private async void StartScan(bool full)
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
            catch (OperationCanceledException)
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
                Process.Start(item.CdrPath);
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
                Process.Start(folder);
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

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(
                    directory
                );
            }

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }

            bitmap.Save(
                path,
                ImageFormat.Png
            );
        }

        private static void SetPictureFromFile(
            PictureBox pictureBox,
            string path)
        {
            ClearPicture(pictureBox);

            if (pictureBox == null ||
                string.IsNullOrEmpty(path) ||
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

        private static void SetPictureFromBitmap(
            PictureBox pictureBox,
            Bitmap bitmap)
        {
            ClearPicture(pictureBox);

            if (pictureBox == null ||
                bitmap == null)
            {
                return;
            }

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

            Image oldImage =
                pictureBox.Image;

            pictureBox.Image = null;
            pictureBox.ImageLocation = null;

            if (oldImage != null)
            {
                try
                {
                    oldImage.Dispose();
                }
                catch
                {
                }
            }
        }

        /*
         * Manual crop selection dialog.
         * Isi MainForm.cs ke andar rakha hai,
         * isliye nayi project file add nahi karni.
         */

        private sealed class CropSelectionForm : Form
        {
            private readonly Bitmap sourceBitmap;
            private readonly PictureBox pictureBox;
            private readonly Button useButton;
            private readonly Button cancelButton;
            private readonly Label instructionLabel;

            private Point dragStart;
            private Rectangle selectionDisplay;
            private bool dragging;
            private Bitmap selectedCrop;

            public CropSelectionForm(
                Bitmap source)
            {
                sourceBitmap =
                    new Bitmap(source);

                Text = "Select Jewellery Area";
                Width = 1000;
                Height = 760;
                StartPosition =
                    FormStartPosition.CenterParent;

                instructionLabel = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 35,
                    Text =
                        "Mouse se jewellery ke around rectangle banao. Phir Use Selection dabao.",
                    TextAlign =
                        ContentAlignment.MiddleCenter
                };

                Controls.Add(
                    instructionLabel
                );

                var bottom = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 50
                };

                Controls.Add(bottom);

                useButton = new Button
                {
                    Text = "Use Selection",
                    Left = 360,
                    Top = 10,
                    Width = 130
                };

                useButton.Click +=
                    OnUseSelection;

                bottom.Controls.Add(useButton);

                cancelButton = new Button
                {
                    Text = "Cancel",
                    Left = 510,
                    Top = 10,
                    Width = 100
                };

                cancelButton.Click +=
                    delegate
                    {
                        DialogResult =
                            DialogResult.Cancel;

                        Close();
                    };

                bottom.Controls.Add(cancelButton);

                pictureBox = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.DimGray,
                    SizeMode =
                        PictureBoxSizeMode.Zoom,
                    Image =
                        new Bitmap(sourceBitmap)
                };

                pictureBox.MouseDown +=
                    OnPictureMouseDown;

                pictureBox.MouseMove +=
                    OnPictureMouseMove;

                pictureBox.MouseUp +=
                    OnPictureMouseUp;

                pictureBox.Paint +=
                    OnPicturePaint;

                Controls.Add(pictureBox);

                AcceptButton = useButton;
                CancelButton = cancelButton;
            }

            protected override void Dispose(
                bool disposing)
            {
                if (disposing)
                {
                    if (pictureBox != null &&
                        pictureBox.Image != null)
                    {
                        pictureBox.Image.Dispose();
                        pictureBox.Image = null;
                    }

                    sourceBitmap.Dispose();

                    if (selectedCrop != null)
                    {
                        selectedCrop.Dispose();
                        selectedCrop = null;
                    }
                }

                base.Dispose(disposing);
            }

            public Bitmap TakeSelectedCrop()
            {
                if (selectedCrop == null)
                    return null;

                Bitmap result =
                    selectedCrop;

                selectedCrop = null;

                return result;
            }

            private void OnPictureMouseDown(
                object sender,
                MouseEventArgs e)
            {
                if (e.Button !=
                    MouseButtons.Left)
                {
                    return;
                }

                dragStart = e.Location;
                selectionDisplay =
                    Rectangle.Empty;

                dragging = true;

                pictureBox.Invalidate();
            }

            private void OnPictureMouseMove(
                object sender,
                MouseEventArgs e)
            {
                if (!dragging)
                    return;

                selectionDisplay =
                    NormalizeRectangle(
                        dragStart,
                        e.Location
                    );

                pictureBox.Invalidate();
            }

            private void OnPictureMouseUp(
                object sender,
                MouseEventArgs e)
            {
                if (!dragging)
                    return;

                dragging = false;

                selectionDisplay =
                    NormalizeRectangle(
                        dragStart,
                        e.Location
                    );

                pictureBox.Invalidate();
            }

            private void OnPicturePaint(
                object sender,
                PaintEventArgs e)
            {
                if (selectionDisplay.Width <= 0 ||
                    selectionDisplay.Height <= 0)
                {
                    return;
                }

                using (var pen =
                    new Pen(Color.Red, 3))
                {
                    e.Graphics.DrawRectangle(
                        pen,
                        selectionDisplay
                    );
                }
            }

            private void OnUseSelection(
                object sender,
                EventArgs e)
            {
                if (selectionDisplay.Width < 10 ||
                    selectionDisplay.Height < 10)
                {
                    MessageBox.Show(
                        "Jewellery ke around thoda bada rectangle banao."
                    );

                    return;
                }

                Rectangle imageDisplay =
                    GetDisplayedImageRectangle(
                        pictureBox,
                        sourceBitmap
                    );

                Rectangle clipped =
                    Rectangle.Intersect(
                        selectionDisplay,
                        imageDisplay
                    );

                if (clipped.Width < 5 ||
                    clipped.Height < 5)
                {
                    MessageBox.Show(
                        "Selection image ke andar banao."
                    );

                    return;
                }

                double scaleX =
                    sourceBitmap.Width /
                    (double)imageDisplay.Width;

                double scaleY =
                    sourceBitmap.Height /
                    (double)imageDisplay.Height;

                int sourceX =
                    (int)Math.Round(
                        (
                            clipped.Left -
                            imageDisplay.Left
                        ) *
                        scaleX
                    );

                int sourceY =
                    (int)Math.Round(
                        (
                            clipped.Top -
                            imageDisplay.Top
                        ) *
                        scaleY
                    );

                int sourceWidth =
                    (int)Math.Round(
                        clipped.Width *
                        scaleX
                    );

                int sourceHeight =
                    (int)Math.Round(
                        clipped.Height *
                        scaleY
                    );

                Rectangle sourceRectangle =
                    new Rectangle(
                        sourceX,
                        sourceY,
                        sourceWidth,
                        sourceHeight
                    );

                sourceRectangle.Intersect(
                    new Rectangle(
                        0,
                        0,
                        sourceBitmap.Width,
                        sourceBitmap.Height
                    )
                );

                if (sourceRectangle.Width < 5 ||
                    sourceRectangle.Height < 5)
                {
                    MessageBox.Show(
                        "Valid jewellery area select nahi hui."
                    );

                    return;
                }

                if (selectedCrop != null)
                    selectedCrop.Dispose();

                selectedCrop =
                    sourceBitmap.Clone(
                        sourceRectangle,
                        PixelFormat.Format24bppRgb
                    );

                DialogResult =
                    DialogResult.OK;

                Close();
            }

            private static Rectangle
                NormalizeRectangle(
                    Point first,
                    Point second)
            {
                int left =
                    Math.Min(
                        first.X,
                        second.X
                    );

                int top =
                    Math.Min(
                        first.Y,
                        second.Y
                    );

                int right =
                    Math.Max(
                        first.X,
                        second.X
                    );

                int bottom =
                    Math.Max(
                        first.Y,
                        second.Y
                    );

                return Rectangle.FromLTRB(
                    left,
                    top,
                    right,
                    bottom
                );
            }

            private static Rectangle
                GetDisplayedImageRectangle(
                    PictureBox box,
                    Image image)
            {
                if (box == null ||
                    image == null ||
                    box.ClientSize.Width <= 0 ||
                    box.ClientSize.Height <= 0)
                {
                    return Rectangle.Empty;
                }

                double imageRatio =
                    image.Width /
                    (double)image.Height;

                double boxRatio =
                    box.ClientSize.Width /
                    (double)box.ClientSize.Height;

                int displayWidth;
                int displayHeight;

                if (imageRatio > boxRatio)
                {
                    displayWidth =
                        box.ClientSize.Width;

                    displayHeight =
                        (int)Math.Round(
                            displayWidth /
                            imageRatio
                        );
                }
                else
                {
                    displayHeight =
                        box.ClientSize.Height;

                    displayWidth =
                        (int)Math.Round(
                            displayHeight *
                            imageRatio
                        );
                }

                int left =
                    (
                        box.ClientSize.Width -
                        displayWidth
                    ) / 2;

                int top =
                    (
                        box.ClientSize.Height -
                        displayHeight
                    ) / 2;

                return new Rectangle(
                    left,
                    top,
                    displayWidth,
                    displayHeight
                );
            }
        }
    }
}
