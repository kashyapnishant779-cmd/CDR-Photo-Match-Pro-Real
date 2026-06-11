using System;
using System.Collections.Generic;
using System.IO;
using CDRPhotoMatchPro.Core;

namespace CDRPhotoMatchPro.Data
{
    public sealed class Database : IDisposable
    {
        private readonly string _dbPath;

        public Database(string dbPath)
        {
            _dbPath = dbPath;

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        public bool NeedsIndex(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            return true;
        }

        public long UpsertCdr(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            return Math.Abs(path.GetHashCode());
        }

        public void InsertDesign(long cdrId, int page, int obj, string thumb, byte[] desc, int w, int h)
        {
        }

        public List<DesignRecord> LoadDesigns()
        {
            return new List<DesignRecord>();
        }

        public void ClearAll()
        {
            try
            {
                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
        }
    }
}
