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

        private static DateTime SafeGetTimestamp(Func<DateTime> getter)
        {
            try
            {
                return getter();
            }
            catch (IOException)
            {
                return DateTime.MinValue;
            }
        }

        public List<FileDetails> ScanDirectory(string startPath)
        {
            var fileDetailsList = new List<FileDetails>();
            var currentFilePaths = Directory.GetFiles(startPath, "*", SearchOption.AllDirectories);
            foreach (var filePath in currentFilePaths)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var hashBytes = FileHelpers.GetFileSha256(filePath);
                    fileDetailsList.Add(new FileDetails
                    {
                        DetailsRetrieved = DateTime.UtcNow,
                        FilePath = filePath,
                        Sha256Hash = hashBytes,
                        FileSize = fileInfo.Length,
                        LastModified = SafeGetTimestamp(() => fileInfo.LastWriteTimeUtc),
                        Created = SafeGetTimestamp(() => fileInfo.CreationTimeUtc),
                        LastAccessed = SafeGetTimestamp(() => fileInfo.LastAccessTimeUtc),
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip files that can't be accessed (e.g., on network shares)
                }
            }
            return fileDetailsList;
        }

        /// <summary>
        /// Scans multiple directories and returns groups of duplicate files.
        /// When comparers are provided, uses them for content-based equivalence (e.g., ignoring metadata).
        /// When no comparers are provided, groups by SHA-256 hash (exact match).
        /// </summary>
        public List<List<FileDetails>> ScanDirectoriesForDuplicateGroups(
            string[] startPaths, long minSizeBytes,
            IFileComparer[]? comparers = null,
            string[]? excludePaths = null,
            Action<string>? onStatus = null,
            Action<double, string>? onProgress = null,
            Action<string, string>? onFileSkipped = null)
        {
            var normalizedExcludes = (excludePaths ?? [])
                .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .ToArray();

            // Phase 1: Collect file info from all paths and filter by minimum size
            var candidates = new List<(string FilePath, FileInfo Info)>();
            int totalFound = 0;
            int skippedCount = 0;
            foreach (var startPath in startPaths)
            {
                onStatus?.Invoke($"Discovering files in {startPath}...");
                foreach (var filePath in EnumerateFilesExcluding(startPath, normalizedExcludes))
                {
                    totalFound++;
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length >= minSizeBytes)
                        {
                            candidates.Add((filePath, fileInfo));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skippedCount++;
                        onFileSkipped?.Invoke(filePath, ex.Message);
                        onStatus?.Invoke($"Skipping inaccessible file: {filePath}");
                    }

                    if (totalFound % 500 == 0)
                    {
                        var dir = Path.GetDirectoryName(filePath) ?? filePath;
                        onProgress?.Invoke(-1, $"Discovering files... {candidates.Count} matched, {totalFound} found ({dir})");
                    }
                }
            }

            if (skippedCount > 0)
            {
                onStatus?.Invoke($"Skipped {skippedCount} inaccessible file(s).");
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

                try
                {
                    var hashBytes = FileHelpers.GetFileSha256(filePath);
                    fileDetailsByPath[filePath] = new FileDetails
                    {
                        DetailsRetrieved = DateTime.UtcNow,
                        FilePath = filePath,
                        Sha256Hash = hashBytes,
                        FileSize = fileInfo.Length,
                        LastModified = SafeGetTimestamp(() => fileInfo.LastWriteTimeUtc),
                        Created = SafeGetTimestamp(() => fileInfo.CreationTimeUtc),
                        LastAccessed = SafeGetTimestamp(() => fileInfo.LastAccessTimeUtc),
                    };
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    onFileSkipped?.Invoke(filePath, ex.Message);
                    onStatus?.Invoke($"Skipping inaccessible file: {filePath}");
                }
            }

            // Phase 4: Group duplicates
            if (comparers is { Length: > 0 })
            {
                onStatus?.Invoke("Using content comparers for equivalence detection...");
                var allGroups = new List<List<FileDetails>>();

                foreach (var sizeGroup in sizeGroups)
                {
                    var filesInGroup = sizeGroup
                        .Where(c => fileDetailsByPath.ContainsKey(c.FilePath))
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
                        {
                            groups.Add(new List<FileDetails> { file });
                        }
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

        private static IEnumerable<string> EnumerateFilesExcluding(string rootPath, string[] normalizedExcludes)
        {
            var dirs = new Stack<string>();
            dirs.Push(rootPath);
            while (dirs.Count > 0)
            {
                var dir = dirs.Pop();
                var fullDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (normalizedExcludes.Any(ex => fullDir.StartsWith(ex, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(dir))
                    {
                        dirs.Push(subDir);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip inaccessible subdirectories
                }
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
