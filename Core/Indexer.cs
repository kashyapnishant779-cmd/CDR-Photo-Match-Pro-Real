using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public Task RunAsync(
            string root,
            bool fullRescan,
            IProgress<IndexProgress> progress,
            CancellationToken token)
        {
            return Task.Factory.StartNew(() =>
            {
                Directory.CreateDirectory(_cacheRoot);

                var scanner = new FileScanner();
                var matcher = new ImageMatcher();

                var files = scanner.EnumerateCdrFiles(root).ToList();

                int insertedCount = 0;
                int failedCount = 0;
                int skippedCount = 0;

                using (var db = new Database(_dbPath))
                {
                    if (fullRescan)
                        db.ClearAll();

                    // CorelDRAW COM ko sirf ek baar open karo.
                    // Har file pe new CorelDrawService mat banao.
                    using (var corel = new CorelDrawService())
                    {
                        for (int i = 0; i < files.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();

                            string file = files[i];

                            progress.Report(new IndexProgress
                            {
                                TotalFiles = files.Count,
                                CurrentFile = i + 1,
                                Message = "Scanning: " + file
                            });

                            try
                            {
                                var info = new FileInfo(file);
                                string sha = scanner.Sha1OfFile(file);

                                if (!fullRescan &&
                                    !db.NeedsIndex(file, info.LastWriteTimeUtc, info.Length, sha))
                                {
                                    skippedCount++;
                                    continue;
                                }

                                long cdrId = db.UpsertCdr(
                                    file,
                                    info.LastWriteTimeUtc,
                                    info.Length,
                                    sha
                                );

                                string fileCache = Path.Combine(_cacheRoot, sha);
                                Directory.CreateDirectory(fileCache);

                                var exported = corel.ExportDesigns(file, fileCache).ToList();

                                progress.Report(new IndexProgress
                                {
                                    TotalFiles = files.Count,
                                    CurrentFile = i + 1,
                                    Message = "Exported " + exported.Count + " designs from: " + Path.GetFileName(file)
                                });

                                foreach (var design in exported)
                                {
                                    token.ThrowIfCancellationRequested();

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

                                        byte[] desc = matcher.ExtractDescriptorBytes(imgPath);
                                        var size = matcher.ReadSize(imgPath);

                                        if (desc == null || desc.Length == 0)
                                        {
                                            failedCount++;
                                            continue;
                                        }

                                        db.InsertDesign(
                                            cdrId,
                                            design.PageNumber,
                                            design.DesignNumber,
                                            design.ThumbnailPath,
                                            imgPath,
                                            desc,
                                            size.Width,
                                            size.Height,
                                            design.ExportMode,
                                            design.ShapeCount
                                        );

                                        insertedCount++;
                                    }
                                    catch
                                    {
                                        failedCount++;
                                    }
                                }
                            }
                            catch
                            {
                                failedCount++;
                            }

                            progress.Report(new IndexProgress
                            {
                                TotalFiles = files.Count,
                                CurrentFile = i + 1,
                                Message =
                                    "Done " + (i + 1) + "/" + files.Count +
                                    " | Inserted: " + insertedCount +
                                    " | Skipped: " + skippedCount +
                                    " | Failed: " + failedCount
                            });
                        }
                    }
                }

                progress.Report(new IndexProgress
                {
                    TotalFiles = files.Count,
                    CurrentFile = files.Count,
                    Message =
                        "Indexing complete. Inserted: " + insertedCount +
                        ", Skipped: " + skippedCount +
                        ", Failed: " + failedCount
                });

            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
