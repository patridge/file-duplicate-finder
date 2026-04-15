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
        public List<FileDetails> ScanDirectoryForDuplicates(string startPath, long minSizeBytes, Action<string>? onStatus = null)
        {
            return ScanDirectoriesForDuplicates([startPath], minSizeBytes, onStatus);
        }

        /// <summary>
        /// Scans multiple directories for duplicate files, optionally filtering by minimum size.
        /// Candidates from all paths are combined before size-grouping and hashing,
        /// so duplicates across different directories are detected.
        /// </summary>
        public List<FileDetails> ScanDirectoriesForDuplicates(string[] startPaths, long minSizeBytes, Action<string>? onStatus = null, Action<double, string>? onProgress = null)
        {
            // Phase 1: Collect file info from all paths and filter by minimum size
            var candidates = new List<(string FilePath, FileInfo Info)>();
            foreach (var startPath in startPaths)
            {
                onStatus?.Invoke($"Discovering files in {startPath}...");
                foreach (var filePath in Directory.EnumerateFiles(startPath, "*", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length >= minSizeBytes)
                    {
                        candidates.Add((filePath, fileInfo));
                    }

                    if (candidates.Count % 500 == 0)
                    {
                        var dir = Path.GetDirectoryName(filePath) ?? filePath;
                        onProgress?.Invoke(-1, $"Discovering files... {candidates.Count} found so far ({dir})");
                    }
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
            int processedCount = 0;
            foreach (var (filePath, fileInfo) in filesToHash)
            {
                processedCount++;
                double pct = (double)processedCount / filesToHash.Count * 100;
                onProgress?.Invoke(pct, filePath);

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

        /// <summary>
        /// Scans multiple directories and returns groups of duplicate files.
        /// When comparers are provided, uses them for content-based equivalence (e.g., ignoring metadata).
        /// When no comparers are provided, groups by SHA-256 hash (exact match).
        /// </summary>
        public List<List<FileDetails>> ScanDirectoriesForDuplicateGroups(
            string[] startPaths, long minSizeBytes,
            IFileComparer[]? comparers = null,
            Action<string>? onStatus = null,
            Action<double, string>? onProgress = null)
        {
            // Phase 1: Collect file info from all paths and filter by minimum size
            var candidates = new List<(string FilePath, FileInfo Info)>();
            foreach (var startPath in startPaths)
            {
                onStatus?.Invoke($"Discovering files in {startPath}...");
                foreach (var filePath in Directory.EnumerateFiles(startPath, "*", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length >= minSizeBytes)
                    {
                        candidates.Add((filePath, fileInfo));
                    }

                    if (candidates.Count % 500 == 0)
                    {
                        var dir = Path.GetDirectoryName(filePath) ?? filePath;
                        onProgress?.Invoke(-1, $"Discovering files... {candidates.Count} found so far ({dir})");
                    }
                }
            }

            onStatus?.Invoke($"Found {candidates.Count} file(s) at or above {FormatFileSize(minSizeBytes)}.");

            // Phase 2: Group by file size — only files sharing a size can be duplicates
            var sizeGroups = candidates
                .GroupBy(c => c.Info.Length)
                .Where(g => g.Count() > 1)
                .ToList();

            var filesToProcess = sizeGroups.SelectMany(g => g).ToList();
            onStatus?.Invoke($"Processing {filesToProcess.Count} file(s) with matching sizes...");

            // Phase 3: Hash all size-matched files and build FileDetails
            var fileDetailsByPath = new Dictionary<string, FileDetails>();
            int processedCount = 0;
            foreach (var (filePath, fileInfo) in filesToProcess)
            {
                processedCount++;
                double pct = (double)processedCount / filesToProcess.Count * 100;
                onProgress?.Invoke(pct, filePath);

                var hashBytes = FileHelpers.GetFileSha256(filePath);
                fileDetailsByPath[filePath] = new FileDetails
                {
                    DetailsRetrieved = DateTime.UtcNow,
                    FilePath = filePath,
                    Sha256Hash = hashBytes,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Created = fileInfo.CreationTimeUtc,
                    LastAccessed = fileInfo.LastAccessTimeUtc,
                };
            }

            // Phase 4: Group duplicates
            if (comparers is { Length: > 0 })
            {
                onStatus?.Invoke("Using content comparers for equivalence detection...");
                var allGroups = new List<List<FileDetails>>();

                foreach (var sizeGroup in sizeGroups)
                {
                    var filesInGroup = sizeGroup
                        .Select(c => fileDetailsByPath[c.FilePath])
                        .ToList();

                    // Build equivalence groups using pairwise comparison
                    var groups = new List<List<FileDetails>>();
                    foreach (var file in filesInGroup)
                    {
                        bool added = false;
                        foreach (var group in groups)
                        {
                            var comparer = comparers.FirstOrDefault(c =>
                                c.CanCompare(file.FilePath) && c.CanCompare(group[0].FilePath));
                            if (comparer != null && comparer.AreFilesEquivalent(file.FilePath, group[0].FilePath))
                            {
                                group.Add(file);
                                added = true;
                                break;
                            }
                        }
                        if (!added)
                            groups.Add(new List<FileDetails> { file });
                    }

                    allGroups.AddRange(groups.Where(g => g.Count > 1));
                }

                return allGroups;
            }
            else
            {
                // Hash-based grouping (exact match)
                return fileDetailsByPath.Values
                    .GroupBy(f => f.Sha256Hash.ToHexString())
                    .Where(g => g.Count() > 1)
                    .Select(g => g.ToList())
                    .ToList();
            }
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
