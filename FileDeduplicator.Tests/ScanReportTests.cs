using System.Text.Json;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests
{
    [TestFixture]
    public class ScanReportTests
    {
        [Test]
        public void Create_ProducesValidReport()
        {
            var duplicateGroups = new List<(string Key, List<FileDetails> Files)>
            {
                ("abc123", new List<FileDetails>
                {
                    new FileDetails
                    {
                        FilePath = "/path/to/file1.txt",
                        Sha256Hash = new byte[] { 0xab, 0xc1, 0x23 },
                        FileSize = 1024,
                        DetailsRetrieved = DateTime.UtcNow,
                        LastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        Created = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastAccessed = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    },
                    new FileDetails
                    {
                        FilePath = "/path/to/file2.txt",
                        Sha256Hash = new byte[] { 0xab, 0xc1, 0x23 },
                        FileSize = 1024,
                        DetailsRetrieved = DateTime.UtcNow,
                        LastModified = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                        Created = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastAccessed = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                    },
                }),
            };

            var report = ScanReport.Create(
                paths: ["/scan/path"],
                minSizeBytes: 1024,
                allowMetadataDiffs: false,
                useCache: true,
                excludePaths: ["/scan/path/skip"],
                excludeExtensions: [".log"],
                excludeFileNames: [".DS_Store"],
                duplicateGroups: duplicateGroups);

            Assert.That(report.Version, Is.EqualTo("1.0"));
            Assert.That(report.Parameters.Paths, Is.EqualTo(new[] { "/scan/path" }));
            Assert.That(report.Parameters.MinSizeBytes, Is.EqualTo(1024));
            Assert.That(report.Parameters.AllowMetadataDiffs, Is.False);
            Assert.That(report.Parameters.UseCache, Is.True);
            Assert.That(report.Parameters.ExcludePaths, Is.EqualTo(new[] { "/scan/path/skip" }));
            Assert.That(report.Parameters.ExcludeExtensions, Is.EqualTo(new[] { ".log" }));
            Assert.That(report.Parameters.ExcludeFileNames, Is.EqualTo(new[] { ".DS_Store" }));
            Assert.That(report.Summary.TotalDuplicateGroups, Is.EqualTo(1));
            Assert.That(report.Summary.TotalDuplicateFiles, Is.EqualTo(2));
            Assert.That(report.Summary.TotalWastedBytes, Is.EqualTo(1024));
            Assert.That(report.DuplicateGroups, Has.Count.EqualTo(1));
            Assert.That(report.DuplicateGroups[0].FileCount, Is.EqualTo(2));
            Assert.That(report.DuplicateGroups[0].Files[0].Path, Is.EqualTo("/path/to/file1.txt"));
            Assert.That(report.SkippedFiles, Is.Null);
        }

        [Test]
        public void Create_IncludesSkippedFiles_WhenPresent()
        {
            var duplicateGroups = new List<(string Key, List<FileDetails> Files)>();
            var skippedFiles = new List<(string Path, string Error)>
            {
                ("/path/to/locked.bin", "Access denied"),
            };

            var report = ScanReport.Create(
                paths: ["/scan/path"],
                minSizeBytes: 0,
                allowMetadataDiffs: false,
                useCache: false,
                excludePaths: [],
                excludeExtensions: [],
                excludeFileNames: [],
                duplicateGroups: duplicateGroups,
                skippedFiles: skippedFiles);

            Assert.That(report.SkippedFiles, Is.Not.Null);
            Assert.That(report.SkippedFiles, Has.Count.EqualTo(1));
            Assert.That(report.SkippedFiles![0].Path, Is.EqualTo("/path/to/locked.bin"));
            Assert.That(report.SkippedFiles[0].Error, Is.EqualTo("Access denied"));
        }

        [Test]
        public void ToJson_ProducesValidJson()
        {
            var duplicateGroups = new List<(string Key, List<FileDetails> Files)>
            {
                ("deadbeef", new List<FileDetails>
                {
                    new FileDetails
                    {
                        FilePath = "/a/file.txt",
                        Sha256Hash = new byte[] { 0xde, 0xad, 0xbe, 0xef },
                        FileSize = 512,
                        DetailsRetrieved = DateTime.UtcNow,
                        LastModified = DateTime.UtcNow,
                        Created = DateTime.UtcNow,
                        LastAccessed = DateTime.UtcNow,
                    },
                    new FileDetails
                    {
                        FilePath = "/b/file.txt",
                        Sha256Hash = new byte[] { 0xde, 0xad, 0xbe, 0xef },
                        FileSize = 512,
                        DetailsRetrieved = DateTime.UtcNow,
                        LastModified = DateTime.UtcNow,
                        Created = DateTime.UtcNow,
                        LastAccessed = DateTime.UtcNow,
                    },
                }),
            };

            var report = ScanReport.Create(
                paths: ["/test"],
                minSizeBytes: 0,
                allowMetadataDiffs: true,
                useCache: false,
                excludePaths: [],
                excludeExtensions: [],
                excludeFileNames: [],
                duplicateGroups: duplicateGroups);

            var json = report.ToJson();

            // Verify it's valid JSON by deserializing
            var deserialized = JsonSerializer.Deserialize<ScanReport>(json);
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Version, Is.EqualTo("1.0"));
            Assert.That(deserialized.Parameters.AllowMetadataDiffs, Is.True);
            Assert.That(deserialized.DuplicateGroups, Has.Count.EqualTo(1));
            Assert.That(deserialized.DuplicateGroups[0].Files, Has.Count.EqualTo(2));
        }

        [Test]
        public void ToJson_OmitsNullOptionalFields()
        {
            var report = ScanReport.Create(
                paths: ["/test"],
                minSizeBytes: 0,
                allowMetadataDiffs: false,
                useCache: false,
                excludePaths: [],
                excludeExtensions: [],
                excludeFileNames: [],
                duplicateGroups: []);

            var json = report.ToJson();

            Assert.That(json, Does.Not.Contain("\"excludePaths\""));
            Assert.That(json, Does.Not.Contain("\"excludeExtensions\""));
            Assert.That(json, Does.Not.Contain("\"excludeFileNames\""));
            Assert.That(json, Does.Not.Contain("\"skippedFiles\""));
        }
    }
}
