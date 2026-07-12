using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CDRPhotoMatchPro.Core
{
    public sealed class FileScanner
    {
        private readonly string _logPath;

        public FileScanner()
        {
            string appFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CDRPhotoMatchPro");

            Directory.CreateDirectory(appFolder);
            _logPath = Path.Combine(appFolder, "scan_debug.txt");
        }

        public IEnumerable<string> EnumerateCdrFiles(string root)
        {
            int foundCount = 0;
            int folderCount = 0;
            int failedFolderCount = 0;

            WriteLog("");
            WriteLog("==============================================");
            WriteLog("SCAN START");
            WriteLog("Root: " + root);
            WriteLog("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (string.IsNullOrWhiteSpace(root))
            {
                WriteLog("ERROR: Scan root empty hai.");
                yield break;
            }

            if (!Directory.Exists(root))
            {
                WriteLog("ERROR: Scan root exist nahi karta: " + root);
                yield break;
            }

            var pending = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            pending.Push(root);

            try
            {
                while (pending.Count > 0)
                {
                    string dir = pending.Pop();

                    if (string.IsNullOrWhiteSpace(dir))
                        continue;

                    if (!visited.Add(dir))
                        continue;

                    folderCount++;

                    string[] files;

                    try
                    {
                        files = Directory.GetFiles(
                            dir,
                            "*",
                            SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception ex)
                    {
                        failedFolderCount++;
                        WriteLog(
                            "FILES ERROR | Folder: " + dir +
                            " | " + ex.GetType().Name +
                            " | " + ex.Message);

                        continue;
                    }

                    foreach (string file in files)
                    {
                        string extension;

                        try
                        {
                            extension = Path.GetExtension(file);
                        }
                        catch (Exception ex)
                        {
                            WriteLog(
                                "EXTENSION ERROR | File: " + file +
                                " | " + ex.GetType().Name +
                                " | " + ex.Message);

                            continue;
                        }

                        if (!string.Equals(
                            extension,
                            ".cdr",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foundCount++;
                        WriteLog("FOUND CDR | " + file);

                        yield return file;
                    }

                    string[] directories;

                    try
                    {
                        directories = Directory.GetDirectories(
                            dir,
                            "*",
                            SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception ex)
                    {
                        failedFolderCount++;
                        WriteLog(
                            "FOLDERS ERROR | Folder: " + dir +
                            " | " + ex.GetType().Name +
                            " | " + ex.Message);

                        continue;
                    }

                    foreach (string childDirectory in directories)
                    {
                        if (!visited.Contains(childDirectory))
                            pending.Push(childDirectory);
                    }
                }
            }
            finally
            {
                WriteLog("----------------------------------------------");
                WriteLog("SCAN ENUMERATION END");
                WriteLog("Folders checked: " + folderCount);
                WriteLog("CDR files found: " + foundCount);
                WriteLog("Folder errors: " + failedFolderCount);
                WriteLog("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                WriteLog("==============================================");
            }
        }

        public string Sha1OfFile(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                return BitConverter
                    .ToString(sha.ComputeHash(fs))
                    .Replace("-", "");
            }
        }

        private void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(
                    _logPath,
                    message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // Logging fail hone se actual scan band nahi hona chahiye.
            }
        }
    }
}
