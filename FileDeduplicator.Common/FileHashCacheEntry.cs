namespace FileDeduplicator.Common;

public class FileHashCacheEntry
{
    public required string FilePath { get; set; }
    public required string Sha256Hash { get; set; }
    public required long FileSize { get; set; }
    public required DateTime LastModified { get; set; }
    public required DateTime Created { get; set; }
    public required DateTime CachedAt { get; set; }
}
