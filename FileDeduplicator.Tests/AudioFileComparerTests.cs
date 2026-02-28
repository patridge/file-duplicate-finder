using System;
using System.IO;
using System.Text;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests
{
    [TestFixture]
    public class AudioFileComparerTests
    {
        private string _file1;
        private string _file2;

        [SetUp]
        public void SetUp()
        {
            _file1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
            _file2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_file1)) File.Delete(_file1);
            if (File.Exists(_file2)) File.Delete(_file2);
        }

        [Test]
        public void IdenticalAudioWithSameId3Tags_AreEquivalent()
        {
            var mp3Bytes = BuildMinimalMp3(title: "Test Song");
            File.WriteAllBytes(_file1, mp3Bytes);
            File.WriteAllBytes(_file2, mp3Bytes);
            var comparer = new AudioFileComparer { IgnoreId3Tags = false };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IdenticalAudioWithDifferentId3Tags_AreEquivalent_WhenIgnoringTags()
        {
            var audioData = BuildMinimalMp3Frame();
            File.WriteAllBytes(_file1, BuildMp3WithId3(title: "Song A", audioData: audioData));
            File.WriteAllBytes(_file2, BuildMp3WithId3(title: "Song B", audioData: audioData));
            var comparer = new AudioFileComparer { IgnoreId3Tags = true };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IdenticalAudioWithDifferentId3Tags_AreNotEquivalent_WhenComparingTags()
        {
            var audioData = BuildMinimalMp3Frame();
            File.WriteAllBytes(_file1, BuildMp3WithId3(title: "Song A", audioData: audioData));
            File.WriteAllBytes(_file2, BuildMp3WithId3(title: "Song B", audioData: audioData));
            var comparer = new AudioFileComparer { IgnoreId3Tags = false };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DifferentAudioWithSameId3Tags_AreNotEquivalent()
        {
            File.WriteAllBytes(_file1, BuildMinimalMp3(title: "Same Title", audioByte: 0x00));
            File.WriteAllBytes(_file2, BuildMinimalMp3(title: "Same Title", audioByte: 0x7F));
            var comparer = new AudioFileComparer { IgnoreId3Tags = true };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Builds a minimal MP3 file: an ID3v2.3 tag with a TIT2 frame + one silent MP3 frame.
        /// </summary>
        private static byte[] BuildMinimalMp3(string title = "Test", byte audioByte = 0x00)
        {
            var audioData = BuildMinimalMp3Frame(audioByte);
            return BuildMp3WithId3(title, audioData);
        }

        /// <summary>
        /// Builds a single MPEG1 Layer III frame (header + data).
        /// MPEG1, Layer III, 128 kbps, 44100 Hz => frame size = 417 bytes.
        /// </summary>
        private static byte[] BuildMinimalMp3Frame(byte fillByte = 0x00)
        {
            const int frameSize = 417; // floor(144 * 128000 / 44100)
            var frame = new byte[frameSize];
            // MP3 frame header: sync + MPEG1 + Layer III + no CRC + 128kbps + 44100Hz + stereo
            frame[0] = 0xFF;
            frame[1] = 0xFB;
            frame[2] = 0x90;
            frame[3] = 0x00;
            // Fill remaining bytes with the given fill byte (silence placeholder)
            for (int i = 4; i < frameSize; i++)
                frame[i] = fillByte;
            return frame;
        }

        /// <summary>
        /// Wraps audio data with an ID3v2.3 tag containing a TIT2 (title) frame.
        /// </summary>
        private static byte[] BuildMp3WithId3(string title, byte[] audioData)
        {
            var titleBytes = Encoding.Latin1.GetBytes(title);

            // TIT2 frame: "TIT2" (4) + size (4, big-endian) + flags (2) + encoding (1) + text
            int tit2DataSize = 1 + titleBytes.Length; // encoding byte + text
            var tit2Frame = new byte[10 + tit2DataSize];
            tit2Frame[0] = (byte)'T';
            tit2Frame[1] = (byte)'I';
            tit2Frame[2] = (byte)'T';
            tit2Frame[3] = (byte)'2';
            tit2Frame[4] = (byte)((tit2DataSize >> 24) & 0xFF);
            tit2Frame[5] = (byte)((tit2DataSize >> 16) & 0xFF);
            tit2Frame[6] = (byte)((tit2DataSize >> 8) & 0xFF);
            tit2Frame[7] = (byte)(tit2DataSize & 0xFF);
            tit2Frame[8] = 0x00; // flags
            tit2Frame[9] = 0x00;
            tit2Frame[10] = 0x00; // encoding: ISO-8859-1
            Array.Copy(titleBytes, 0, tit2Frame, 11, titleBytes.Length);

            // ID3v2.3 header (10 bytes)
            int tagDataSize = tit2Frame.Length;
            var id3Header = new byte[10];
            id3Header[0] = (byte)'I';
            id3Header[1] = (byte)'D';
            id3Header[2] = (byte)'3';
            id3Header[3] = 0x03; // version 2.3
            id3Header[4] = 0x00; // revision
            id3Header[5] = 0x00; // flags
            // Size as synchsafe integer (4 x 7 bits)
            id3Header[6] = (byte)((tagDataSize >> 21) & 0x7F);
            id3Header[7] = (byte)((tagDataSize >> 14) & 0x7F);
            id3Header[8] = (byte)((tagDataSize >> 7) & 0x7F);
            id3Header[9] = (byte)(tagDataSize & 0x7F);

            // Combine: ID3 header + TIT2 frame + audio data
            var result = new byte[id3Header.Length + tit2Frame.Length + audioData.Length];
            Array.Copy(id3Header, 0, result, 0, id3Header.Length);
            Array.Copy(tit2Frame, 0, result, id3Header.Length, tit2Frame.Length);
            Array.Copy(audioData, 0, result, id3Header.Length + tit2Frame.Length, audioData.Length);
            return result;
        }
    }
}
