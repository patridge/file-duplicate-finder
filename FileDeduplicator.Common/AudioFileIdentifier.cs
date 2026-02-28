using System;
using System.Collections.Generic;
using System.IO;

namespace FileDeduplicator.Common
{
    /// <summary>
    /// Identifies audio files by file extension and/or magic bytes (e.g., ID3 header, MP3 sync word).
    /// </summary>
    public class AudioFileIdentifier : IFileTypeIdentifier
    {
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".ogg", ".wav", ".aac", ".wma", ".m4a", ".opus"
        };

        public bool IsMatch(string filePath)
        {
            // Check by extension first
            var ext = Path.GetExtension(filePath);
            if (AudioExtensions.Contains(ext))
                return true;

            // Check by magic bytes (ID3v2 header or MP3 sync word)
            return HasAudioMagicBytes(filePath);
        }

        private static bool HasAudioMagicBytes(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                if (stream.Length < 3)
                    return false;

                Span<byte> header = stackalloc byte[3];
                int bytesRead = stream.Read(header);
                if (bytesRead < 3)
                    return false;

                // ID3v2 tag: "ID3"
                if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                    return true;

                // MP3 sync word: 0xFF 0xFB / 0xFF 0xFA / 0xFF 0xF3 / 0xFF 0xF2 / 0xFF 0xE3 / 0xFF 0xE2
                if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
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
