using System.ComponentModel;
using FileDeduplicator.Common;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator.Commands;

public enum GroupSortOrder
{
    Path,
    Size,
}

[Description("Find duplicate files, optionally filtering by minimum file size.")]
public sealed class FindDuplicatesCommand : Command<FindDuplicatesCommand.Settings>
{
    private const long DefaultMinSizeBytes = 0L;

    public sealed class Settings : CommandSettings
    {
        // NOTE: [developers] When running via dotnet tool for multiple directories, the --path option will avoid errors about multiple projects being called.
        [CommandOption("-p|--path <PATH>")]
        [Description("One or more paths to scan for files (defaults to the current directory if not provided). Specify multiple times: -d /path1 -d /path2")]
        public string[]? Paths { get; set; }

        [CommandOption("-s|--min-size <SIZE>")]
        [Description("Minimum file size in bytes to consider (defaults to 0, no filter). Supports suffixes: KB, MB, GB, TB (e.g., '500MB', '2GB').")]
        [TypeConverter(typeof(FileSizeConverter))]
        [DefaultValue(DefaultMinSizeBytes)]
        public long MinSizeBytes { get; set; }

        [CommandOption("-m|--allow-metadata-diffs")]
        [Description("Allow metadata differences when comparing files (e.g., ignore ID3 tags, EXIF data). Files are compared by content only.")]
        [DefaultValue(false)]
        public bool AllowMetadataDiffs { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var paths = settings.Paths is { Length: > 0 }
            ? settings.Paths.Select(p => string.IsNullOrWhiteSpace(p) ? Environment.CurrentDirectory : p).ToArray()
            : [Environment.CurrentDirectory];

        foreach (var path in paths)
        {
            if (!Directory.Exists(path))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Directory not found: {Markup.Escape(path)}");
                return 1;
            }
        }

        AnsiConsole.MarkupLine($"Scanning for duplicate files in: [blue]{Markup.Escape(string.Join(", ", paths))}[/]");
        if (settings.MinSizeBytes > 0)
        {
            AnsiConsole.MarkupLine($"Minimum file size: [blue]{FormatFileSize(settings.MinSizeBytes)}[/]");
        }
        if (settings.AllowMetadataDiffs)
        {
            AnsiConsole.MarkupLine("[blue]Allowing metadata differences[/]");
        }
        AnsiConsole.WriteLine();

        var scanner = new FileScanner();
        IFileComparer[]? comparers = settings.AllowMetadataDiffs
            ? [new AudioFileComparer(), new ImageFileComparer(), new BinaryFileComparer { IgnoreMetadata = true }]
            : null;

        List<List<FileDetails>>? rawGroups = null;
        var skippedFiles = new List<(string Path, string Error)>();

        AnsiConsole.Progress()
            .AutoClear(true)
            .Columns(
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            )
            .Start(ctx =>
            {
                var task = ctx.AddTask("Discovering files...", autoStart: true, maxValue: 100);
                task.IsIndeterminate = true;

                rawGroups = scanner.ScanDirectoriesForDuplicateGroups(
                    paths,
                    settings.MinSizeBytes,
                    comparers,
                    onStatus: message =>
                    {
                        task.Description = Markup.Escape(message);
                    },
                    onProgress: (pct, message) =>
                    {
                        if (pct < 0)
                        {
                            // Discovery phase (indeterminate) — message is a status string, not a path
                            task.IsIndeterminate = true;
                            task.Description = Markup.Escape(message.Length > 80 ? message[..77] + "..." : message);
                        }
                        else
                        {
                            // Hashing phase (determinate) — message is a file path
                            task.IsIndeterminate = false;
                            task.Value = pct;
                            task.Description = Markup.Escape($"Hashing: {ShortenPath(message, 60)}");
                        }
                    },
                    onFileSkipped: (path, error) =>
                    {
                        skippedFiles.Add((path, error));
                    }
                );
            });

        var duplicateGroups = (rawGroups ?? [])
        .Select(g =>
        {
            g.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
            return (Key: g[0].Sha256Hash.ToHexString(), Files: g);
        })
        .OrderByDescending(g => g.Files[0].FileSize)
        .ToList();

        var sortOrder = GroupSortOrder.Size;

        if (duplicateGroups.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No duplicate files found.[/]");
            if (skippedFiles.Count > 0)
            {
                AnsiConsole.MarkupLine($"[yellow]{skippedFiles.Count} file(s) could not be scanned.[/]");
                ShowSkippedFiles(skippedFiles);
            }
            return 0;
        }

        var totalWastedBytes = duplicateGroups.Sum(g => g.Files[0].FileSize * (g.Files.Count - 1));
        AnsiConsole.MarkupLine($"[green]Found {duplicateGroups.Count} duplicate group(s)![/]");
        AnsiConsole.MarkupLine($"[yellow]Potential space savings: {FormatFileSize(totalWastedBytes)}[/]");
        if (skippedFiles.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{skippedFiles.Count} file(s) could not be scanned.[/]");
        }
        AnsiConsole.WriteLine();

        const int pageSize = 10;
        int currentPage = 0;
        bool exitRequested = false;

        while (!exitRequested)
        {
            var sortedGroups = sortOrder switch
            {
                GroupSortOrder.Path => duplicateGroups.OrderBy(g => g.Files[0].FilePath, StringComparer.OrdinalIgnoreCase).ToList(),
                GroupSortOrder.Size or _ => duplicateGroups.OrderByDescending(g => g.Files[0].FileSize).ToList(),
            };

            int totalPages = (int)Math.Ceiling(sortedGroups.Count / (double)pageSize);
            if (currentPage >= totalPages)
            {
                currentPage = Math.Max(0, totalPages - 1);
            }
            int startIdx = currentPage * pageSize;
            var pageGroups = sortedGroups.Skip(startIdx).Take(pageSize).ToArray();

            var labelMap = new Dictionary<string, string>();
            foreach (var g in pageGroups)
            {
                var files = g.Files;
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
                var prefixEscaped = prefix.Replace("[", "[[").Replace("]", "]]");
                var hashShort = ShortenHash(g.Key);
                var hashEscaped = hashShort.Replace("[", "[[").Replace("]", "]]");
                var label = $"{prefixEscaped} ({sizeLabel}, {g.Files.Count} files) [[{hashEscaped}]]";
                labelMap[label] = g.Key;
            }

            AnsiConsole.MarkupLine($"[grey]Page {currentPage + 1} of {totalPages} (sorted by {sortOrder.ToString().ToLowerInvariant()})[/]");

            var prompt = new SelectionPrompt<string>()
                .Title("[bold yellow]Select a duplicate group, page, or Exit:[/]")
                .PageSize(pageSize + 3)
                .HighlightStyle("bold yellow");

            if (currentPage > 0)
            {
                prompt.AddChoice("[blue]Prev Page[/]");
            }
            foreach (var label in labelMap.Keys)
            {
                prompt.AddChoice(label);
            }
            if (currentPage < totalPages - 1)
            {
                prompt.AddChoice("[blue]Next Page[/]");
            }
            if (skippedFiles.Count > 0)
            {
                prompt.AddChoice($"[yellow]Skipped Files ({skippedFiles.Count})[/]");
            }
            var nextSortOrder = sortOrder switch
            {
                GroupSortOrder.Path => GroupSortOrder.Size,
                GroupSortOrder.Size or _ => GroupSortOrder.Path,
            };
            var sortLabel = $"[blue]Sort by {nextSortOrder}[/]";
            prompt.AddChoice(sortLabel);
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
            if (selectedLabel == $"[yellow]Skipped Files ({skippedFiles.Count})[/]")
            {
                ShowSkippedFiles(skippedFiles);
                continue;
            }
            if (selectedLabel == sortLabel)
            {
                sortOrder = nextSortOrder;
                currentPage = 0;
                continue;
            }

            if (!labelMap.TryGetValue(selectedLabel, out var selectedKey) || string.IsNullOrEmpty(selectedKey))
            {
                AnsiConsole.MarkupLine("[red]Could not determine hash from selection. Skipping group.[/]");
                continue;
            }

            var selectedGroup = duplicateGroups.First(g => g.Key == selectedKey);

            bool backRequested = false;
            bool showTable = true;
            while (!backRequested)
            {
                if (showTable)
                {
                    var table = new Table();
                    table.Title = new TableTitle($"[bold yellow]SHA-256: {ShortenHash(selectedGroup.Key)}[/]");
                    table.AddColumn(new TableColumn("[bold]Filename[/]").LeftAligned());
                    table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());
                    table.AddColumn(new TableColumn("[bold]Path[/]").LeftAligned());

                    foreach (var file in selectedGroup.Files)
                    {
                        var fileName = Markup.Escape(Path.GetFileName(file.FilePath));
                        var directoryPath = Markup.Escape(Path.GetDirectoryName(file.FilePath) ?? string.Empty);
                        table.AddRow(fileName, FormatFileSize(file.FileSize), directoryPath);
                    }

                    AnsiConsole.Write(table);
                    showTable = false;
                }

                var escapedPathMap = selectedGroup.Files.ToDictionary(
                    f => Markup.Escape(f.FilePath),
                    f => f.FilePath
                );
                var fileChoices = escapedPathMap.Keys.ToList();
                fileChoices.Add("[green]Refresh[/]");
                fileChoices.Add("[yellow]Back[/]");
                var selectedFileLabel = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[bold]Select a file to open location, Refresh, or Back:[/]")
                        .PageSize(12)
                        .AddChoices(fileChoices)
                );

                if (selectedFileLabel == "[yellow]Back[/]")
                {
                    backRequested = true;
                    continue;
                }

                if (selectedFileLabel == "[green]Refresh[/]")
                {
                    var removedCount = 0;
                    var originalHash = selectedGroup.Key;
                    for (int i = selectedGroup.Files.Count - 1; i >= 0; i--)
                    {
                        var file = selectedGroup.Files[i];
                        if (!File.Exists(file.FilePath))
                        {
                            AnsiConsole.MarkupLine($"[yellow]Removed (missing):[/] {Markup.Escape(file.FilePath)}");
                            selectedGroup.Files.RemoveAt(i);
                            removedCount++;
                            continue;
                        }
                        try
                        {
                            var currentHash = FileHelpers.GetFileSha256(file.FilePath).ToHexString();
                            if (currentHash != originalHash)
                            {
                                AnsiConsole.MarkupLine($"[yellow]Removed (hash changed):[/] {Markup.Escape(file.FilePath)}");
                                selectedGroup.Files.RemoveAt(i);
                                removedCount++;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            AnsiConsole.MarkupLine($"[yellow]Removed (inaccessible):[/] {Markup.Escape(file.FilePath)}");
                            selectedGroup.Files.RemoveAt(i);
                            removedCount++;
                        }
                    }

                    if (removedCount == 0)
                    {
                        AnsiConsole.MarkupLine("[green]All files still match.[/]");
                    }
                    else if (selectedGroup.Files.Count < 2)
                    {
                        AnsiConsole.MarkupLine("[yellow]Group no longer has duplicates. Removing group.[/]");
                        duplicateGroups.Remove(selectedGroup);
                        if (duplicateGroups.Count == 0)
                        {
                            AnsiConsole.MarkupLine("[green]No duplicate groups remaining.[/]");
                            return 0;
                        }
                        backRequested = true;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Removed {removedCount} file(s). {selectedGroup.Files.Count} remaining.[/]");
                    }
                    AnsiConsole.WriteLine();
                    showTable = true;
                    continue;
                }

                var selectedFile = escapedPathMap[selectedFileLabel];

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

    private static string ShortenPath(string filePath, int maxLength)
    {
        if (string.IsNullOrEmpty(filePath) || filePath.Length <= maxLength)
        {
            return filePath;
        }
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName) || fileName.Length >= maxLength - 4)
        {
            return "..." + filePath[^Math.Min(maxLength - 3, filePath.Length)..];
        }
        var remaining = maxLength - fileName.Length - 5; // 5 = "/..." + separator
        if (remaining <= 0)
        {
            return "..." + Path.DirectorySeparatorChar + fileName;
        }
        var dir = Path.GetDirectoryName(filePath) ?? "";
        return dir[..Math.Min(remaining, dir.Length)] + "/..." + Path.DirectorySeparatorChar + fileName;
    }

    private static void ShowSkippedFiles(List<(string Path, string Error)> skippedFiles)
    {
        var table = new Table();
        table.Title = new TableTitle($"[bold yellow]Skipped Files ({skippedFiles.Count})[/]");
        table.AddColumn(new TableColumn("[bold]Filename[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]Path[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]Error[/]").LeftAligned());

        foreach (var (path, error) in skippedFiles)
        {
            var fileName = Path.GetFileName(path);
            var directoryPath = Path.GetDirectoryName(path) ?? string.Empty;
            table.AddRow(
                Markup.Escape(fileName),
                Markup.Escape(directoryPath),
                Markup.Escape(error));
        }

        AnsiConsole.Write(table);

        var escapedPathMap = skippedFiles.ToDictionary(
            f => Markup.Escape(f.Path),
            f => f.Path
        );
        var fileChoices = escapedPathMap.Keys.ToList();
        fileChoices.Add("[yellow]Back[/]");
        var selectedFileLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Select a file to open its location in Finder/Explorer, or Back:[/]")
                .PageSize(15)
                .AddChoices(fileChoices)
        );

        if (selectedFileLabel == "[yellow]Back[/]")
        {
            return;
        }

        var selectedFile = escapedPathMap[selectedFileLabel];

        AnsiConsole.MarkupLine($"[blue]Opening location:[/] {Markup.Escape(selectedFile)}");
        var directory = Path.GetDirectoryName(selectedFile);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                var absPath = Path.GetFullPath(directory);
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer",
                        Arguments = $"\"{absPath}\"",
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"\"{absPath}\"",
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsLinux())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"'{absPath.Replace("'", "'\\''")}'",
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

    private static string ShortenHash(string hash)
    {
        if (hash.Length <= 15)
        {
            return hash;
        }
        return $"{hash[..6]}\u2026{hash[^6..]}";
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
