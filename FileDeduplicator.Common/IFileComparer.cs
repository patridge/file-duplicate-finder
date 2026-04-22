namespace FileDeduplicator.Common
{
    public interface IFileComparer
    {
        /// <summary>
        /// An optional identifier used to determine if a file is of a type this comparer can handle.
        /// When null, the comparer cannot identify whether it handles a given file.
        /// </summary>
        IFileTypeIdentifier? Identifier { get; set; }

        /// <summary>
        /// When true, the comparer ignores format-specific metadata differences (e.g., ID3 tags, EXIF data, timestamps)
        /// and compares only the primary content of the files. Defaults to true.
        /// </summary>
        bool IgnoreMetadata { get; set; }

        /// <summary>
        /// Returns true if this comparer can handle the given file, based on its Identifier.
        /// When no Identifier is set, returns false.
        /// </summary>
        bool CanCompare(string filePath)
        {
            return Identifier?.IsMatch(filePath) ?? false;
        }

        /// <summary>
        /// Compares two files and returns true if they are considered equivalent according to the comparer logic.
        /// </summary>
        /// <param name="filePath1">Path to the first file.</param>
        /// <param name="filePath2">Path to the second file.</param>
        /// <returns>True if files are equivalent, false otherwise.</returns>
        bool AreFilesEquivalent(string filePath1, string filePath2);
    }
}
