using System;
using System.Collections.Generic;
using System.IO;
using FileDeduplicator.Common;

namespace FileDeduplicator.Common
{
    public class FileScanner
    {
        public IFileComparer? Comparer { get; set; }

        public FileScanner() { }
        public FileScanner(IFileComparer comparer)
        {
            Comparer = comparer;
        }

        public List<FileDetails> ScanDirectory(string startPath)
        {
            var fileDetailsList = new List<FileDetails>();
            var currentFilePaths = Directory.GetFiles(startPath, "*", SearchOption.AllDirectories);
            foreach (var filePath in currentFilePaths)
            {
                var fileInfo = new FileInfo(filePath);
                var hashBytes = FileHelpers.GetFileSha256(filePath);
                fileDetailsList.Add(new FileDetails
                {
                    DetailsRetrieved = DateTime.UtcNow,
                    FilePath = filePath,
                    Sha256Hash = hashBytes,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Created = fileInfo.CreationTimeUtc,
                    LastAccessed = fileInfo.LastAccessTimeUtc,
                });
            }
            return fileDetailsList;
        }

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            if (Comparer == null)
                throw new InvalidOperationException("No comparer configured.");
            return Comparer.AreFilesEquivalent(filePath1, filePath2);
        }
    }
}
