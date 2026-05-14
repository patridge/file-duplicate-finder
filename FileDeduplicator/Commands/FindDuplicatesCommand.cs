using System.ComponentModel;
using FileDeduplicator.Common;
using FileDeduplicator.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator.Commands;

[Description("Find duplicate files, optionally filtering by minimum file size.")]
public sealed class FindDuplicatesCommand : Command<FindDuplicatesCommand.Settings>
{
    private const long DefaultMinSizeBytes = 0L;

    private static readonly string[] SystemGeneratedFiles =
    [
        ".DS_Store",           // macOS: Finder folder metadata (view options, icon positions, background)
        "Thumbs.db",           // Windows: Explorer thumbnail cache for image/video previews
        "desktop.ini",         // Windows: folder display settings (custom icons, localized names)
        ".thumbs",             // Linux (Nautilus/GNOME): thumbnail cache directory marker
        ".Spotlight-V100",     // macOS: Spotlight search index data for a volume
        ".Trashes",            // macOS: per-volume trash folder (used on external/network drives)
        ".fseventsd",          // macOS: file system event log used by FSEvents for change tracking
        ".TemporaryItems",     // macOS: temporary files created during Finder copy/move operations
    ];

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

        [CommandOption("-x|--exclude <PATH>")]
        [Description("One or more paths to exclude from scanning. Specify multiple times: --exclude /path1/skip --exclude /path2/skip")]
        public string[]? ExcludePaths { get; set; }

        [CommandOption("--exclude-extension <EXT>")]
        [Description("One or more file extensions to exclude (e.g., --exclude-extension .log --exclude-extension .tmp).")]
        public string[]? ExcludeExtensions { get; set; }

        [CommandOption("--exclude-filename <FILENAME>")]
        [Description("One or more filenames to exclude (e.g., --exclude-filename .DS_Store --exclude-filename Thumbs.db).")]
        public string[]? ExcludeFileNames { get; set; }

        [CommandOption("--exclude-common-system-files")]
        [Description("Exclude common system-generated files (e.g., .DS_Store, Thumbs.db, desktop.ini).")]
        [DefaultValue(false)]
        public bool ExcludeSystemFiles { get; set; }

        [CommandOption("--use-cache")]
        [Description("Cache file hashes in app data to speed up future scans. Stale entries are automatically refreshed when file dates change.")]
        [DefaultValue(false)]
        public bool UseCache { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
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

        AnsiConsole.MarkupLine("Scanning for duplicate files:");
        foreach (var path in paths)
        {
            AnsiConsole.MarkupLine($"  [blue]+[/] {Markup.Escape(path)}");
        }
        var excludePaths = settings.ExcludePaths is { Length: > 0 }
            ? settings.ExcludePaths.Select(p => Path.GetFullPath(p)).ToArray()
            : Array.Empty<string>();
        foreach (var excludePath in excludePaths)
        {
            AnsiConsole.MarkupLine($"  [yellow]-[/] {Markup.Escape(excludePath)}");
        }

        var excludeExtensions = (settings.ExcludeExtensions ?? [])
            .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
            .ToArray();

        var excludeFileNames = settings.ExcludeSystemFiles
            ? (settings.ExcludeFileNames ?? []).Concat(SystemGeneratedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : (settings.ExcludeFileNames ?? []).ToArray();

        if (excludeExtensions.Length > 0)
        {
            AnsiConsole.MarkupLine($"Excluding extensions: [yellow]{Markup.Escape(string.Join(", ", excludeExtensions))}[/]");
        }
        if (excludeFileNames.Length > 0)
        {
            AnsiConsole.MarkupLine($"Excluding filenames: [yellow]{Markup.Escape(string.Join(", ", excludeFileNames))}[/]");
        }
        if (settings.MinSizeBytes > 0)
        {
            AnsiConsole.MarkupLine($"Minimum file size: [blue]{FileHelpers.FormatFileSize(settings.MinSizeBytes)}[/]");
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

        FileHashCache? cache = null;
        if (settings.UseCache)
        {
            cache = new FileHashCache();
            scanner.HashCache = cache;
            AnsiConsole.MarkupLine($"[blue]Using hash cache[/] ({Markup.Escape(FileHashCache.GetDefaultCacheFilePath())})");
            AnsiConsole.WriteLine();
        }

        DuplicateResultsViewer.ShowWithScan(
            scanner,
            paths,
            settings.MinSizeBytes,
            comparers,
            excludePaths,
            excludeExtensions,
            excludeFileNames,
            onExport: (duplicateGroups, skippedFiles) =>
            {
                var report = ScanReport.Create(
                    paths,
                    settings.MinSizeBytes,
                    settings.AllowMetadataDiffs,
                    settings.UseCache,
                    excludePaths,
                    excludeExtensions,
                    excludeFileNames,
                    duplicateGroups,
                    skippedFiles);
                var exportPath = Path.GetFullPath(
                    $"scan-report-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(exportPath, report.ToJson());
                return exportPath;
            });

        cache?.Save();

        return 0;
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
            {
                return plainBytes;
            }

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
