using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace CDRPhotoMatchPro.Core
{
    public sealed class FileScanner
    {
        public IEnumerable<string> EnumerateCdrFiles(string root)
        {
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                string[] files = new string[0]; string[] dirs = new string[0];
                try { files = Directory.GetFiles(dir, "*.cdr"); } catch { }
                foreach (var f in files) yield return f;
                try { dirs = Directory.GetDirectories(dir); } catch { }
                foreach (var d in dirs) pending.Push(d);
            }
        }
        public string Sha1OfFile(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }
    }
}
