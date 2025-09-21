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
}
