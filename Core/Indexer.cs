using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CDRPhotoMatchPro.Data;
using CDRPhotoMatchPro.Imaging;

namespace CDRPhotoMatchPro.Core
{
    public sealed class Indexer
    {
        private readonly string _dbPath;
        private readonly string _cacheRoot;

        public Indexer(string dbPath, string cacheRoot)
        {
            _dbPath = dbPath;
            _cacheRoot = cacheRoot;
        }

        public Task RunAsync(string root, bool fullRescan, IProgress<IndexProgress> progress, CancellationToken token)
        {
            return Task.Factory.StartNew(() =>
            {
                Directory.CreateDirectory(_cacheRoot);

                var scanner = new FileScanner();
                var files = scanner.EnumerateCdrFiles(root).ToList();

                MessageBox.Show("CDR files found: " + files.Count);

                int insertedCount = 0;
                int failedCount = 0;

                using (var db = new Database(_dbPath))
                {
                    if (fullRescan)
                        db.ClearAll();

                    int i = 0;

                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        i++;

                        progress.Report(new IndexProgress
                        {
                            TotalFiles = files.Count,
                            CurrentFile = i,
                            Message = file
                        });

                        var info = new FileInfo(file);
                        var sha = scanner.Sha1OfFile(file);

                        if (!db.NeedsIndex(file, info.LastWriteTimeUtc, info.Length, sha))
                            continue;

                        var cdrId = db.UpsertCdr(file, info.LastWriteTimeUtc, info.Length, sha);

                        using (var corel = new CorelDrawService())
                        {
                            var matcher = new ImageMatcher();
                            var exported = corel.ExportDesigns(file, Path.Combine(_cacheRoot, sha)).ToList();

                            MessageBox.Show("Exported images count: " + exported.Count);

                            foreach (var design in exported)
                            {
                                try
                                {
                                    string imgPath = design.PngPath;

                                    if (string.IsNullOrEmpty(imgPath))
                                        imgPath = design.ThumbnailPath;

                                    if (string.IsNullOrEmpty(imgPath) || !File.Exists(imgPath))
                                    {
                                        failedCount++;
                                        continue;
                                    }

                                    var desc = matcher.ExtractDescriptorBytes(imgPath);
                                    var size = matcher.ReadSize(imgPath);

                                    if (desc == null || desc.Length == 0)
                                    {
                                        failedCount++;
                                        continue;
                                    }

                                    db.InsertDesign(
                                        cdrId,
                                        design.PageNumber,
                                        design.ObjectNumber,
                                        imgPath,
                                        desc,
                                        size.Width,
                                        size.Height
                                    );

                                    insertedCount++;
                                }
                                catch
                                {
                                    failedCount++;
                                }
                            }
                        }
                    }
                }

                MessageBox.Show(
                    "Indexing complete\n" +
                    "Inserted designs: " + insertedCount + "\n" +
                    "Failed designs: " + failedCount
                );

            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
