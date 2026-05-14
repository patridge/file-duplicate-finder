using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileDeduplicator.Common;

[JsonSerializable(typeof(ScanReport))]
internal partial class ScanReportJsonContext : JsonSerializerContext
{
}

public class ScanReport
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("scanDate")]
    public DateTime ScanDate { get; set; }

    [JsonPropertyName("parameters")]
    public ScanReportParameters Parameters { get; set; } = new();

    [JsonPropertyName("summary")]
    public ScanReportSummary Summary { get; set; } = new();

    [JsonPropertyName("duplicateGroups")]
    public List<ScanReportDuplicateGroup> DuplicateGroups { get; set; } = [];

    [JsonPropertyName("skippedFiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ScanReportSkippedFile>? SkippedFiles { get; set; }

    public static ScanReport Create(
        string[] paths,
        long minSizeBytes,
        bool allowMetadataDiffs,
        bool useCache,
        string[] excludePaths,
        string[] excludeExtensions,
        string[] excludeFileNames,
        List<(string Key, List<FileDetails> Files)> duplicateGroups,
        List<(string Path, string Error)>? skippedFiles = null)
    {
        var report = new ScanReport
        {
            ScanDate = DateTime.UtcNow,
            Parameters = new ScanReportParameters
            {
                Paths = paths,
                ExcludePaths = excludePaths.Length > 0 ? excludePaths : null,
                ExcludeExtensions = excludeExtensions.Length > 0 ? excludeExtensions : null,
                ExcludeFileNames = excludeFileNames.Length > 0 ? excludeFileNames : null,
                MinSizeBytes = minSizeBytes,
                AllowMetadataDiffs = allowMetadataDiffs,
                UseCache = useCache,
            },
            DuplicateGroups = duplicateGroups.Select(g => new ScanReportDuplicateGroup
            {
                Hash = g.Key,
                FileSize = g.Files[0].FileSize,
                FileCount = g.Files.Count,
                Files = g.Files.Select(f => new ScanReportFile
                {
                    Path = f.FilePath,
                    Size = f.FileSize,
                    Sha256 = f.Sha256Hash.ToHexString(),
                    LastModified = f.LastModified,
                    Created = f.Created,
                }).ToList(),
            }).ToList(),
        };

        report.Summary = new ScanReportSummary
        {
            TotalDuplicateGroups = report.DuplicateGroups.Count,
            TotalDuplicateFiles = report.DuplicateGroups.Sum(g => g.FileCount),
            TotalWastedBytes = report.DuplicateGroups.Sum(g => g.FileSize * (g.FileCount - 1)),
        };

        if (skippedFiles is { Count: > 0 })
        {
            report.SkippedFiles = skippedFiles.Select(s => new ScanReportSkippedFile
            {
                Path = s.Path,
                Error = s.Error,
            }).ToList();
        }

        return report;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new ScanReportJsonContext(new JsonSerializerOptions
        {
            WriteIndented = true,
        }).ScanReport);
    }
}

public class ScanReportParameters
{
    [JsonPropertyName("paths")]
    public string[] Paths { get; set; } = [];

    [JsonPropertyName("excludePaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ExcludePaths { get; set; }

    [JsonPropertyName("excludeExtensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ExcludeExtensions { get; set; }

    [JsonPropertyName("excludeFileNames")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ExcludeFileNames { get; set; }

    [JsonPropertyName("minSizeBytes")]
    public long MinSizeBytes { get; set; }

    [JsonPropertyName("allowMetadataDiffs")]
    public bool AllowMetadataDiffs { get; set; }

    [JsonPropertyName("useCache")]
    public bool UseCache { get; set; }
}

public class ScanReportSummary
{
    [JsonPropertyName("totalDuplicateGroups")]
    public int TotalDuplicateGroups { get; set; }

    [JsonPropertyName("totalDuplicateFiles")]
    public int TotalDuplicateFiles { get; set; }

    [JsonPropertyName("totalWastedBytes")]
    public long TotalWastedBytes { get; set; }
}

public class ScanReportDuplicateGroup
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("files")]
    public List<ScanReportFile> Files { get; set; } = [];
}

public class ScanReportFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }
}

public class ScanReportSkippedFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}
