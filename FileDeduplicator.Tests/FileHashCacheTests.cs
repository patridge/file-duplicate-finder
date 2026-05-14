using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests;

[TestFixture]
public class FileHashCacheTests
{
    private string _tempDir = null!;
    private string _cacheFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileHashCacheTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cacheFilePath = Path.Combine(_tempDir, "test-cache.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreateTestFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public void GetOrComputeHash_ComputesAndCachesHash()
    {
        var filePath = CreateTestFile("test.txt", "hello world");
        using var cache = new FileHashCache(_cacheFilePath);

        var hash1 = cache.GetOrComputeHash(filePath);
        Assert.That(hash1, Is.Not.Null);
        Assert.That(hash1.Length, Is.EqualTo(32)); // SHA256 = 32 bytes

        var entry = cache.GetEntry(filePath);
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Sha256Hash, Is.EqualTo(hash1.ToHexString()));
    }

    [Test]
    public void GetOrComputeHash_ReturnsCachedValue_WhenNotStale()
    {
        var filePath = CreateTestFile("test.txt", "hello world");
        using var cache = new FileHashCache(_cacheFilePath);

        var hash1 = cache.GetOrComputeHash(filePath);
        var hash2 = cache.GetOrComputeHash(filePath);

        Assert.That(hash2, Is.EqualTo(hash1));
        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrComputeHash_RecomputesHash_WhenFileModified()
    {
        var filePath = CreateTestFile("test.txt", "original content");
        using var cache = new FileHashCache(_cacheFilePath);

        var hash1 = cache.GetOrComputeHash(filePath);

        // Modify the file and ensure the timestamp changes
        File.WriteAllText(filePath, "modified content");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(2));

        var hash2 = cache.GetOrComputeHash(filePath);

        Assert.That(hash2, Is.Not.EqualTo(hash1));
    }

    [Test]
    public void IsStale_ReturnsTrueForMissingFile()
    {
        var filePath = CreateTestFile("test.txt", "hello");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(filePath);

        File.Delete(filePath);

        Assert.That(cache.IsStale(filePath), Is.True);
    }

    [Test]
    public void IsStale_ReturnsTrueForUncachedFile()
    {
        using var cache = new FileHashCache(_cacheFilePath);
        Assert.That(cache.IsStale("/nonexistent/path.txt"), Is.True);
    }

    [Test]
    public void IsStale_ReturnsFalseForCurrentEntry()
    {
        var filePath = CreateTestFile("test.txt", "hello");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(filePath);

        Assert.That(cache.IsStale(filePath), Is.False);
    }

    [Test]
    public void Remove_RemovesEntry()
    {
        var filePath = CreateTestFile("test.txt", "hello");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(filePath);

        Assert.That(cache.Count, Is.EqualTo(1));

        cache.Remove(filePath);

        Assert.That(cache.Count, Is.EqualTo(0));
        Assert.That(cache.GetEntry(filePath), Is.Null);
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        var file1 = CreateTestFile("a.txt", "aaa");
        var file2 = CreateTestFile("b.txt", "bbb");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(file1);
        cache.GetOrComputeHash(file2);

        Assert.That(cache.Count, Is.EqualTo(2));

        cache.Clear();

        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void RemoveEntriesUnderPath_RemovesOnlyMatchingEntries()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);

        var file1 = CreateTestFile("root.txt", "root");
        var file2 = Path.Combine(subDir, "nested.txt");
        File.WriteAllText(file2, "nested");

        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(file1);
        cache.GetOrComputeHash(file2);

        Assert.That(cache.Count, Is.EqualTo(2));

        var removed = cache.RemoveEntriesUnderPath(subDir);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(cache.Count, Is.EqualTo(1));
        Assert.That(cache.GetEntry(file1), Is.Not.Null);
        Assert.That(cache.GetEntry(file2), Is.Null);
    }

    [Test]
    public void RemoveStaleEntries_RemovesOnlyStale()
    {
        var file1 = CreateTestFile("fresh.txt", "fresh");
        var file2 = CreateTestFile("stale.txt", "stale");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(file1);
        cache.GetOrComputeHash(file2);

        // Make file2 stale by deleting it
        File.Delete(file2);

        var removed = cache.RemoveStaleEntries();

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(cache.Count, Is.EqualTo(1));
        Assert.That(cache.GetEntry(file1), Is.Not.Null);
    }

    [Test]
    public void SaveAndLoad_PersistsEntries()
    {
        var filePath = CreateTestFile("test.txt", "persist me");
        byte[] originalHash;

        using (var cache = new FileHashCache(_cacheFilePath))
        {
            originalHash = cache.GetOrComputeHash(filePath);
            cache.Save();
        }

        // Load from the same file
        using (var cache2 = new FileHashCache(_cacheFilePath))
        {
            Assert.That(cache2.Count, Is.EqualTo(1));
            var entry = cache2.GetEntry(filePath);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Sha256Hash, Is.EqualTo(originalHash.ToHexString()));
        }
    }

    [Test]
    public void RefreshStaleEntries_UpdatesModifiedFiles()
    {
        var filePath = CreateTestFile("test.txt", "original");
        using var cache = new FileHashCache(_cacheFilePath);
        var originalHash = cache.GetOrComputeHash(filePath);

        // Modify the file
        File.WriteAllText(filePath, "updated content");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(2));

        Assert.That(cache.IsStale(filePath), Is.True);

        var refreshed = cache.RefreshStaleEntries();

        Assert.That(refreshed, Is.EqualTo(1));
        Assert.That(cache.IsStale(filePath), Is.False);

        var entry = cache.GetEntry(filePath);
        Assert.That(entry!.Sha256Hash, Is.Not.EqualTo(originalHash.ToHexString()));
    }

    [Test]
    public void RefreshStaleEntries_RemovesDeletedFiles()
    {
        var filePath = CreateTestFile("test.txt", "will be deleted");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(filePath);

        File.Delete(filePath);

        var refreshed = cache.RefreshStaleEntries();

        Assert.That(refreshed, Is.EqualTo(1));
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_HandlesCorruptCacheFile()
    {
        File.WriteAllText(_cacheFilePath, "not valid json {{{");

        using var cache = new FileHashCache(_cacheFilePath);
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_HandlesMissingCacheFile()
    {
        using var cache = new FileHashCache(_cacheFilePath);
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void ConcurrentAccess_IsThreadSafe()
    {
        // Create several test files
        var files = new string[20];
        for (int i = 0; i < files.Length; i++)
        {
            files[i] = CreateTestFile($"file_{i}.txt", $"content_{i}_{Guid.NewGuid()}");
        }

        using var cache = new FileHashCache(_cacheFilePath);

        // Access cache concurrently from multiple threads
        Parallel.ForEach(files, filePath =>
        {
            var hash = cache.GetOrComputeHash(filePath);
            Assert.That(hash, Is.Not.Null);
            Assert.That(hash.Length, Is.EqualTo(32));

            var entry = cache.GetEntry(filePath);
            Assert.That(entry, Is.Not.Null);
        });

        Assert.That(cache.Count, Is.EqualTo(files.Length));
    }

    [Test]
    public void GetAllEntries_ReturnsAllCachedEntries()
    {
        var file1 = CreateTestFile("a.txt", "aaa");
        var file2 = CreateTestFile("b.txt", "bbb");
        using var cache = new FileHashCache(_cacheFilePath);
        cache.GetOrComputeHash(file1);
        cache.GetOrComputeHash(file2);

        var entries = cache.GetAllEntries();

        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries.Select(e => e.FilePath), Does.Contain(Path.GetFullPath(file1)));
        Assert.That(entries.Select(e => e.FilePath), Does.Contain(Path.GetFullPath(file2)));
    }

    [Test]
    public void FileScanner_UsesCache_WhenConfigured()
    {
        var file1 = CreateTestFile("a.txt", "same content");
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var file2 = Path.Combine(subDir, "b.txt");
        File.WriteAllText(file2, "same content");

        using var cache = new FileHashCache(_cacheFilePath);
        var scanner = new FileScanner { HashCache = cache };

        var results = scanner.ScanDirectory(_tempDir);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(cache.Count, Is.EqualTo(2));

        // Second scan should use cached values
        var results2 = scanner.ScanDirectory(_tempDir);
        Assert.That(results2, Has.Count.EqualTo(2));
    }
}
