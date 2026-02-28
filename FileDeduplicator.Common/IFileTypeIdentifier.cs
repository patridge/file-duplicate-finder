namespace FileDeduplicator.Common
{
    /// <summary>
    /// Analyzes a file to determine if it is of a type that a particular comparer can handle.
    /// </summary>
    public interface IFileTypeIdentifier
    {
        /// <summary>
        /// Returns true if the given file is of a type this identifier recognizes.
        /// </summary>
        /// <param name="filePath">Path to the file to analyze.</param>
        /// <returns>True if the file matches the recognized type.</returns>
        bool IsMatch(string filePath);
    }
}
