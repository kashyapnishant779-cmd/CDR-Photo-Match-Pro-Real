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
            if (!File.Exists(_dbPath)) return true;

            string key = "CDR|" + MakeId(path).ToString(CultureInfo.InvariantCulture) + "|";
            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    string[] p = line.Split('|');
                    if (p.Length >= 5 && p[2] == lastWriteUtc.Ticks.ToString() && p[3] == sizeBytes.ToString() && p[4] == sha1)
                        return false;
                }
            }
            return true;
        }

        public long UpsertCdr(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            long id = MakeId(path);
            _cdrPaths[id] = path;

            RemoveLines("CDR|" + id + "|");
            AppendLine("CDR|" + id + "|" + lastWriteUtc.Ticks + "|" + sizeBytes + "|" + sha1 + "|" + Encode(path));
            return id;
        }

        public void InsertDesign(long cdrId, int page, int obj, string thumb, byte[] desc, int w, int h)
        {
            string path = _cdrPaths.ContainsKey(cdrId) ? _cdrPaths[cdrId] : "";
            string b64 = desc == null ? "" : Convert.ToBase64String(desc);

            AppendLine("DES|" + cdrId + "|" + page + "|" + obj + "|" + w + "|" + h + "|" + Encode(thumb) + "|" + b64 + "|" + Encode(path));
        }

        public List<DesignRecord> LoadDesigns()
        {
            var list = new List<DesignRecord>();
            if (!File.Exists(_dbPath)) return list;

            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (!line.StartsWith("DES|", StringComparison.OrdinalIgnoreCase)) continue;

                string[] p = line.Split('|');
                if (p.Length < 9) continue;

                try
                {
                    DesignRecord r = new DesignRecord();

                    Set(r, "CdrId", ToLong(p[1]));
                    Set(r, "Page", ToInt(p[2]));
                    Set(r, "ObjectIndex", ToInt(p[3]));
                    Set(r, "Obj", ToInt(p[3]));
                    Set(r, "Width", ToInt(p[4]));
                    Set(r, "Height", ToInt(p[5]));
                    Set(r, "ThumbnailPath", Decode(p[6]));
                    Set(r, "Thumb", Decode(p[6]));
                    Set(r, "Descriptor", string.IsNullOrEmpty(p[7]) ? null : Convert.FromBase64String(p[7]));
                    Set(r, "CdrPath", Decode(p[8]));
                    Set(r, "Path", Decode(p[8]));

                    list.Add(r);
                }
                catch { }
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

        public void Dispose() { }

        private void LoadCdrMap()
        {
            if (!File.Exists(_dbPath)) return;

            foreach (string line in File.ReadAllLines(_dbPath))
            {
                if (!line.StartsWith("CDR|", StringComparison.OrdinalIgnoreCase)) continue;
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
            if (!File.Exists(_dbPath)) return;

            var lines = new List<string>();
            foreach (string line in File.ReadAllLines(_dbPath))
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    lines.Add(line);

            File.WriteAllLines(_dbPath, lines.ToArray(), Encoding.UTF8);
        }

        private static long MakeId(string path)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (char c in path.ToLowerInvariant())
                    hash = (hash ^ c) * 1099511628211L;
                return Math.Abs(hash);
            }
        }

        private static string Encode(string s)
        {
            if (s == null) return "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }

        private static string Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
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

        private static void Set(object obj, string name, object value)
        {
            var t = obj.GetType();

            var prop = t.GetProperty(name);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value, null);
                return;
            }

            var field = t.GetField(name);
            if (field != null)
                field.SetValue(obj, value);
        }
    }
}
