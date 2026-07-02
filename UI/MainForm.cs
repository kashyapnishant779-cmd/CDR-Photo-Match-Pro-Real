using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        private readonly string AppRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CDRPhotoMatchPro");

        private string DbPath { get { return Path.Combine(AppRoot, "cdr_index.sqlite"); } }
        private string CachePath { get { return Path.Combine(AppRoot, "thumb_cache"); } }

        private TabControl tabs;
        private TextBox imagePath, scanRoot, thresholdBox;
        private DataGridView grid;
        private PictureBox preview;
        private Label status;
        private CancellationTokenSource cts;

        private Button openCdrBtn, openFolderBtn, copyPathBtn;

        public MainForm()
        {
            Text = "CDR Photo Match Pro";
            Width = 1250;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;

            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(CachePath);

            using (var db = new Database(DbPath)) { }

            BuildUi();

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        private void BuildUi()
        {
            tabs = new TabControl { Dock = DockStyle.Fill };
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
                BorderStyle = BorderStyle.Fixed3D
            };

            Controls.Add(status);
        }

        private void BuildSearchTab()
        {
            var page = new TabPage("Search");
            tabs.TabPages.Add(page);

            var top = new Panel { Dock = DockStyle.Top, Height = 46 };
            page.Controls.Add(top);

            imagePath = new TextBox { Left = 10, Top = 12, Width = 620 };
            top.Controls.Add(imagePath);

            var browse = new Button { Text = "Browse Image", Left = 640, Top = 10, Width = 110 };
            browse.Click += delegate { PickImage(); };
            top.Controls.Add(browse);

            var search = new Button { Text = "Search", Left = 760, Top = 10, Width = 90 };
            search.Click += delegate { SearchImage(); };
            top.Controls.Add(search);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42 };
            page.Controls.Add(bottom);

            openCdrBtn = new Button { Text = "Open CDR", Left = 10, Top = 8, Width = 110 };
            openCdrBtn.Click += delegate { OpenSelectedCdr(); };
            bottom.Controls.Add(openCdrBtn);

            openFolderBtn = new Button { Text = "Open Folder", Left = 130, Top = 8, Width = 120 };
            openFolderBtn.Click += delegate { OpenSelectedFolder(); };
            bottom.Controls.Add(openFolderBtn);

            copyPathBtn = new Button { Text = "Copy Full Path", Left = 260, Top = 8, Width = 130 };
            copyPathBtn.Click += delegate { CopySelectedPath(); };
            bottom.Controls.Add(copyPathBtn);

            preview = new PictureBox
            {
                Dock = DockStyle.Right,
                Width = 330,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            page.Controls.Add(preview);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Match %", DataPropertyName = "MatchPercent", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CDR File", DataPropertyName = "CdrFileName", Width = 140 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Full CDR Path", DataPropertyName = "CdrPath", Width = 430 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Page", DataPropertyName = "PageNumber", Width = 55 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Design No", DataPropertyName = "DesignNumber", Width = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mode", DataPropertyName = "ExportMode", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shapes", DataPropertyName = "ShapeCount", Width = 60 });

            grid.DoubleClick += OnGridDoubleClick;
            grid.SelectionChanged += OnGridSelectionChanged;

            page.Controls.Add(grid);
        }

        private void BuildScanTab()
        {
            var page = new TabPage("Scan");
            tabs.TabPages.Add(page);

            scanRoot = new TextBox { Left = 20, Top = 25, Width = 500, Text = "D:\\" };
            page.Controls.Add(scanRoot);

            var start = new Button { Text = "Start Incremental Scan", Left = 540, Top = 23, Width = 160 };
            start.Click += delegate { StartScan(false); };
            page.Controls.Add(start);

            var rescan = new Button { Text = "Full Rescan", Left = 710, Top = 23, Width = 110 };
            rescan.Click += delegate { StartScan(true); };
            page.Controls.Add(rescan);

            var cancel = new Button { Text = "Cancel", Left = 830, Top = 23, Width = 90 };
            cancel.Click += delegate { if (cts != null) cts.Cancel(); };
            page.Controls.Add(cancel);

            page.Controls.Add(new Label
            {
                Left = 20,
                Top = 75,
                Width = 950,
                Height = 140,
                Text = "This scans CDR files recursively, exports design thumbnails, stores full CDR path + page number + design number."
            });
        }

        private void BuildIndexTab()
        {
            var page = new TabPage("Index");
            tabs.TabPages.Add(page);

            var openDb = new Button { Text = "Open Database Folder", Left = 20, Top = 25, Width = 180 };
            openDb.Click += delegate { Process.Start(AppRoot); };
            page.Controls.Add(openDb);

            var openCache = new Button { Text = "Open Thumbnail Cache", Left = 220, Top = 25, Width = 180 };
            openCache.Click += delegate { Process.Start(CachePath); };
            page.Controls.Add(openCache);

            var count = new Button { Text = "Show Indexed Count", Left = 420, Top = 25, Width = 150 };
            count.Click += delegate
            {
                using (var db = new Database(DbPath))
                    MessageBox.Show("Indexed designs: " + db.LoadDesigns().Count);
            };
            page.Controls.Add(count);
        }

        private void BuildSettingsTab()
        {
            var page = new TabPage("Settings");
            tabs.TabPages.Add(page);

            page.Controls.Add(new Label { Left = 20, Top = 28, Width = 180, Text = "Minimum match %" });

            thresholdBox = new TextBox { Left = 210, Top = 24, Width = 80, Text = "45" };
            page.Controls.Add(thresholdBox);

            page.Controls.Add(new Label
            {
                Left = 20,
                Top = 65,
                Width = 850,
                Text = "45 recommended. Lower value = more possible matches. Higher value = stricter exact match."
            });
        }

        private void PickImage()
        {
            using (var ofd = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    imagePath.Text = ofd.FileName;
                    preview.ImageLocation = ofd.FileName;
                }
            }
        }
        private void SearchImage()
{
    if (!File.Exists(imagePath.Text))
    {
        MessageBox.Show("Select a valid image.");
        return;
    }

    status.Text = "Searching...";
    Application.DoEvents();

    double threshold;
    if (!double.TryParse(thresholdBox.Text, out threshold))
        threshold = 20;

    var matcher = new ImageMatcher();
    var query = matcher.ExtractDescriptorBytes(imagePath.Text);
    var results = new List<MatchResult>();

    using (var db = new Database(DbPath))
    {
        foreach (var d in db.LoadDesigns())
        {
            var score = matcher.Compare(query, d.Descriptor);

            if (score > 1)
            {
                results.Add(new MatchResult
                {
                    MatchPercent = Math.Round(score, 2),
                    CdrFileName = d.FileName,
                    FullFolderPath = d.FolderPath,
                    CdrPath = d.CdrPath,
                    PageNumber = d.PageNumber,
                    DesignNumber = d.DesignNumber,
                    ObjectNumber = d.DesignNumber,
                    ThumbnailPath = d.ThumbnailPath,
                    PngPath = d.PngPath,
                    ExportMode = d.ExportMode,
                    ShapeCount = d.ShapeCount
                });
            }
        }
    }

    var top = results
        .OrderByDescending(x => x.MatchPercent)
        .Take(50)
        .ToList();

    grid.DataSource = top;

    if (top.Count == 0)
    {
        status.Text = "NO RESULT - index empty or descriptor mismatch";
        preview.ImageLocation = imagePath.Text;
    }
    else if (top[0].MatchPercent < threshold)
    {
        status.Text = "POSSIBLE MATCH ONLY: " + top[0].MatchPercent + "% | " + top[0].CdrPath + " | Page " + top[0].PageNumber + " | Design " + top[0].DesignNumber;
    }
    else
    {
        status.Text = "Best match: " + top[0].MatchPercent + "% | " + top[0].CdrPath + " | Page " + top[0].PageNumber + " | Design " + top[0].DesignNumber;
    }
}
        
        private async void StartScan(bool full)
        {
            if (!Directory.Exists(scanRoot.Text))
            {
                MessageBox.Show("Scan folder not found.");
                return;
            }

            cts = new CancellationTokenSource();
            var indexer = new Indexer(DbPath, CachePath);

            var progress = new Progress<IndexProgress>(p =>
            {
                status.Text = p.CurrentFile + "/" + p.TotalFiles + " " + p.Message;
            });

            try
            {
                status.Text = full ? "Full rescan started..." : "Incremental scan started...";
                await indexer.RunAsync(scanRoot.Text, full, progress, cts.Token);
                status.Text = "Indexing complete";
            }
            catch (OperationCanceledException)
            {
                status.Text = "Indexing cancelled";
            }
            catch (Exception ex)
            {
                status.Text = "Indexing error";
                MessageBox.Show(ex.ToString());
            }
        }

        private MatchResult SelectedItem()
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as MatchResult;
        }

        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            var item = SelectedItem();

            if (item != null)
            {
                string img = item.PngPath;

                if (string.IsNullOrEmpty(img))
                    img = item.ThumbnailPath;

                if (File.Exists(img))
                    preview.ImageLocation = img;
            }
        }

        private void OpenSelectedCdr()
        {
            var item = SelectedItem();
            if (item != null && File.Exists(item.CdrPath))
                Process.Start(item.CdrPath);
        }

        private void OpenSelectedFolder()
        {
            var item = SelectedItem();
            if (item == null)
                return;

            string folder = item.FullFolderPath;

            if (string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(item.CdrPath))
                folder = Path.GetDirectoryName(item.CdrPath);

            if (Directory.Exists(folder))
                Process.Start(folder);
        }

        private void CopySelectedPath()
        {
            var item = SelectedItem();
            if (item != null && !string.IsNullOrEmpty(item.CdrPath))
            {
                Clipboard.SetText(item.CdrPath);
                status.Text = "Copied: " + item.CdrPath;
            }
        }

        private void OnGridDoubleClick(object sender, EventArgs e)
        {
            OpenSelectedCdr();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (files.Length > 0)
            {
                imagePath.Text = files[0];
                preview.ImageLocation = files[0];
                tabs.SelectedIndex = 0;
            }
        }
    }
}
