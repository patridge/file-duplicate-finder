using System;
using System.Text;

namespace FileDeduplicator.Common;

public static class ByteArrayExtensions
{
    public static string ToHexString(this byte[] hash)
    {
        if (hash == null)
            throw new ArgumentNullException(nameof(hash));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }
}

