using System;
using System.IO;

namespace FileDeduplicator.Common
{
    public class AudioFileComparer : IFileComparer
    {
        public IFileTypeIdentifier? Identifier { get; set; } = new AudioFileIdentifier();

        /// <summary>
        /// When true, ignores ID3 tag differences and compares only audio frame data.
        /// </summary>
        public bool IgnoreMetadata { get; set; } = true;

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            var bytes1 = File.ReadAllBytes(filePath1);
            var bytes2 = File.ReadAllBytes(filePath2);

            int offset1 = IgnoreMetadata ? GetAudioDataOffset(bytes1) : 0;
            int offset2 = IgnoreMetadata ? GetAudioDataOffset(bytes2) : 0;

            int len1 = bytes1.Length - offset1;
            int len2 = bytes2.Length - offset2;

            if (len1 != len2)
                return false;

            for (int i = 0; i < len1; i++)
            {
                if (bytes1[offset1 + i] != bytes2[offset2 + i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the byte offset where audio data begins, skipping any ID3v2 tag.
        /// </summary>
        private static int GetAudioDataOffset(byte[] data)
        {
            // ID3v2 header: "ID3" + version (2 bytes) + flags (1 byte) + size (4 bytes synchsafe)
            if (data.Length >= 10
                && data[0] == 'I'
                && data[1] == 'D'
                && data[2] == '3')
            {
                int size = (data[6] & 0x7F) << 21
                         | (data[7] & 0x7F) << 14
                         | (data[8] & 0x7F) << 7
                         | (data[9] & 0x7F);
                return 10 + size;
            }

            return 0;
        }
    }
}
