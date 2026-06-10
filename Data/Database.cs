using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using CDRPhotoMatchPro.Core;

namespace CDRPhotoMatchPro.Data
{
    public sealed class Database : IDisposable
    {
        private readonly SQLiteConnection _con;
        public Database(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            var first = !File.Exists(dbPath);
            _con = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;Journal Mode=WAL;Synchronous=NORMAL;");
            _con.Open();
            EnsureSchema();
        }
        private void EnsureSchema()
        {
            Execute(@"CREATE TABLE IF NOT EXISTS cdr_files(id INTEGER PRIMARY KEY AUTOINCREMENT,path TEXT UNIQUE NOT NULL,file_name TEXT NOT NULL,folder_path TEXT NOT NULL,last_write_utc TEXT NOT NULL,size_bytes INTEGER NOT NULL,sha1 TEXT NOT NULL,indexed_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS designs(id INTEGER PRIMARY KEY AUTOINCREMENT,cdr_file_id INTEGER NOT NULL,page_number INTEGER NOT NULL,object_number INTEGER NOT NULL,thumbnail_path TEXT NOT NULL,descriptor BLOB NOT NULL,width INTEGER NOT NULL,height INTEGER NOT NULL,FOREIGN KEY(cdr_file_id) REFERENCES cdr_files(id));
CREATE INDEX IF NOT EXISTS ix_design_file ON designs(cdr_file_id);CREATE INDEX IF NOT EXISTS ix_cdr_path ON cdr_files(path);");
        }
        private void Execute(string sql) { using (var c = new SQLiteCommand(sql, _con)) c.ExecuteNonQuery(); }
        public bool NeedsIndex(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            using (var cmd = new SQLiteCommand("SELECT last_write_utc,size_bytes,sha1 FROM cdr_files WHERE path=@p", _con))
            { cmd.Parameters.AddWithValue("@p", path); using (var r = cmd.ExecuteReader()) { if (!r.Read()) return true; return r.GetString(0) != lastWriteUtc.ToString("o") || r.GetInt64(1) != sizeBytes || r.GetString(2) != sha1; } }
        }
        public long UpsertCdr(string path, DateTime lastWriteUtc, long sizeBytes, string sha1)
        {
            using (var tx = _con.BeginTransaction())
            {
                using (var del = new SQLiteCommand("DELETE FROM designs WHERE cdr_file_id IN (SELECT id FROM cdr_files WHERE path=@p)", _con, tx)) { del.Parameters.AddWithValue("@p", path); del.ExecuteNonQuery(); }
                using (var cmd = new SQLiteCommand(@"INSERT OR REPLACE INTO cdr_files(id,path,file_name,folder_path,last_write_utc,size_bytes,sha1,indexed_utc)
VALUES((SELECT id FROM cdr_files WHERE path=@p),@p,@n,@f,@lw,@s,@h,@iu); SELECT id FROM cdr_files WHERE path=@p;", _con, tx))
                {
                    cmd.Parameters.AddWithValue("@p", path); cmd.Parameters.AddWithValue("@n", Path.GetFileName(path)); cmd.Parameters.AddWithValue("@f", Path.GetDirectoryName(path)); cmd.Parameters.AddWithValue("@lw", lastWriteUtc.ToString("o")); cmd.Parameters.AddWithValue("@s", sizeBytes); cmd.Parameters.AddWithValue("@h", sha1); cmd.Parameters.AddWithValue("@iu", DateTime.UtcNow.ToString("o"));
                    var id = Convert.ToInt64(cmd.ExecuteScalar()); tx.Commit(); return id;
                }
            }
        }
        public void InsertDesign(long cdrId, int page, int obj, string thumb, byte[] desc, int w, int h)
        {
            using (var cmd = new SQLiteCommand("INSERT INTO designs(cdr_file_id,page_number,object_number,thumbnail_path,descriptor,width,height) VALUES(@c,@p,@o,@t,@d,@w,@h)", _con))
            { cmd.Parameters.AddWithValue("@c", cdrId); cmd.Parameters.AddWithValue("@p", page); cmd.Parameters.AddWithValue("@o", obj); cmd.Parameters.AddWithValue("@t", thumb); cmd.Parameters.Add("@d", System.Data.DbType.Binary, desc.Length).Value = desc; cmd.Parameters.AddWithValue("@w", w); cmd.Parameters.AddWithValue("@h", h); cmd.ExecuteNonQuery(); }
        }
        public List<DesignRecord> LoadDesigns()
        {
            var list = new List<DesignRecord>();
            using (var cmd = new SQLiteCommand(@"SELECT d.id,d.cdr_file_id,f.path,f.file_name,f.folder_path,d.page_number,d.object_number,d.thumbnail_path,d.descriptor,d.width,d.height FROM designs d JOIN cdr_files f ON f.id=d.cdr_file_id", _con))
            using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(new DesignRecord { Id = r.GetInt64(0), CdrFileId = r.GetInt64(1), CdrPath = r.GetString(2), FileName = r.GetString(3), FolderPath = r.GetString(4), PageNumber = r.GetInt32(5), ObjectNumber = r.GetInt32(6), ThumbnailPath = r.GetString(7), Descriptor = (byte[])r[8], Width = r.GetInt32(9), Height = r.GetInt32(10) });
            return list;
        }
        public void ClearAll() { Execute("DELETE FROM designs; DELETE FROM cdr_files;"); }
        public void Dispose() { _con.Dispose(); }
    }
}
