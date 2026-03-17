using System.ComponentModel;
using FileDeduplicator.Common;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator.Commands;

[Description("Find duplicates of large files, filtering by minimum file size.")]
public sealed class FindLargeDuplicatesCommand : Command<FindLargeDuplicatesCommand.Settings>
{
    private const long DefaultMinSizeBytes = 1L * 1024 * 1024 * 1024; // 1 GB

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--path <PATH>")]
        [Description("The path to start scanning files (defaults to the current directory if not provided).")]
        public required string StartPath { get; set; }

        [CommandOption("-s|--min-size <SIZE>")]
        [Description("Minimum file size in bytes to consider (defaults to 1 GB). Supports suffixes: KB, MB, GB, TB (e.g., '500MB', '2GB').")]
        [TypeConverter(typeof(FileSizeConverter))]
        [DefaultValue(DefaultMinSizeBytes)]
        public long MinSizeBytes { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var startPath = string.IsNullOrWhiteSpace(settings.StartPath)
            ? Environment.CurrentDirectory
            : settings.StartPath;

        if (!Directory.Exists(startPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Directory not found: {Markup.Escape(startPath)}");
            return 1;
        }

        AnsiConsole.MarkupLine($"Scanning for large duplicate files in: [blue]{Markup.Escape(startPath)}[/]");
        AnsiConsole.MarkupLine($"Minimum file size: [blue]{FormatFileSize(settings.MinSizeBytes)}[/]");
        AnsiConsole.WriteLine();

        var scanner = new FileScanner();
        var fileDetailsList = scanner.ScanDirectoryForLargeDuplicates(
            startPath,
            settings.MinSizeBytes,
            onStatus: message => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]")
        );

        var duplicateGroups = fileDetailsList
            .GroupBy(fd => fd.Sha256Hash.ToHexString())
            .OrderByDescending(g => g.First().FileSize)
            .ToArray();

        if (duplicateGroups.Length == 0)
        {
            AnsiConsole.MarkupLine("[green]No duplicate large files found.[/]");
            return 0;
        }

        var totalWastedBytes = duplicateGroups.Sum(g => g.First().FileSize * (g.Count() - 1));
        AnsiConsole.MarkupLine($"[green]Found {duplicateGroups.Length} duplicate group(s)![/]");
        AnsiConsole.MarkupLine($"[yellow]Potential space savings: {FormatFileSize(totalWastedBytes)}[/]");
        AnsiConsole.WriteLine();

        const int pageSize = 10;
        int currentPage = 0;
        int totalPages = (int)Math.Ceiling(duplicateGroups.Length / (double)pageSize);
        bool exitRequested = false;

        while (!exitRequested)
        {
            int startIdx = currentPage * pageSize;
            var pageGroups = duplicateGroups.Skip(startIdx).Take(pageSize).ToArray();

            var labelMap = new Dictionary<string, string>();
            foreach (var g in pageGroups)
            {
                var files = g.ToList();
                var fileNames = files.Select(f => Path.GetFileName(f.FilePath)).Distinct().ToList();
                string prefix;
                if (fileNames.Count == 1)
                {
                    prefix = fileNames[0];
                }
                else
                {
                    var extensions = files.Select(f => Path.GetExtension(f.FilePath).ToLowerInvariant()).Distinct().ToList();
                    prefix = extensions.Count == 1 && !string.IsNullOrWhiteSpace(extensions[0])
                        ? $"{extensions[0]} file"
                        : "multiple files";
                }

                var sizeLabel = FormatFileSize(files[0].FileSize);
                var hashEscaped = g.Key.Replace("[", "[[").Replace("]", "]]");
                var label = $"{prefix} ({sizeLabel}, {g.Count()} files) [[{hashEscaped}]]";
                labelMap[label] = g.Key;
            }

            AnsiConsole.MarkupLine($"[grey]Page {currentPage + 1} of {totalPages}[/]");

            var prompt = new SelectionPrompt<string>()
                .Title("[bold yellow]Select a duplicate group, page, or Exit:[/]")
                .PageSize(pageSize + 3)
                .HighlightStyle("bold yellow");

            if (currentPage > 0)
                prompt.AddChoice("[blue]Prev Page[/]");
            foreach (var label in labelMap.Keys)
                prompt.AddChoice(label);
            if (currentPage < totalPages - 1)
                prompt.AddChoice("[blue]Next Page[/]");
            prompt.AddChoice("[red]Exit[/]");

            var selectedLabel = AnsiConsole.Prompt(prompt);

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

            if (!labelMap.TryGetValue(selectedLabel, out var selectedKey) || string.IsNullOrEmpty(selectedKey))
            {
                AnsiConsole.MarkupLine("[red]Could not determine hash from selection. Skipping group.[/]");
                continue;
            }

            var selectedGroup = duplicateGroups.First(g => g.Key == selectedKey);

            bool backRequested = false;
            while (!backRequested)
            {
                var table = new Table();
                table.Title = new TableTitle($"[bold yellow]SHA-256: {selectedGroup.Key}[/]");
                table.AddColumn(new TableColumn("[bold]Filename[/]").LeftAligned());
                table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());
                table.AddColumn(new TableColumn("[bold]Path[/]").LeftAligned());

                foreach (var file in selectedGroup)
                {
                    var fileName = Path.GetFileName(file.FilePath);
                    var directoryPath = Path.GetDirectoryName(file.FilePath) ?? string.Empty;
                    table.AddRow(fileName, FormatFileSize(file.FileSize), directoryPath);
                }

                AnsiConsole.Write(table);

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

                AnsiConsole.MarkupLine($"[blue]You selected:[/] {Markup.Escape(selectedFile)}");
                var directory = Path.GetDirectoryName(selectedFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    try
                    {
                        var absFile = Path.GetFullPath(selectedFile);
                        if (OperatingSystem.IsWindows())
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer",
                                Arguments = $"/select,\"{absFile}\"",
                                UseShellExecute = true
                            });
                        }
                        else if (OperatingSystem.IsMacOS())
                        {
                            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "open",
                                Arguments = $"-R \"{absFile}\"",
                                UseShellExecute = true
                            });
                            if (process == null)
                            {
                                AnsiConsole.MarkupLine($"[red]Failed to start Finder process for: {Markup.Escape(absFile)}[/]");
                            }
                        }
                        else if (OperatingSystem.IsLinux())
                        {
                            var absDir = Path.GetFullPath(directory);
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "xdg-open",
                                Arguments = $"'{absDir.Replace("'", "'\\''")}'",
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[yellow]Cannot open folder: unsupported OS.[/]");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to open folder: {Markup.Escape(ex.Message)}[/]");
                    }
                }
            }
        }

        return 0;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int suffixIndex = 0;
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        return $"{size:0.##} {suffixes[suffixIndex]}";
    }
}

/// <summary>
/// Converts human-readable file size strings (e.g., "500MB", "2GB") to byte counts.
/// </summary>
public sealed class FileSizeConverter : System.ComponentModel.TypeConverter
{
    private static readonly Dictionary<string, long> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "B", 1L },
        { "KB", 1024L },
        { "MB", 1024L * 1024 },
        { "GB", 1024L * 1024 * 1024 },
        { "TB", 1024L * 1024 * 1024 * 1024 },
    };

    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            text = text.Trim();

            // Try plain numeric (bytes)
            if (long.TryParse(text, out var plainBytes))
                return plainBytes;

            // Try suffix match
            foreach (var (suffix, multiplier) in Suffixes.OrderByDescending(s => s.Key.Length))
            {
                if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var numberPart = text[..^suffix.Length].Trim();
                    if (double.TryParse(numberPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                    {
                        return (long)(number * multiplier);
                    }
                }
            }

            throw new FormatException($"Cannot parse '{text}' as a file size. Use a number optionally followed by B, KB, MB, GB, or TB (e.g., '500MB', '2GB').");
        }

        return base.ConvertFrom(context, culture, value);
    }
}
