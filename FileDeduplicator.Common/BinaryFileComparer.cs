using System.IO;

namespace FileDeduplicator.Common
{
    public class BinaryFileComparer : IFileComparer
    {
        public IFileTypeIdentifier? Identifier { get; set; }

        /// <summary>
        /// When true, ignores file timestamp differences (last write, creation time).
        /// </summary>
        public bool IgnoreMetadata { get; set; } = false;

        /// <summary>
        /// BinaryFileComparer can compare any file type.
        /// </summary>
        public bool CanCompare(string filePath) => true;

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            var file1 = new FileInfo(filePath1);
            var file2 = new FileInfo(filePath2);
            if (!IgnoreMetadata)
            {
                if (file1.LastWriteTimeUtc != file2.LastWriteTimeUtc || file1.CreationTimeUtc != file2.CreationTimeUtc)
                    return false;
            }
            if (file1.Length != file2.Length)
                return false;
            using var stream1 = file1.OpenRead();
            using var stream2 = file2.OpenRead();
            for (int i = 0; i < file1.Length; i++)
            {
                int b1 = stream1.ReadByte();
                int b2 = stream2.ReadByte();
                if (b1 != b2)
                    return false;
            }
            return true;
        }
    }
}
