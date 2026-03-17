using System;
using System.IO;
using System.Linq;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests;

[TestFixture]
public class FileScannerTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateFile(string relativePath, int sizeInBytes)
    {
        var content = new byte[sizeInBytes];
        Random.Shared.NextBytes(content);
        return CreateFileWithContent(relativePath, content);
    }

    private string CreateFileWithContent(string relativePath, byte[] content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    #region ScanDirectory tests

    [Test]
    public void ScanDirectory_ReturnsAllFiles()
    {
        CreateFile("a.txt", 100);
        CreateFile("b.txt", 200);
        CreateFile("sub/c.txt", 50);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectory(_tempDir);

        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public void ScanDirectory_PopulatesFileDetails()
    {
        var path = CreateFile("test.bin", 512);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectory(_tempDir);

        Assert.That(results, Has.Count.EqualTo(1));
        var detail = results[0];
        Assert.That(detail.FilePath, Is.EqualTo(path));
        Assert.That(detail.FileSize, Is.EqualTo(512));
        Assert.That(detail.Sha256Hash, Is.Not.Null.And.Length.EqualTo(32));
    }

    [Test]
    public void ScanDirectory_IdenticalFiles_HaveSameHash()
    {
        var content = new byte[256];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("file1.bin", content);
        CreateFileWithContent("file2.bin", content);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectory(_tempDir);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Sha256Hash.ToHexString(), Is.EqualTo(results[1].Sha256Hash.ToHexString()));
    }

    [Test]
    public void ScanDirectory_EmptyDirectory_ReturnsEmpty()
    {
        var scanner = new FileScanner();
        var results = scanner.ScanDirectory(_tempDir);

        Assert.That(results, Is.Empty);
    }

    #endregion

    #region ScanDirectoryForDuplicates tests

    [Test]
    public void ScanDirectoryForDuplicates_FiltersOutSmallFiles()
    {
        CreateFile("small.txt", 50);
        CreateFile("small2.txt", 50);
        CreateFile("large.bin", 1000);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        // large.bin is the only file >= 100 bytes but has no size-match partner, so no results
        Assert.That(results, Is.Empty);
    }

    [Test]
    public void ScanDirectoryForDuplicates_ReturnsDuplicatesAboveMinSize()
    {
        var content = new byte[500];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("dup1.bin", content);
        CreateFileWithContent("dup2.bin", content);
        CreateFile("small.txt", 10);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Sha256Hash.ToHexString(), Is.EqualTo(results[1].Sha256Hash.ToHexString()));
    }

    [Test]
    public void ScanDirectoryForDuplicates_ExcludesUniqueFiles()
    {
        // Two different files with different sizes — no duplicates
        CreateFile("unique1.bin", 500);
        CreateFile("unique2.bin", 600);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void ScanDirectoryForDuplicates_SameSizeDifferentContent_NotReturned()
    {
        // Two files with same size but different content are not duplicates
        var content1 = new byte[500];
        var content2 = new byte[500];
        Random.Shared.NextBytes(content1);
        Random.Shared.NextBytes(content2);
        CreateFileWithContent("file1.bin", content1);
        CreateFileWithContent("file2.bin", content2);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void ScanDirectoryForDuplicates_MinSizeZero_IncludesAllSizeMatchedFiles()
    {
        var content = new byte[1];
        content[0] = 0x42;
        CreateFileWithContent("tiny1.bin", content);
        CreateFileWithContent("tiny2.bin", content);
        CreateFileWithContent("empty1.bin", Array.Empty<byte>());
        CreateFileWithContent("empty2.bin", Array.Empty<byte>());

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 0);

        Assert.That(results, Has.Count.EqualTo(4));
    }

    [Test]
    public void ScanDirectoryForDuplicates_EmptyDirectory_ReturnsEmpty()
    {
        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void ScanDirectoryForDuplicates_ScansSubdirectories()
    {
        var content = new byte[200];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("top/dup1.bin", content);
        CreateFileWithContent("top/sub/dup2.bin", content);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void ScanDirectoryForDuplicates_OnStatusCallback_IsInvoked()
    {
        var content = new byte[200];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("a.bin", content);
        CreateFileWithContent("b.bin", content);

        var messages = new System.Collections.Generic.List<string>();
        var scanner = new FileScanner();
        scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100, onStatus: msg => messages.Add(msg));

        Assert.That(messages, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(messages[0], Does.Contain("file(s) at or above"));
        Assert.That(messages[1], Does.Contain("Computing hashes"));
    }

    [Test]
    public void ScanDirectoryForDuplicates_ThreeDuplicates_ReturnsAll()
    {
        var content = new byte[300];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("dup1.bin", content);
        CreateFileWithContent("dup2.bin", content);
        CreateFileWithContent("dup3.bin", content);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Has.Count.EqualTo(3));
        var hashes = results.Select(r => r.Sha256Hash.ToHexString()).Distinct().ToList();
        Assert.That(hashes, Has.Count.EqualTo(1));
    }

    [Test]
    public void ScanDirectoryForDuplicates_MultipleDuplicateGroups()
    {
        var contentA = new byte[400];
        var contentB = new byte[400];
        Random.Shared.NextBytes(contentA);
        Random.Shared.NextBytes(contentB);
        // Ensure different content
        contentB[0] = (byte)(contentA[0] ^ 0xFF);

        CreateFileWithContent("groupA_1.bin", contentA);
        CreateFileWithContent("groupA_2.bin", contentA);
        CreateFileWithContent("groupB_1.bin", contentB);
        CreateFileWithContent("groupB_2.bin", contentB);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Has.Count.EqualTo(4));
        var groups = results.GroupBy(r => r.Sha256Hash.ToHexString()).ToList();
        Assert.That(groups, Has.Count.EqualTo(2));
    }

    [Test]
    public void ScanDirectoryForDuplicates_MinSizeAtExactBoundary_IncludesFile()
    {
        var content = new byte[100];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("exact1.bin", content);
        CreateFileWithContent("exact2.bin", content);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 100);

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void ScanDirectoryForDuplicates_MinSizeJustAboveBoundary_ExcludesFile()
    {
        var content = new byte[100];
        Random.Shared.NextBytes(content);
        CreateFileWithContent("borderline1.bin", content);
        CreateFileWithContent("borderline2.bin", content);

        var scanner = new FileScanner();
        var results = scanner.ScanDirectoryForDuplicates(_tempDir, minSizeBytes: 101);

        Assert.That(results, Is.Empty);
    }

    #endregion
}
