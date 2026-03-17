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

        /// <summary>
        /// Scans a directory for duplicate files at or above a minimum size.
        /// Optimized to only compute hashes for files that share a size with at least one other file.
        /// </summary>
        public List<FileDetails> ScanDirectoryForLargeDuplicates(string startPath, long minSizeBytes, Action<string>? onStatus = null)
        {
            var currentFilePaths = Directory.GetFiles(startPath, "*", SearchOption.AllDirectories);

            // Phase 1: Collect file info and filter by minimum size
            var candidates = new List<(string FilePath, FileInfo Info)>();
            foreach (var filePath in currentFilePaths)
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length >= minSizeBytes)
                {
                    candidates.Add((filePath, fileInfo));
                }
            }

            onStatus?.Invoke($"Found {candidates.Count} file(s) at or above {FormatFileSize(minSizeBytes)}.");

            // Phase 2: Group by file size — only files sharing a size can be duplicates
            var sizeGroups = candidates
                .GroupBy(c => c.Info.Length)
                .Where(g => g.Count() > 1)
                .ToList();

            var filesToHash = sizeGroups.SelectMany(g => g).ToList();
            onStatus?.Invoke($"Computing hashes for {filesToHash.Count} file(s) with matching sizes...");

            // Phase 3: Hash only the files that have size-matches
            var hashedFiles = new List<FileDetails>();
            foreach (var (filePath, fileInfo) in filesToHash)
            {
                var hashBytes = FileHelpers.GetFileSha256(filePath);
                hashedFiles.Add(new FileDetails
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

            // Phase 4: Keep only files that share a hash with at least one other file
            var duplicates = hashedFiles
                .GroupBy(f => f.Sha256Hash.ToHexString())
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .ToList();

            return duplicates;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            int suffixIndex = 0;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            if (Comparer == null)
                throw new InvalidOperationException("No comparer configured.");
            return Comparer.AreFilesEquivalent(filePath1, filePath2);
        }
    }
}
