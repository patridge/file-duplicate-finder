namespace FileDeduplicator.Common
{
    public class ImageFileComparer : IFileComparer
    {
        public IFileTypeIdentifier? Identifier { get; set; } = new ImageFileIdentifier();

        /// <summary>
        /// When true, ignores EXIF/metadata differences and compares only pixel data.
        /// </summary>
        public bool IgnoreMetadata { get; set; } = true;

        public bool AreFilesEquivalent(string filePath1, string filePath2)
        {
            // Stub: Always returns false for now
            // Real implementation would compare pixel data, optionally ignoring EXIF
            return false;
        }
    }
}
