using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FileDeduplicator.Common
{
    public class ImageFileComparer : IFileComparer
    {
        // PNG chunk types considered critical (non-metadata) for image content comparison.
        private static readonly HashSet<string> PngCriticalChunkTypes = new(StringComparer.Ordinal)
        {
            "IHDR", "PLTE", "IDAT", "IEND",
        };

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public IFileTypeIdentifier? Identifier { get; set; } = new ImageFileIdentifier();

        /// <summary>
        /// When true, ignores EXIF/metadata differences and compares only pixel data.
        /// </summary>
        public bool IgnoreMetadata { get; set; } = true;

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            if (!IgnoreMetadata)
            {
                return BinaryEqual(filePath1, filePath2);
            }

            // Try PNG-aware comparison first.
            if (IsPng(filePath1) && IsPng(filePath2))
            {
                return PngCriticalChunksEqual(filePath1, filePath2);
            }

            // Fallback: byte-for-byte comparison for non-PNG image formats.
            return BinaryEqual(filePath1, filePath2);
        }

        private static bool BinaryEqual(string path1, string path2)
        {
            var f1 = new FileInfo(path1);
            var f2 = new FileInfo(path2);
            if (f1.Length != f2.Length)
                return false;

            using var s1 = f1.OpenRead();
            using var s2 = f2.OpenRead();
            var buf1 = new byte[8192];
            var buf2 = new byte[8192];
            int bytesRead;
            while ((bytesRead = s1.Read(buf1, 0, buf1.Length)) > 0)
            {
                int read2 = 0;
                while (read2 < bytesRead)
                    read2 += s2.Read(buf2, read2, bytesRead - read2);

                if (!buf1.AsSpan(0, bytesRead).SequenceEqual(buf2.AsSpan(0, bytesRead)))
                    return false;
            }
            return true;
        }

        private static bool IsPng(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < PngSignature.Length)
                return false;
            var header = new byte[PngSignature.Length];
            stream.ReadExactly(header, 0, header.Length);
            return header.AsSpan().SequenceEqual(PngSignature);
        }

        /// <summary>
        /// Compares two PNG files by extracting only the critical (non-metadata) chunks
        /// and checking byte equality of their combined data.
        /// </summary>
        private static bool PngCriticalChunksEqual(string path1, string path2)
        {
            var chunks1 = ReadCriticalChunks(path1);
            var chunks2 = ReadCriticalChunks(path2);

            if (chunks1.Count != chunks2.Count)
                return false;

            for (int i = 0; i < chunks1.Count; i++)
            {
                if (chunks1[i].Type != chunks2[i].Type)
                    return false;
                if (!chunks1[i].Data.AsSpan().SequenceEqual(chunks2[i].Data))
                    return false;
            }
            return true;
        }

        private static List<PngChunk> ReadCriticalChunks(string filePath)
        {
            var chunks = new List<PngChunk>();
            using var stream = File.OpenRead(filePath);
            // Skip the 8-byte PNG signature.
            stream.Seek(PngSignature.Length, SeekOrigin.Begin);

            var intBuf = new byte[4];
            while (stream.Position < stream.Length)
            {
                if (stream.Read(intBuf, 0, 4) < 4) break;
                uint length = ReadBigEndianUInt32(intBuf);

                if (stream.Read(intBuf, 0, 4) < 4) break;
                string type = Encoding.ASCII.GetString(intBuf);

                var data = new byte[length];
                if (length > 0)
                {
                    int totalRead = 0;
                    while (totalRead < (int)length)
                        totalRead += stream.Read(data, totalRead, (int)length - totalRead);
                }

                // Skip the 4-byte CRC.
                stream.Seek(4, SeekOrigin.Current);

                if (PngCriticalChunkTypes.Contains(type))
                {
                    chunks.Add(new PngChunk(type, data));
                }
            }
            return chunks;
        }

        private static uint ReadBigEndianUInt32(byte[] buf)
        {
            return (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);
        }

        private readonly record struct PngChunk(string Type, byte[] Data);
    }
}
