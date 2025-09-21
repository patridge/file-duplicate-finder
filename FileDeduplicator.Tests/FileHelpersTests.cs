using FileDeduplicator.Common;

namespace FileDeduplicator.Tests;

public class FileHelpersTests
{
    [Test]
    public void FileHelpers_GetFileSha256_Stream_ReturnsExpectedHash_ForKnownText()
    {
        var text = "abc123";
        var expectedHash = "6ca13d52ca70c883e0f0bb101e425a89e8624de51db2d2392593af6a84118090";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    
        var resultBytes = FileHelpers.GetFileSha256(stream);
        var resultHash = BitConverter.ToString(resultBytes).Replace("-", "").ToLowerInvariant();
    
        Assert.That(resultHash, Is.EqualTo(expectedHash));
    }

    [Test]
    public void FileHelpers_GetFileSha256_FileFromTestFilesOriginal_ReturnsExpectedHash()
    {
        var expectedHash = "6ca13d52ca70c883e0f0bb101e425a89e8624de51db2d2392593af6a84118090";
        var filePath = "test_files/original/file_abc123.txt";

        var resultBytes = FileHelpers.GetFileSha256(filePath);
        var resultHash = BitConverter.ToString(resultBytes).Replace("-", "").ToLowerInvariant();

        Assert.That(resultHash, Is.EqualTo(expectedHash));
    }
}