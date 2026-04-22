using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests
{
    [TestFixture]
    public class ImageFileComparerTests
    {
        private string _file1;
        private string _file2;

        [SetUp]
        public void SetUp()
        {
            _file1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            _file2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_file1)) File.Delete(_file1);
            if (File.Exists(_file2)) File.Delete(_file2);
        }

        [Test]
        public void IdenticalImages_AreEquivalent()
        {
            var pngBytes = BuildMinimalPng(pixelValue: 0x00);
            File.WriteAllBytes(_file1, pngBytes);
            File.WriteAllBytes(_file2, pngBytes);
            var comparer = new ImageFileComparer { IgnoreMetadata = false };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IdenticalPixelsWithDifferentMetadata_AreEquivalent_WhenIgnoringMetadata()
        {
            var pixelData = new byte[] { 0x00 };
            File.WriteAllBytes(_file1, BuildPngWithTextChunk(pixelData, "Comment", "Photo A"));
            File.WriteAllBytes(_file2, BuildPngWithTextChunk(pixelData, "Comment", "Photo B"));
            var comparer = new ImageFileComparer { IgnoreMetadata = true };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IdenticalPixelsWithDifferentMetadata_AreNotEquivalent_WhenComparingMetadata()
        {
            var pixelData = new byte[] { 0x00 };
            File.WriteAllBytes(_file1, BuildPngWithTextChunk(pixelData, "Comment", "Photo A"));
            File.WriteAllBytes(_file2, BuildPngWithTextChunk(pixelData, "Comment", "Photo B"));
            var comparer = new ImageFileComparer { IgnoreMetadata = false };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DifferentPixelData_AreNotEquivalent()
        {
            File.WriteAllBytes(_file1, BuildMinimalPng(pixelValue: 0x00));
            File.WriteAllBytes(_file2, BuildMinimalPng(pixelValue: 0xFF));
            var comparer = new ImageFileComparer { IgnoreMetadata = true };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DefaultIgnoreMetadata_IsTrue()
        {
            var comparer = new ImageFileComparer();
            Assert.That(comparer.IgnoreMetadata, Is.True);
        }

        [Test]
        public void DefaultIdentifier_IsImageFileIdentifier()
        {
            var comparer = new ImageFileComparer();
            Assert.That(comparer.Identifier, Is.InstanceOf<ImageFileIdentifier>());
        }

        [Test]
        public void CanCompare_ReturnsTrueForImageExtensions()
        {
            IFileComparer comparer = new ImageFileComparer();
            // CanCompare delegates to the Identifier, so create a real file for extension check
            var jpgPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
            try
            {
                File.WriteAllBytes(jpgPath, BuildMinimalPng(pixelValue: 0x00));
                Assert.That(comparer.CanCompare(jpgPath), Is.True);
            }
            finally
            {
                if (File.Exists(jpgPath)) File.Delete(jpgPath);
            }
        }

        [Test]
        public void CanCompare_ReturnsFalseForNonImageExtensions()
        {
            IFileComparer comparer = new ImageFileComparer();
            var txtPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
            try
            {
                File.WriteAllText(txtPath, "not an image");
                Assert.That(comparer.CanCompare(txtPath), Is.False);
            }
            finally
            {
                if (File.Exists(txtPath)) File.Delete(txtPath);
            }
        }

        /// <summary>
        /// Builds a minimal valid 1x1 grayscale PNG with the given pixel value.
        /// </summary>
        private static byte[] BuildMinimalPng(byte pixelValue)
        {
            using var ms = new MemoryStream();
            // PNG signature
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            // IHDR: 1x1, 8-bit grayscale
            var ihdrData = new byte[13];
            WriteBigEndianUInt32(ihdrData, 0, 1); // width
            WriteBigEndianUInt32(ihdrData, 4, 1); // height
            ihdrData[8] = 8;  // bit depth
            ihdrData[9] = 0;  // color type: grayscale
            ihdrData[10] = 0; // compression
            ihdrData[11] = 0; // filter
            ihdrData[12] = 0; // interlace
            WriteChunk(ms, "IHDR", ihdrData);

            // IDAT: compressed scanline (filter byte 0 + pixel value)
            var rawScanline = new byte[] { 0x00, pixelValue }; // filter=None, then pixel
            var compressedData = DeflateCompress(rawScanline);
            WriteChunk(ms, "IDAT", compressedData);

            // IEND
            WriteChunk(ms, "IEND", Array.Empty<byte>());

            return ms.ToArray();
        }

        /// <summary>
        /// Builds a minimal 1x1 grayscale PNG with a tEXt metadata chunk.
        /// </summary>
        private static byte[] BuildPngWithTextChunk(byte[] pixelData, string keyword, string text)
        {
            byte pixelValue = pixelData.Length > 0 ? pixelData[0] : (byte)0x00;

            using var ms = new MemoryStream();
            // PNG signature
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            // IHDR: 1x1, 8-bit grayscale
            var ihdrData = new byte[13];
            WriteBigEndianUInt32(ihdrData, 0, 1);
            WriteBigEndianUInt32(ihdrData, 4, 1);
            ihdrData[8] = 8;
            ihdrData[9] = 0;
            ihdrData[10] = 0;
            ihdrData[11] = 0;
            ihdrData[12] = 0;
            WriteChunk(ms, "IHDR", ihdrData);

            // tEXt chunk: keyword + null separator + text
            var keywordBytes = System.Text.Encoding.Latin1.GetBytes(keyword);
            var textBytes = System.Text.Encoding.Latin1.GetBytes(text);
            var textChunkData = new byte[keywordBytes.Length + 1 + textBytes.Length];
            Array.Copy(keywordBytes, 0, textChunkData, 0, keywordBytes.Length);
            textChunkData[keywordBytes.Length] = 0x00; // null separator
            Array.Copy(textBytes, 0, textChunkData, keywordBytes.Length + 1, textBytes.Length);
            WriteChunk(ms, "tEXt", textChunkData);

            // IDAT
            var rawScanline = new byte[] { 0x00, pixelValue };
            var compressedData = DeflateCompress(rawScanline);
            WriteChunk(ms, "IDAT", compressedData);

            // IEND
            WriteChunk(ms, "IEND", Array.Empty<byte>());

            return ms.ToArray();
        }

        private static void WriteChunk(MemoryStream ms, string type, byte[] data)
        {
            var lengthBytes = new byte[4];
            WriteBigEndianUInt32(lengthBytes, 0, (uint)data.Length);
            ms.Write(lengthBytes);

            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            ms.Write(typeBytes);

            ms.Write(data);

            // CRC over type + data
            var crcInput = new byte[typeBytes.Length + data.Length];
            Array.Copy(typeBytes, 0, crcInput, 0, typeBytes.Length);
            Array.Copy(data, 0, crcInput, typeBytes.Length, data.Length);
            var crc = Crc32(crcInput);
            var crcBytes = new byte[4];
            WriteBigEndianUInt32(crcBytes, 0, crc);
            ms.Write(crcBytes);
        }

        private static byte[] DeflateCompress(byte[] data)
        {
            using var output = new MemoryStream();
            // zlib header (deflate, no dict, default compression)
            output.WriteByte(0x78);
            output.WriteByte(0x01);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data, 0, data.Length);
            }
            // Adler-32 checksum
            uint adler = Adler32(data);
            var adlerBytes = new byte[4];
            WriteBigEndianUInt32(adlerBytes, 0, adler);
            output.Write(adlerBytes);
            return output.ToArray();
        }

        private static void WriteBigEndianUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        // Standard CRC-32 used by PNG
        private static readonly uint[] Crc32Table = GenerateCrc32Table();

        private static uint[] GenerateCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                table[i] = crc;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
                crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }
}
