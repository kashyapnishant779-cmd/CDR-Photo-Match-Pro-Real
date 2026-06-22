using System;

namespace CDRPhotoMatchPro.Core
{
    public sealed class CdrFileRecord
    {
        public long Id { get; set; }
        public string Path { get; set; }
        public string FileName { get; set; }
        public string FolderPath { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public long SizeBytes { get; set; }
        public string Sha1 { get; set; }
    }

    public sealed class DesignRecord
    {
        public long Id { get; set; }
        public long CdrFileId { get; set; }
        public string CdrPath { get; set; }
        public string FileName { get; set; }
        public string FolderPath { get; set; }
        public int PageNumber { get; set; }
        public int ObjectNumber { get; set; }
        public string ThumbnailPath { get; set; }
        public string PngPath { get; set; }
        public byte[] Descriptor { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class MatchResult
    {
        public double MatchPercent { get; set; }
        public string CdrFileName { get; set; }
        public string FullFolderPath { get; set; }
        public string CdrPath { get; set; }
        public int PageNumber { get; set; }
        public int ObjectNumber { get; set; }
        public string ThumbnailPath { get; set; }
    }

    public sealed class IndexProgress
    {
        public int TotalFiles { get; set; }
        public int CurrentFile { get; set; }
        public string Message { get; set; }
    }
}
