using System.ComponentModel;
using FileDeduplicator.Common;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator.Commands;

[Description("Scan files for duplicates.")]
public sealed class ScanCommand : Command<ScanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--path <PATH>")]
        [Description("The path to start scanning files (defaults to the current directory if not provided).")]
        public required string StartPath { get; set; }
        
        // TODO: Add any other configuration settings.

        // [CommandOption("-c|--configuration <CONFIGURATION>")]
        // [Description("The configuration to run for. The default for most projects is '[grey]Debug[/]'.")]
        // [DefaultValue("Debug")]
        // public string Configuration { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // SettingsDumper.Dump(settings);
        var startPath = string.IsNullOrWhiteSpace(settings.StartPath)
            ? Environment.CurrentDirectory
            : settings.StartPath;
        Console.WriteLine($"Scanning files in: {startPath}");

        var scanner = new FileScanner();
        var fileDetailsList = scanner.ScanDirectory(startPath);
        foreach (var file in fileDetailsList)
        {
            Console.WriteLine($"Found file: {file.FilePath}");
            Console.WriteLine($"  SHA-256: {file.Sha256Hash.ToHexString()}");
        }

        // TODO: Store file paths and hashes in a suitable data system (e.g., SQLite or LiteDB).
        // TODO: Compare all hashes and identify duplicates.
        var duplicateGroups = fileDetailsList
            .GroupBy(fd => fd.Sha256Hash.ToHexString())
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .ToArray();

        if (duplicateGroups.Any())
        {
            Console.WriteLine("Found duplicate files:");
            Console.WriteLine();

            foreach (var group in duplicateGroups)
            {
                var table = new Table();
                table.Title = new TableTitle($"[bold yellow]SHA-256: {group.Key}[/]");
                table.AddColumn(new TableColumn("[bold]Filename[/]").LeftAligned());
                table.AddColumn(new TableColumn("[bold]Path[/]").LeftAligned());

                foreach (var file in group)
                {
                    var fileName = Path.GetFileName(file.FilePath);
                    var directoryPath = Path.GetDirectoryName(file.FilePath) ?? string.Empty;
                    table.AddRow(fileName, directoryPath);
                }

                AnsiConsole.Write(table);
                Console.WriteLine();
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
