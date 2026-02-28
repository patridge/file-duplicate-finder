using System;
using System.Collections.Generic;
using System.IO;

namespace FileDeduplicator.Common
{
    /// <summary>
    /// Identifies image files by file extension and/or magic bytes (PNG, JPEG, GIF, BMP, WebP, HEIF).
    /// </summary>
    public class ImageFileIdentifier : IFileTypeIdentifier
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".heic", ".heif", ".svg"
        };

        public bool IsMatch(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (ImageExtensions.Contains(ext))
                return true;

            return HasImageMagicBytes(filePath);
        }

        private static bool HasImageMagicBytes(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                if (stream.Length < 4)
                    return false;

                Span<byte> header = stackalloc byte[12];
                int bytesRead = stream.Read(header);
                if (bytesRead < 4)
                    return false;

                // PNG: 0x89 0x50 0x4E 0x47
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                    return true;

                // JPEG: 0xFF 0xD8 0xFF
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                    return true;

                // GIF: "GIF8"
                if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8')
                    return true;

                // BMP: "BM"
                if (header[0] == 'B' && header[1] == 'M')
                    return true;

                // WebP: "RIFF" ... "WEBP"
                if (bytesRead >= 12
                    && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                    && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
