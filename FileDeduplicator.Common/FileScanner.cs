using System;
using System.Collections.Generic;
using System.IO;
using FileDeduplicator.Common;

namespace FileDeduplicator.Common
{
    public class FileScanner
    {
        public IFileComparer? Comparer { get; set; }
        public FileHashCache? HashCache { get; set; }

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
                    var hashBytes = HashCache != null
                        ? HashCache.GetOrComputeHash(filePath)
                        : FileHelpers.GetFileSha256(filePath);
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
            string[]? excludeExtensions = null,
            string[]? excludeFileNames = null,
            Action<string>? onStatus = null,
            Action<double, string>? onProgress = null,
            Action<string, string>? onFileSkipped = null,
            Action<List<FileDetails>>? onDuplicateGroupFound = null)
        {
            var normalizedExcludes = (excludePaths ?? [])
                .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .ToArray();
            var extExcludes = (excludeExtensions ?? [])
                .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
                .ToArray();
            var fileNameExcludes = excludeFileNames ?? [];

            // Phase 1: Collect file info from all paths and filter by minimum size
            var candidates = new List<(string FilePath, FileInfo Info)>();
            int totalFound = 0;
            int skippedCount = 0;
            foreach (var startPath in startPaths)
            {
                onStatus?.Invoke($"Discovering files in {startPath}...");
                foreach (var filePath in EnumerateFilesExcluding(startPath, normalizedExcludes, extExcludes, fileNameExcludes))
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
                        var prefix = $"Discovering files... {candidates.Count} matched, {totalFound} found";
                        onProgress?.Invoke(-1, $"{prefix} ({dir})");
                    }
                }
            }

            if (skippedCount > 0)
            {
                onStatus?.Invoke($"Skipped {skippedCount} inaccessible file(s).");
            }
            onStatus?.Invoke($"Found {candidates.Count} file(s) at or above {FileHelpers.FormatFileSize(minSizeBytes)}.");

            // When caching is enabled, hash ALL candidates so future scans benefit from cached data
            if (HashCache != null)
            {
                onStatus?.Invoke($"Hashing {candidates.Count} file(s)...");
                int cachedCount = 0;
                foreach (var (filePath, fileInfo) in candidates)
                {
                    cachedCount++;
                    double pct = (double)cachedCount / candidates.Count * 100;
                    onProgress?.Invoke(pct, $"Hashing [{cachedCount}/{candidates.Count}] {filePath}");

                    try
                    {
                        HashCache.GetOrComputeHash(filePath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Skip files that can't be hashed
                    }

                    if (cachedCount % 100 == 0)
                    {
                        HashCache.Save();
                    }
                }
                HashCache.Save();
            }

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
                onProgress?.Invoke(pct, $"Hashing [{processedCount}/{filesToProcess.Count}] {filePath}");

                try
                {
                    var hashBytes = HashCache != null
                        ? HashCache.GetOrComputeHash(filePath)
                        : FileHelpers.GetFileSha256(filePath);
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

                    // Periodically save cache to avoid losing work if interrupted
                    if (HashCache != null && processedCount % 100 == 0)
                    {
                        HashCache.Save();
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    onFileSkipped?.Invoke(filePath, ex.Message);
                    onStatus?.Invoke($"Skipping inaccessible file: {filePath}");
                }
            }

            // Save cache after all hashing is complete
            HashCache?.Save();

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

                    foreach (var group in groups.Where(g => g.Count > 1))
                    {
                        allGroups.Add(group);
                        onDuplicateGroupFound?.Invoke(group);
                    }
                }

                return allGroups;
            }
            else
            {
                // Hash-based grouping (exact match)
                var allGroups = fileDetailsByPath.Values
                    .GroupBy(f => f.Sha256Hash.ToHexString())
                    .Where(g => g.Count() > 1)
                    .Select(g => g.ToList())
                    .ToList();

                foreach (var group in allGroups)
                {
                    onDuplicateGroupFound?.Invoke(group);
                }

                return allGroups;
            }
        }

        private static IEnumerable<string> EnumerateFilesExcluding(string rootPath, string[] normalizedExcludes, string[] extExcludes, string[] fileNameExcludes)
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
                    var fileName = Path.GetFileName(file);
                    if (extExcludes.Length > 0)
                    {
                        var ext = Path.GetExtension(file);
                        if (extExcludes.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }
                    if (fileNameExcludes.Length > 0)
                    {
                        if (fileNameExcludes.Any(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }
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

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            if (Comparer == null)
                throw new InvalidOperationException("No comparer configured.");
            return Comparer.AreFilesEquivalent(filePath1, filePath2);
        }
    }
}
