namespace FileDeduplicator.Common;

public class FileDetails
{
    public DateTime DetailsRetrieved { get; set; }
    public string FilePath { get; set; }
    public byte[] Sha256Hash { get; set; }
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime Created { get; set; }
    public DateTime LastAccessed { get; set; }
    // FUTURE: File type
    // FUTURE: Metadata (e.g., image EXIF, MP3 ID3 tags, etc.)
}