using System.ComponentModel;
using FileDeduplicator.Common;
using Spectre.Console.Cli;

namespace MyApp.Commands;

[Description("Scan files for duplicates.")]
public sealed class ScanCommand : Command<ScanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--path <PATH>")]
        [Description("The path to start scanning files (defaults to the current directory if not provided).")]
        public string StartPath { get; set; }
        
        // TODO: Add any other configuration settings.

        // [CommandOption("-c|--configuration <CONFIGURATION>")]
        // [Description("The configuration to run for. The default for most projects is '[grey]Debug[/]'.")]
        // [DefaultValue("Debug")]
        // public string Configuration { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // SettingsDumper.Dump(settings);
        
        var startPath = string.IsNullOrWhiteSpace(settings.StartPath)
            ? Environment.CurrentDirectory
            : settings.StartPath;
        
        Console.WriteLine($"Scanning files in: {startPath}");
        
        var fileDetailsList = new List<FileDetails>();
        
        // TODO: Scan all files, recursively, and generate SHA-256 hashes.
        //       * Identify files to hash.
        var currentFilePaths = Directory.GetFiles(startPath, "*", SearchOption.AllDirectories);
        // FUTURE: May want to handle the recursion manually to avoid any super-deep file structure delays.
        
        foreach (var filePath in currentFilePaths)
        {
            Console.WriteLine($"Found file: {filePath}");
            var fileInfo = new FileInfo(filePath);
            var hashBytes = FileHelpers.GetFileSha256(filePath);
            // TODO: Compare to `FileHelpers.GetFileSha256(fileInfo.OpenRead())`.
            fileDetailsList.Add(new FileDetails
            {
                DetailsRetrieved = DateTime.UtcNow,
                FilePath = filePath,
                Sha256Hash = hashBytes,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc, // File.GetLastWriteTimeUtc(filePath),
                Created = fileInfo.CreationTimeUtc, // File.GetCreationTimeUtc(filePath),
                LastAccessed = fileInfo.LastAccessTimeUtc, // File.GetLastAccessTimeUtc(filePath),
            });
            Console.WriteLine($"  SHA-256: {hashBytes.ToHexString()}");
        }
        
        // TODO: Store file paths and hashes in a suitable data system (e.g., SQLite or LiteDB).
        // TODO: Compare all hashes and identify duplicates.
        var matches = fileDetailsList
            .Where(fileDetails =>
            {
                return fileDetailsList.Any(otherFileDetails =>
                {
                    return fileDetails.FilePath != otherFileDetails.FilePath
                           && fileDetails.Sha256Hash.AsSpan().SequenceEqual(otherFileDetails.Sha256Hash.AsSpan());
                });
            })
            .ToArray();
        if (matches.Any())
        {
            Console.WriteLine("Found duplicate files:");
            foreach (var match in matches.OrderBy(fileDetails => fileDetails.Sha256Hash.ToHexString()))
            {
                Console.WriteLine($"  {match.FilePath} (SHA-256: {match.Sha256Hash.ToHexString()})");
            }
        }
        else
        {
            Console.WriteLine("No duplicate files found.");
        }
        
        // FUTURE: Identify file types, where possible for querying.
        // FUTURE: Allow for comparing files without metadata to identify duplicates with just metadata diffs (e.g., image EXIF or MP3 ID3 tags).
        
        return 0;
    }
}
