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
            AnsiConsole.MarkupLine("[green]Found duplicate files![/]");
            AnsiConsole.WriteLine();

            const int pageSize = 10;
            int currentPage = 0;
            int totalPages = (int)Math.Ceiling(duplicateGroups.Length / (double)pageSize);
            bool exitRequested = false;
            while (!exitRequested)
            {
                // Calculate page bounds
                int startIdx = currentPage * pageSize;
                int endIdx = Math.Min(startIdx + pageSize, duplicateGroups.Length);
                var pageGroups = duplicateGroups.Skip(startIdx).Take(pageSize).ToArray();

                // Build enhanced group labels for this page
                var hashLabelMap = new Dictionary<string, string>();
                foreach (var g in pageGroups) {
                    var files = g.ToList();
                    var fileNames = files.Select(f => Path.GetFileName(f.FilePath)).Distinct().ToList();
                    string prefix;
                    if (fileNames.Count == 1)
                    {
                        prefix = $"{fileNames[0]}";
                    }
                    else
                    {
                        // Try to get common extension
                        var extensions = files.Select(f => Path.GetExtension(f.FilePath).ToLowerInvariant()).Distinct().ToList();
                        if (extensions.Count == 1 && !string.IsNullOrWhiteSpace(extensions[0]))
                        {
                            prefix = $"{extensions[0]} file";
                        }
                        else
                        {
                            prefix = "multiple files";
                        }
                    }
                    var hashEscaped = g.Key.Replace("[", "[[").Replace("]", "]]");
                    var label = $"{prefix} [[{hashEscaped}]] ({g.Count()} files)";
                    hashLabelMap[label] = g.Key;
                }
                var hashChoices = new List<string>();
                // Paging indicator
                hashChoices.Add($"[grey]Page {currentPage + 1} of {totalPages}[/]");
                // Prev/Next page menu items
                if (currentPage > 0)
                    hashChoices.Add("[blue]Prev Page[/]");
                hashChoices.AddRange(hashLabelMap.Keys);
                if (currentPage < totalPages - 1)
                    hashChoices.Add("[blue]Next Page[/]");
                hashChoices.Add("[red]Exit[/]");

                var selectedLabel = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[bold yellow]Select a duplicate group, page, or Exit:[/]")
                        .PageSize(pageSize + 3)
                        .AddChoices(hashChoices)
                );

                if (selectedLabel == "[red]Exit[/]")
                {
                    exitRequested = true;
                    break;
                }
                if (selectedLabel == "[blue]Prev Page[/]")
                {
                    if (currentPage > 0) currentPage--;
                    continue;
                }
                if (selectedLabel == "[blue]Next Page[/]")
                {
                    if (currentPage < totalPages - 1) currentPage++;
                    continue;
                }
                // Ignore paging indicator line
                if (selectedLabel.StartsWith("[grey]Page "))
                {
                    continue;
                }

                if (!hashLabelMap.TryGetValue(selectedLabel, out var selectedKey) || string.IsNullOrEmpty(selectedKey))
                {
                    AnsiConsole.MarkupLine("[red]Could not determine hash from selection. Skipping group.[/]");
                    continue;
                }
                var selectedGroup = duplicateGroups.First(g => g.Key == selectedKey);

                bool backRequested = false;
                while (!backRequested)
                {
                    // Show files in the selected group
                    var table = new Table();
                    table.Title = new TableTitle($"[bold yellow]SHA-256: {selectedGroup.Key}[/]");
                    table.AddColumn(new TableColumn("[bold]Filename[/]").LeftAligned());
                    table.AddColumn(new TableColumn("[bold]Path[/]").LeftAligned());

                    foreach (var file in selectedGroup)
                    {
                        var fileName = Path.GetFileName(file.FilePath);
                        var directoryPath = Path.GetDirectoryName(file.FilePath) ?? string.Empty;
                        table.AddRow(fileName, directoryPath);
                    }

                    AnsiConsole.Write(table);

                    // Let user select a file for further action or go back
                    var fileChoices = selectedGroup.Select(f => f.FilePath).ToList();
                    fileChoices.Add("[yellow]Back[/]");
                    var selectedFile = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold]Select a file to open location, or Back:[/]")
                            .PageSize(10)
                            .AddChoices(fileChoices)
                    );

                    if (selectedFile == "[yellow]Back[/]")
                    {
                        backRequested = true;
                        continue;
                    }

                    AnsiConsole.MarkupLine($"[blue]You selected:[/] {selectedFile}");
                    // Open the folder containing the selected file in the system's file explorer
                    var directory = Path.GetDirectoryName(selectedFile);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        try
                        {
                            var absFile = Path.GetFullPath(selectedFile);
                            if (OperatingSystem.IsWindows())
                            {
                                // Open Explorer and select the file
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "explorer",
                                    Arguments = $"/select,\"{absFile}\"",
                                    UseShellExecute = true
                                };
                                System.Diagnostics.Process.Start(psi);
                            }
                            else if (OperatingSystem.IsMacOS())
                            {
                                // Open Finder and select the file (must use absolute path, double quotes for spaces)
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "open",
                                    Arguments = $"-R \"{absFile}\"",
                                    UseShellExecute = true
                                };
                                var process = System.Diagnostics.Process.Start(psi);
                                if (process == null)
                                {
                                    AnsiConsole.MarkupLine($"[red]Failed to start Finder process for: {absFile}[/]");
                                }
                            }
                            else if (OperatingSystem.IsLinux())
                            {
                                // Try to open the folder with the default file manager
                                var absDir = Path.GetFullPath(directory);
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "xdg-open",
                                    Arguments = $"'{absDir.Replace("'", "'\\''")}'",
                                    UseShellExecute = true
                                };
                                System.Diagnostics.Process.Start(psi);
                            }
                            else
                            {
                                AnsiConsole.MarkupLine("[yellow]Cannot open folder: unsupported OS.[/]");
                            }
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to open folder: {ex.Message}[/]");
                        }
                    }
                }
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]No duplicate files found.[/]");
        }
        
        // FUTURE: Identify file types, where possible for querying.
        // FUTURE: Allow for comparing files without metadata to identify duplicates with just metadata diffs (e.g., image EXIF or MP3 ID3 tags).
        
        return 0;
    }
}
