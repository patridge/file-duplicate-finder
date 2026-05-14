using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileDeduplicator.Common;

[JsonSerializable(typeof(List<FileHashCacheEntry>))]
internal partial class FileHashCacheJsonContext : JsonSerializerContext
{
}

public class FileHashCache : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = FileHashCacheJsonContext.Default,
    };

    private readonly ConcurrentDictionary<string, FileHashCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _fileLock = new();
    private readonly string _cacheFilePath;
    private bool _dirty;
    private bool _disposed;

    public FileHashCache(string? cacheFilePath = null)
    {
        _cacheFilePath = cacheFilePath ?? GetDefaultCacheFilePath();
        Load();
    }

    public static string GetDefaultCacheFilePath()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileDeduplicator");
        return Path.Combine(appDataDir, "hash-cache.json");
    }

    public int Count => _entries.Count;

    public IReadOnlyCollection<FileHashCacheEntry> GetAllEntries()
    {
        return _entries.Values.ToList().AsReadOnly();
    }

    public FileHashCacheEntry? GetEntry(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        _entries.TryGetValue(fullPath, out var entry);
        return entry;
    }

    public bool IsStale(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!_entries.TryGetValue(fullPath, out var entry))
        {
            return true;
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                return true;
            }

            return fileInfo.LastWriteTimeUtc != entry.LastModified
                || fileInfo.Length != entry.FileSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public byte[] GetOrComputeHash(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (_entries.TryGetValue(fullPath, out var cached) && !IsStale(fullPath))
        {
            return HexToBytes(cached.Sha256Hash);
        }

        var hash = FileHelpers.GetFileSha256(fullPath);
        var fileInfo = new FileInfo(fullPath);

        var entry = new FileHashCacheEntry
        {
            FilePath = fullPath,
            Sha256Hash = hash.ToHexString(),
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Created = fileInfo.CreationTimeUtc,
            CachedAt = DateTime.UtcNow,
        };

        _entries[fullPath] = entry;
        _dirty = true;

        return hash;
    }

    public void Remove(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (_entries.TryRemove(fullPath, out _))
        {
            _dirty = true;
        }
    }

    public int RemoveStaleEntries()
    {
        int removed = 0;
        foreach (var entry in _entries.Values.ToList())
        {
            if (IsStale(entry.FilePath))
            {
                if (_entries.TryRemove(entry.FilePath, out _))
                {
                    removed++;
                }
            }
        }

        if (removed > 0)
        {
            _dirty = true;
        }

        return removed;
    }

    public int RemoveEntriesUnderPath(string directoryPath)
    {
        var fullDir = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int removed = 0;

        foreach (var key in _entries.Keys.ToList())
        {
            if (key.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
            {
                if (_entries.TryRemove(key, out _))
                {
                    removed++;
                }
            }
        }

        if (removed > 0)
        {
            _dirty = true;
        }

        return removed;
    }

    public void Clear()
    {
        if (!_entries.IsEmpty)
        {
            _entries.Clear();
            _dirty = true;
        }
    }

    public int RefreshStaleEntries(Action<string>? onRefreshing = null)
    {
        int refreshed = 0;
        foreach (var entry in _entries.Values.ToList())
        {
            if (IsStale(entry.FilePath))
            {
                try
                {
                    if (!File.Exists(entry.FilePath))
                    {
                        _entries.TryRemove(entry.FilePath, out _);
                        refreshed++;
                        continue;
                    }

                    onRefreshing?.Invoke(entry.FilePath);
                    var hash = FileHelpers.GetFileSha256(entry.FilePath);
                    var fileInfo = new FileInfo(entry.FilePath);
                    _entries[entry.FilePath] = new FileHashCacheEntry
                    {
                        FilePath = entry.FilePath,
                        Sha256Hash = hash.ToHexString(),
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        Created = fileInfo.CreationTimeUtc,
                        CachedAt = DateTime.UtcNow,
                    };
                    refreshed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _entries.TryRemove(entry.FilePath, out _);
                    refreshed++;
                }
            }
        }

        if (refreshed > 0)
        {
            _dirty = true;
        }

        return refreshed;
    }

    public void Save()
    {
        if (!_dirty)
        {
            return;
        }

        _fileLock.EnterWriteLock();
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var entries = _entries.Values.ToList();
            var json = JsonSerializer.Serialize(entries, FileHashCacheJsonContext.Default.ListFileHashCacheEntry);
            File.WriteAllText(_cacheFilePath, json);
            _dirty = false;
        }
        finally
        {
            _fileLock.ExitWriteLock();
        }
    }

    private void Load()
    {
        _fileLock.EnterReadLock();
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_cacheFilePath);
            var entries = JsonSerializer.Deserialize(json, FileHashCacheJsonContext.Default.ListFileHashCacheEntry);
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    _entries[entry.FilePath] = entry;
                }
            }
        }
        catch (JsonException ex)
        {
            // Corrupted cache file — start fresh
            System.Diagnostics.Debug.WriteLine(
                $"FileHashCache: Failed to load cache from '{_cacheFilePath}'. " +
                $"Starting fresh. Error: {ex.Message} (Path: {ex.Path}, LineNumber: {ex.LineNumber}, BytePositionInLine: {ex.BytePositionInLine})");
        }
        finally
        {
            _fileLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Save();
            _fileLock.Dispose();
            _disposed = true;
        }
    }

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}
