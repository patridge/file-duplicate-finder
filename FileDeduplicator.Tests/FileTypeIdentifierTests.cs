using System;
using System.IO;
using System.Text;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests
{
    [TestFixture]
    public class AudioFileIdentifierTests
    {
        private string _tempFile = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        [TestCase(".mp3")]
        [TestCase(".flac")]
        [TestCase(".ogg")]
        [TestCase(".wav")]
        [TestCase(".aac")]
        [TestCase(".m4a")]
        [TestCase(".opus")]
        [TestCase(".MP3")]
        public void IsMatch_KnownAudioExtension_ReturnsTrue(string extension)
        {
            _tempFile = _tempFile + extension;
            File.WriteAllText(_tempFile, "dummy");
            var identifier = new AudioFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.True);
        }

        [Test]
        public void IsMatch_Mp3MagicBytes_WithoutExtension_ReturnsTrue()
        {
            _tempFile = _tempFile + ".bin";
            // Write MP3 sync word: 0xFF 0xFB
            File.WriteAllBytes(_tempFile, new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x00 });
            var identifier = new AudioFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.True);
        }

        [Test]
        public void IsMatch_Id3MagicBytes_WithoutExtension_ReturnsTrue()
        {
            _tempFile = _tempFile + ".bin";
            File.WriteAllBytes(_tempFile, Encoding.ASCII.GetBytes("ID3\x03\x00\x00\x00\x00\x00\x00"));
            var identifier = new AudioFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.True);
        }

        [TestCase(".txt")]
        [TestCase(".png")]
        [TestCase(".jpg")]
        [TestCase(".csv")]
        public void IsMatch_NonAudioExtension_NoAudioMagicBytes_ReturnsFalse(string extension)
        {
            _tempFile = _tempFile + extension;
            File.WriteAllText(_tempFile, "not audio data");
            var identifier = new AudioFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.False);
        }

        [Test]
        public void IsMatch_EmptyFile_NoAudioExtension_ReturnsFalse()
        {
            _tempFile = _tempFile + ".bin";
            File.WriteAllBytes(_tempFile, Array.Empty<byte>());
            var identifier = new AudioFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.False);
        }
    }

    [TestFixture]
    public class ImageFileIdentifierTests
    {
        private string _tempFile = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        [TestCase(".jpeg")]
        [TestCase(".gif")]
        [TestCase(".bmp")]
        [TestCase(".webp")]
        [TestCase(".heic")]
        [TestCase(".PNG")]
        public void IsMatch_KnownImageExtension_ReturnsTrue(string extension)
        {
            _tempFile = _tempFile + extension;
            File.WriteAllText(_tempFile, "dummy");
            var identifier = new ImageFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.True);
        }

        [Test]
        public void IsMatch_PngMagicBytes_WithoutExtension_ReturnsTrue()
        {
            _tempFile = _tempFile + ".bin";
            File.WriteAllBytes(_tempFile, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A });
            var identifier = new ImageFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.True);
        }

        [Test]
        public void IsMatch_JpegMagicBytes_WithoutExtension_ReturnsTrue()
        {
            _tempFile = _tempFile + ".bin";
            File.WriteAllBytes(_tempFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });
            var identifier = new ImageFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.True);
        }

        [TestCase(".txt")]
        [TestCase(".mp3")]
        [TestCase(".csv")]
        public void IsMatch_NonImageExtension_NoImageMagicBytes_ReturnsFalse(string extension)
        {
            _tempFile = _tempFile + extension;
            File.WriteAllText(_tempFile, "not image data");
            var identifier = new ImageFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.False);
        }

        [Test]
        public void IsMatch_EmptyFile_NoImageExtension_ReturnsFalse()
        {
            _tempFile = _tempFile + ".bin";
            File.WriteAllBytes(_tempFile, Array.Empty<byte>());
            var identifier = new ImageFileIdentifier();

            Assert.That(identifier.IsMatch(_tempFile), Is.False);
        }
    }

    [TestFixture]
    public class ComparerIdentifierIntegrationTests
    {
        [Test]
        public void AudioFileComparer_HasAudioIdentifierByDefault()
        {
            var comparer = new AudioFileComparer();

            Assert.That(comparer.Identifier, Is.Not.Null);
            Assert.That(comparer.Identifier, Is.InstanceOf<AudioFileIdentifier>());
        }

        [Test]
        public void ImageFileComparer_HasImageIdentifierByDefault()
        {
            var comparer = new ImageFileComparer();

            Assert.That(comparer.Identifier, Is.Not.Null);
            Assert.That(comparer.Identifier, Is.InstanceOf<ImageFileIdentifier>());
        }

        [Test]
        public void BinaryFileComparer_HasNoIdentifierByDefault()
        {
            var comparer = new BinaryFileComparer();

            Assert.That(comparer.Identifier, Is.Null);
        }

        [Test]
        public void CanCompare_WithNullIdentifier_DefaultImplementation_ReturnsFalse()
        {
            IFileComparer comparer = new StubFileComparer();

            Assert.That(comparer.CanCompare("anything.xyz"), Is.False);
        }

        [Test]
        public void CanCompare_BinaryComparer_ReturnsTrueForAnyFile()
        {
            IFileComparer comparer = new BinaryFileComparer();

            Assert.That(comparer.CanCompare("anything.xyz"), Is.True);
        }

        [Test]
        public void CanCompare_AudioComparer_ReturnsTrueForMp3()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
            File.WriteAllText(tempFile, "dummy");
            try
            {
                IFileComparer comparer = new AudioFileComparer();
                Assert.That(comparer.CanCompare(tempFile), Is.True);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public void CanCompare_AudioComparer_ReturnsFalseForPng()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            File.WriteAllText(tempFile, "dummy");
            try
            {
                IFileComparer comparer = new AudioFileComparer();
                Assert.That(comparer.CanCompare(tempFile), Is.False);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Minimal stub that relies on the default IFileComparer.CanCompare implementation.
    /// </summary>
    internal class StubFileComparer : IFileComparer
    {
        public IFileTypeIdentifier? Identifier { get; set; }
        public bool IgnoreMetadata { get; set; } = true;
        public bool AreFilesEquivalent(string filePath1, string filePath2) => false;
    }
}
