namespace FileDeduplicator.Common;

public class FileDetails
{
    public required string FilePath { get; set; }
    public required DateTime DetailsRetrieved { get; set; }
    public required byte[] Sha256Hash { get; set; }
    public required long FileSize { get; set; }
    public required DateTime LastModified { get; set; }
    public required DateTime Created { get; set; }
    public required DateTime LastAccessed { get; set; }
    // FUTURE: File type
    // FUTURE: Metadata (e.g., image EXIF, MP3 ID3 tags, etc.)
}