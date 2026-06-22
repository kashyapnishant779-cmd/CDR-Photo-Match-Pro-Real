using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CDRPhotoMatchPro.Core;

namespace CDRPhotoMatchPro.Data
{
    public sealed class Database : IDisposable
    {
        private readonly string _dbPath;
        private readonly Dictionary<long, string> _cdrPaths = new Dictionary<long, string>();

        public Database(string dbPath)
        {
            _dbPath = dbPath;

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            LoadCdrMap();
        }

        public bool NeedsIndex(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            if (!File.Exists(_dbPath))
                return true;

            long id = MakeId(path);
            string key = "CDR|" + id.ToString(CultureInfo.InvariantCulture) + "|";

            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] p = line.Split('|');

                if (p.Length >= 5 &&
                    p[2] == lastWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture) &&
                    p[3] == sizeBytes.ToString(CultureInfo.InvariantCulture) &&
                    p[4] == sha1)
                {
                    return false;
                }
            }

            return true;
        }

        public long UpsertCdr(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            long id = MakeId(path);
            _cdrPaths[id] = path;

            // Same CDR ka old file record aur old designs hatao,
            // warna incremental scan me duplicate results aayenge.
            RemoveLines("CDR|" + id + "|");
            RemoveLines("DES|" + id + "|");

            AppendLine(
                "CDR|" + id + "|" +
                lastWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                sizeBytes.ToString(CultureInfo.InvariantCulture) + "|" +
                sha1 + "|" +
                Encode(path)
            );

            return id;
        }

        public void InsertDesign(
            long cdrId,
            int pageNumber,
            int designNumber,
            string thumbnailPath,
            string pngPath,
            byte[] descriptor,
            int width,
            int height,
            string exportMode,
            int shapeCount)
        {
            string cdrPath = _cdrPaths.ContainsKey(cdrId) ? _cdrPaths[cdrId] : "";
            string fileName = Path.GetFileName(cdrPath);
            string folderPath = Path.GetDirectoryName(cdrPath);
            string b64 = descriptor == null ? "" : Convert.ToBase64String(descriptor);

            AppendLine(
                "DES|" + cdrId + "|" +
                pageNumber.ToString(CultureInfo.InvariantCulture) + "|" +
                designNumber.ToString(CultureInfo.InvariantCulture) + "|" +
                width.ToString(CultureInfo.InvariantCulture) + "|" +
                height.ToString(CultureInfo.InvariantCulture) + "|" +
                Encode(thumbnailPath) + "|" +
                Encode(pngPath) + "|" +
                b64 + "|" +
                Encode(cdrPath) + "|" +
                Encode(fileName) + "|" +
                Encode(folderPath) + "|" +
                Encode(exportMode) + "|" +
                shapeCount.ToString(CultureInfo.InvariantCulture)
            );
        }

        // Purane Indexer code ke liye compatibility overload.
        public void InsertDesign(long cdrId, int page, int obj, string thumb, byte[] desc, int w, int h)
        {
            InsertDesign(cdrId, page, obj, thumb, thumb, desc, w, h, "legacy", 0);
        }

        public List<DesignRecord> LoadDesigns()
        {
            var list = new List<DesignRecord>();

            if (!File.Exists(_dbPath))
                return list;

            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (!line.StartsWith("DES|", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] p = line.Split('|');

                try
                {
                    DesignRecord r = new DesignRecord();

                    r.CdrFileId = ToLong(p[1]);
                    r.PageNumber = ToInt(p[2]);
                    r.DesignNumber = ToInt(p[3]);
                    r.Width = ToInt(p[4]);
                    r.Height = ToInt(p[5]);

                    // New format:
                    // DES|cdrId|page|design|w|h|thumb|png|desc|cdrPath|fileName|folderPath|exportMode|shapeCount
                    if (p.Length >= 14)
                    {
                        r.ThumbnailPath = Decode(p[6]);
                        r.PngPath = Decode(p[7]);
                        r.Descriptor = string.IsNullOrEmpty(p[8]) ? null : Convert.FromBase64String(p[8]);
                        r.CdrPath = Decode(p[9]);
                        r.FileName = Decode(p[10]);
                        r.FolderPath = Decode(p[11]);
                        r.ExportMode = Decode(p[12]);
                        r.ShapeCount = ToInt(p[13]);
                    }
                    else
                    {
                        // Old format support:
                        // DES|cdrId|page|obj|w|h|thumb|desc|cdrPath|fileName|folderPath
                        r.ThumbnailPath = Decode(p[6]);
                        r.PngPath = r.ThumbnailPath;
                        r.Descriptor = string.IsNullOrEmpty(p[7]) ? null : Convert.FromBase64String(p[7]);
                        r.CdrPath = p.Length >= 9 ? Decode(p[8]) : "";
                        r.FileName = p.Length >= 10 ? Decode(p[9]) : Path.GetFileName(r.CdrPath);
                        r.FolderPath = p.Length >= 11 ? Decode(p[10]) : Path.GetDirectoryName(r.CdrPath);
                        r.ExportMode = "old";
                        r.ShapeCount = 0;
                    }

                    if (!string.IsNullOrEmpty(r.CdrPath))
                        list.Add(r);
                }
                catch
                {
                    // Corrupt line skip
                }
            }

            return list;
        }

        public void ClearAll()
        {
            try
            {
                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }
            catch { }

            _cdrPaths.Clear();
        }

        public void Dispose()
        {
        }

        private void LoadCdrMap()
        {
            if (!File.Exists(_dbPath))
                return;

            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (!line.StartsWith("CDR|", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] p = line.Split('|');

                if (p.Length >= 6)
                    _cdrPaths[ToLong(p[1])] = Decode(p[5]);
            }
        }

        private void AppendLine(string line)
        {
            File.AppendAllText(_dbPath, line + Environment.NewLine, Encoding.UTF8);
        }

        private void RemoveLines(string prefix)
        {
            if (!File.Exists(_dbPath))
                return;

            var lines = new List<string>();

            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    lines.Add(line);
            }

            File.WriteAllLines(_dbPath, lines.ToArray(), Encoding.UTF8);
        }

        private static long MakeId(string path)
        {
            unchecked
            {
                long hash = 1469598103934665603L;

                foreach (char c in path.ToLowerInvariant())
                    hash = (hash ^ c) * 1099511628211L;

                if (hash == long.MinValue)
                    return long.MaxValue;

                return Math.Abs(hash);
            }
        }

        private static string Encode(string s)
        {
            if (s == null)
                return "";

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }

        private static string Decode(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }

        private static int ToInt(string s)
        {
            int v;
            int.TryParse(s, out v);
            return v;
        }

        private static long ToLong(string s)
        {
            long v;
            long.TryParse(s, out v);
            return v;
        }
    }
}
