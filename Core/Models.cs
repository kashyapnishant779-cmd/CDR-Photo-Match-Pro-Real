using System;

namespace CDRPhotoMatchPro.Core
{
    public sealed class CdrFileRecord
    {
        public long Id;
        public string Path;
        public string FileName;
        public string FolderPath;
        public DateTime LastWriteUtc;
        public long SizeBytes;
        public string Sha1;
    }

    public sealed class DesignRecord
    {
        public long Id;
        public long CdrFileId;
        public string CdrPath;
        public string FileName;
        public string FolderPath;
        public int PageNumber;
        public int ObjectNumber;

        public string ThumbnailPath;
        public string PngPath;

        public byte[] Descriptor;
        public int Width;
        public int Height;
    }

    public sealed class MatchResult
    {
        public double MatchPercent;
        public string CdrFileName;
        public string FullFolderPath;
        public string CdrPath;
        public int PageNumber;
        public int ObjectNumber;
        public string ThumbnailPath;
    }

    public sealed class IndexProgress
    {
        public int TotalFiles;
        public int CurrentFile;
        public string Message;
    }
}
