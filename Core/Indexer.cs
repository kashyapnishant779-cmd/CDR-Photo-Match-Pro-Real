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
        private readonly string _dbPath; private readonly string _cacheRoot;
        public Indexer(string dbPath, string cacheRoot) { _dbPath = dbPath; _cacheRoot = cacheRoot; }
        public Task RunAsync(string root, bool fullRescan, IProgress<IndexProgress> progress, CancellationToken token)
        {
            return Task.Factory.StartNew(() =>
            {
                Directory.CreateDirectory(_cacheRoot);
                var scanner = new FileScanner(); var files = scanner.EnumerateCdrFiles(root).ToList();
                using (var db = new Database(_dbPath))
                {
                    if (fullRescan) db.ClearAll();
                    int i = 0;
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested(); i++;
                        progress.Report(new IndexProgress { TotalFiles = files.Count, CurrentFile = i, Message = file });
                        var info = new FileInfo(file); var sha = scanner.Sha1OfFile(file);
                        if (!db.NeedsIndex(file, info.LastWriteTimeUtc, info.Length, sha)) continue;
                        var cdrId = db.UpsertCdr(file, info.LastWriteTimeUtc, info.Length, sha);
                        using (var corel = new CorelDrawService())
                        {
                            var matcher = new ImageMatcher();
                            foreach (var design in corel.ExportDesigns(file, Path.Combine(_cacheRoot, sha)))
                            {
                                try
                                {
                                    var desc = matcher.ExtractDescriptorBytes(design.PngPath); var size = matcher.ReadSize(design.PngPath);
                                    db.InsertDesign(cdrId, design.PageNumber, design.ObjectNumber, design.PngPath, desc, size.Width, size.Height);
                                }
                                catch (Exception ex) { progress.Report(new IndexProgress { TotalFiles = files.Count, CurrentFile = i, Message = "Image index failed: " + ex.Message }); }
                            }
                        }
                    }
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
