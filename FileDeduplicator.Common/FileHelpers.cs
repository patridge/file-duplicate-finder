using System.IO;
using System.Security.Cryptography;

namespace FileDeduplicator.Common;

public class FileHelpers
{
    public static byte[] GetFileSha256(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using SHA256 sha256 = SHA256.Create();
        return sha256.ComputeHash(stream);
    }
    public static byte[] GetFileSha256(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or empty.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found.", filePath);

        using var stream = File.OpenRead(filePath);
        return GetFileSha256(stream);
    }

    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int suffixIndex = 0;
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        return $"{size:0.00} {suffixes[suffixIndex]}";
    }
}
