using System.Text;
using Spectre.Tui;
using FileDeduplicator.Common;
using Color = Spectre.Console.Color;
using Layout = Spectre.Tui.Layout;
using Paragraph = Spectre.Tui.Paragraph;
using Style = Spectre.Console.Style;
using TableColumn = Spectre.Tui.TableColumn;

namespace FileDeduplicator.Tui;

public static class DuplicateResultsViewer
{
    private enum ViewMode
    {
        Scanning,
        GroupList,
        FileDetail,
    }

    private enum SortOrder
    {
        Size,
        Path,
    }

    public static void Show(
        List<(string Key, List<FileDetails> Files)> duplicateGroups,
        List<(string Path, string Error)> skippedFiles)
    {
        RunViewer(duplicateGroups, skippedFiles, scanComplete: true);
    }

    public static void ShowWithScan(
        FileScanner scanner,
        string[] paths,
        long minSizeBytes,
        IFileComparer[]? comparers,
        string[] excludePaths,
        string[] excludeExtensions,
        string[] excludeFileNames)
    {
        var duplicateGroups = new List<(string Key, List<FileDetails> Files)>();
        var skippedFiles = new List<(string Path, string Error)>();

        // Shared scan state (written by background thread, read by render loop)
        var scanProgress = 0d;
        var scanStatusText = "Discovering files...";
        var scanComplete = false;

        var scanThread = new Thread(() =>
        {
            var rawGroups = scanner.ScanDirectoriesForDuplicateGroups(
                paths,
                minSizeBytes,
                comparers,
                excludePaths: excludePaths,
                excludeExtensions: excludeExtensions,
                excludeFileNames: excludeFileNames,
                onStatus: message =>
                {
                    scanStatusText = message.Length > 80 ? message[..77] + "..." : message;
                },
                onProgress: (pct, message) =>
                {
                    if (pct < 0)
                    {
                        scanProgress = -1;
                        scanStatusText = message.Length > 80 ? message[..77] + "..." : message;
                    }
                    else
                    {
                        scanProgress = pct;
                        scanStatusText = $"Hashing: {ShortenPath(message, 60)}";
                    }
                },
                onFileSkipped: (path, error) =>
                {
                    skippedFiles.Add((path, error));
                }
            );

            var groups = (rawGroups ?? [])
                .Select(g =>
                {
                    g.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
                    return (Key: g[0].Sha256Hash.ToHexString(), Files: g);
                })
                .OrderByDescending(g => g.Files[0].FileSize)
                .ToList();

            duplicateGroups.AddRange(groups);
            scanComplete = true;
        })
        {
            IsBackground = true,
        };

        scanThread.Start();
        RunViewer(duplicateGroups, skippedFiles, scanComplete: false,
            getScanComplete: () => scanComplete,
            getScanProgress: () => scanProgress,
            getScanStatus: () => scanStatusText);
    }

    private static void RunViewer(
        List<(string Key, List<FileDetails> Files)> duplicateGroups,
        List<(string Path, string Error)> skippedFiles,
        bool scanComplete,
        Func<bool>? getScanComplete = null,
        Func<double>? getScanProgress = null,
        Func<string>? getScanStatus = null)
    {
        Console.OutputEncoding = Encoding.Unicode;
        using var terminal = Terminal.Create();
        var renderer = new Renderer(terminal);
        renderer.SetTargetFps(30);

        var running = true;
        var mode = scanComplete ? ViewMode.GroupList : ViewMode.Scanning;
        var sortOrder = SortOrder.Size;
        string? statusMessage = null;
        DateTime statusExpiry = DateTime.MinValue;

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            running = false;
        };

        // Build group list widget
        var groupItems = duplicateGroups
            .Select(g => new DuplicateGroupListItem(g.Key, g.Files))
            .ToList();

        var groupList = new ListWidget<DuplicateGroupListItem>();
        groupList.Items.AddRange(groupItems);
        groupList.HighlightStyle = new Style(Color.Yellow);
        groupList.WrapAround = true;
        if (groupItems.Count > 0)
        {
            groupList.SelectedIndex = 0;
        }

        // Spinner for scanning phase
        var spinner = new SpinnerWidget().Kind(SpinnerKind.Default);

        // Progress bar for scanning phase
        var scanProgressBar = new ProgressBarWidget()
            .Value(0).Max(100)
            .Foreground(ProgressBarBrush.Solid(new Style(Color.Green)))
            .Percentage()
            .Smooth();

        // File detail state
        TableWidget<FileDetailRow>? fileTable = null;
        DuplicateGroupListItem? selectedGroup = null;

        var layout = new Layout("Root").SplitRows(
            new Layout("Title").Size(1),
            new Layout("Summary").Size(1),
            new Layout("Content"),
            new Layout("Progress").Size(2),
            new Layout("Status").Size(1));

        while (running)
        {
            // Check if scan just completed
            if (mode == ViewMode.Scanning && (getScanComplete?.Invoke() ?? true))
            {
                // Rebuild group list from newly populated data
                groupItems.Clear();
                groupItems.AddRange(
                    duplicateGroups.Select(g => new DuplicateGroupListItem(g.Key, g.Files)));
                groupList.Items.Clear();
                groupList.Items.AddRange(groupItems);
                if (groupItems.Count > 0)
                {
                    groupList.SelectedIndex = 0;
                }
                mode = ViewMode.GroupList;

                if (duplicateGroups.Count == 0)
                {
                    SetStatus(ref statusMessage, ref statusExpiry, "No duplicate files found.");
                }
            }

            renderer.Draw((ctx, info) =>
            {
                if (statusMessage != null && DateTime.UtcNow > statusExpiry)
                {
                    statusMessage = null;
                }

                // Update spinner for scanning phase
                spinner.Update(info);
                scanProgressBar.Update(info);

                var titleArea = layout.GetArea(ctx, "Title");
                var summaryArea = layout.GetArea(ctx, "Summary");
                var contentArea = layout.GetArea(ctx, "Content");
                var progressArea = layout.GetArea(ctx, "Progress");
                var statusArea = layout.GetArea(ctx, "Status");

                // Title
                ctx.Render(
                    Paragraph.FromMarkup("[bold blue]File Deduplicator[/]", null).Centered(),
                    titleArea);

                // Summary
                if (mode == ViewMode.Scanning)
                {
                    var scanStatus = getScanStatus?.Invoke() ?? "Scanning...";
                    ctx.Render(
                        Paragraph.FromMarkup($"[yellow]{EscapeMarkup(scanStatus)}[/]", null).Centered(),
                        summaryArea);
                }
                else
                {
                    var sortLabel = sortOrder == SortOrder.Size ? "size" : "path";
                    var skippedSuffix = skippedFiles.Count > 0 ? $" | {skippedFiles.Count} skipped" : "";
                    var totalWastedBytes = duplicateGroups.Sum(g => g.Files[0].FileSize * (g.Files.Count - 1));
                    var summary = $"[green]{duplicateGroups.Count} group(s) | Potential Savings: {FormatFileSize(totalWastedBytes)} | Sort: {sortLabel}{skippedSuffix}[/]";
                    ctx.Render(
                        Paragraph.FromMarkup(summary, null).Centered(),
                        summaryArea);
                }

                // Content
                switch (mode)
                {
                    case ViewMode.Scanning:
                        ctx.Render(
                            new BoxWidget()
                                .Border(Border.Rounded)
                                .MarkupTitle("[bold yellow]Duplicate Groups[/]")
                                .Inner(Paragraph.FromMarkup("[grey]Scanning for duplicates...[/]", null).Centered().AlignedMiddle()),
                            contentArea);
                        break;
                    case ViewMode.GroupList:
                        ctx.Render(
                            new BoxWidget()
                                .Border(Border.Rounded)
                                .MarkupTitle("[bold yellow]Duplicate Groups[/]")
                                .Inner(groupList),
                            contentArea);
                        break;
                    case ViewMode.FileDetail when fileTable != null && selectedGroup != null:
                        var detailTitle = $"[bold yellow]{EscapeMarkup(selectedGroup.DisplayName)} \u2014 {selectedGroup.Files.Count} files[/]";
                        ctx.Render(
                            new BoxWidget()
                                .Border(Border.Rounded)
                                .MarkupTitle(detailTitle)
                                .Inner(fileTable),
                            contentArea);
                        break;
                }

                // Progress bar (visible during scanning)
                if (mode == ViewMode.Scanning)
                {
                    var pct = getScanProgress?.Invoke() ?? 0;
                    if (pct < 0)
                    {
                        // Indeterminate — show spinner in progress area
                        ctx.Render(spinner, progressArea);
                    }
                    else
                    {
                        scanProgressBar.Value = pct;
                        ctx.Render(scanProgressBar, progressArea);
                    }
                }

                // Status bar
                var help = mode switch
                {
                    ViewMode.Scanning => "[bold]Q[/] Quit",
                    ViewMode.GroupList => "[bold]\u2191\u2193[/] Navigate  [bold]Enter[/] View Files  [bold]S[/] Sort  [bold]Q[/] Quit",
                    ViewMode.FileDetail => "[bold]\u2191\u2193[/] Navigate  [bold]Enter[/] Open Location  [bold]R[/] Refresh  [bold]Esc[/] Back  [bold]Q[/] Quit",
                    _ => "",
                };
                if (statusMessage != null)
                {
                    help = $"[yellow]{EscapeMarkup(statusMessage)}[/]  |  {help}";
                }
                ctx.Render(
                    Paragraph.FromMarkup(help, null).Centered().Style(new Style(Color.Grey)),
                    statusArea);
            });

            if (!Console.KeyAvailable)
            {
                continue;
            }

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.Q:
                    running = false;
                    break;
                case ConsoleKey.UpArrow:
                    if (mode == ViewMode.GroupList)
                    {
                        groupList.MoveUp();
                    }
                    else
                    {
                        fileTable?.MoveUp();
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (mode == ViewMode.GroupList)
                    {
                        groupList.MoveDown();
                    }
                    else
                    {
                        fileTable?.MoveDown();
                    }
                    break;
                case ConsoleKey.Enter:
                    if (mode == ViewMode.GroupList && groupList.SelectedItem != null)
                    {
                        selectedGroup = groupList.SelectedItem;
                        fileTable = CreateFileTable(selectedGroup.Files);
                        mode = ViewMode.FileDetail;
                    }
                    else if (mode == ViewMode.FileDetail && fileTable?.SelectedItem != null)
                    {
                        OpenFileLocation(fileTable.SelectedItem.File.FilePath);
                    }
                    break;
                case ConsoleKey.Escape:
                case ConsoleKey.Backspace:
                    if (mode == ViewMode.FileDetail)
                    {
                        mode = ViewMode.GroupList;
                        fileTable = null;
                        selectedGroup = null;
                    }
                    break;
                case ConsoleKey.S:
                    if (mode == ViewMode.GroupList)
                    {
                        sortOrder = sortOrder == SortOrder.Size ? SortOrder.Path : SortOrder.Size;
                        RebuildGroupList(groupList, groupItems, duplicateGroups, sortOrder);
                        SetStatus(ref statusMessage, ref statusExpiry, $"Sorted by {(sortOrder == SortOrder.Size ? "size" : "path")}");
                    }
                    break;
                case ConsoleKey.R:
                    if (mode == ViewMode.FileDetail && selectedGroup != null)
                    {
                        var removedCount = RefreshGroup(selectedGroup);
                        if (selectedGroup.Files.Count < 2)
                        {
                            groupItems.Remove(selectedGroup);
                            groupList.Items.Remove(selectedGroup);
                            duplicateGroups.RemoveAll(g => g.Key == selectedGroup.Key);

                            mode = ViewMode.GroupList;
                            fileTable = null;
                            selectedGroup = null;
                            SetStatus(ref statusMessage, ref statusExpiry, "Group removed (no longer has duplicates)");
                        }
                        else
                        {
                            fileTable = CreateFileTable(selectedGroup.Files);
                            SetStatus(ref statusMessage, ref statusExpiry, $"Refreshed \u2014 removed {removedCount} file(s)");
                        }

                        if (duplicateGroups.Count == 0)
                        {
                            running = false;
                        }
                    }
                    break;
            }
        }
    }

    private static void SetStatus(ref string? statusMessage, ref DateTime statusExpiry, string message)
    {
        statusMessage = message;
        statusExpiry = DateTime.UtcNow.AddSeconds(3);
    }

    private static void RebuildGroupList(
        ListWidget<DuplicateGroupListItem> groupList,
        List<DuplicateGroupListItem> groupItems,
        List<(string Key, List<FileDetails> Files)> duplicateGroups,
        SortOrder sortOrder)
    {
        var sorted = sortOrder switch
        {
            SortOrder.Path => groupItems.OrderBy(g => g.Files[0].FilePath, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => groupItems.OrderByDescending(g => g.Files[0].FileSize).ToList(),
        };

        groupList.Items.Clear();
        groupList.Items.AddRange(sorted);
        groupList.SelectedIndex = 0;
    }

    private static TableWidget<FileDetailRow> CreateFileTable(List<FileDetails> files)
    {
        var rows = files.Select(f => new FileDetailRow(f)).ToList();
        var table = new TableWidget<FileDetailRow>();
        table.Columns.AddRange([
            new TableColumn(new TextLine { Spans = [new TextSpan("Filename", null)] })
            {
                Width = ColumnWidth.Star(2),
            },
            new TableColumn(new TextLine { Spans = [new TextSpan("Size", null)] })
            {
                Width = ColumnWidth.Fixed(15),
                Alignment = Spectre.Tui.Justify.Right,
            },
            new TableColumn(new TextLine { Spans = [new TextSpan("Path", null)] })
            {
                Width = ColumnWidth.Star(3),
            },
        ]);
        table.Rows.AddRange(rows);
        table.HighlightStyle = new Style(Color.Yellow);
        table.WrapAround = true;
        table.ShowHeader = true;
        table.SelectedIndex = 0;
        return table;
    }

    private static int RefreshGroup(DuplicateGroupListItem group)
    {
        var removedCount = 0;
        var originalHash = group.Key;
        for (int i = group.Files.Count - 1; i >= 0; i--)
        {
            var file = group.Files[i];
            if (!File.Exists(file.FilePath))
            {
                group.Files.RemoveAt(i);
                removedCount++;
                continue;
            }
            try
            {
                var currentHash = FileHelpers.GetFileSha256(file.FilePath).ToHexString();
                if (currentHash != originalHash)
                {
                    group.Files.RemoveAt(i);
                    removedCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                group.Files.RemoveAt(i);
                removedCount++;
            }
        }
        return removedCount;
    }

    private static void OpenFileLocation(string filePath)
    {
        try
        {
            var absFile = Path.GetFullPath(filePath);
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer",
                    Arguments = $"/select,\"{absFile}\"",
                    UseShellExecute = true,
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-R \"{absFile}\"",
                    UseShellExecute = true,
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                var absDir = Path.GetFullPath(Path.GetDirectoryName(filePath) ?? ".");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"'{absDir.Replace("'", "'\\''")}'",
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // In TUI mode, file location opening is best-effort
        }
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

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
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
        var remaining = maxLength - fileName.Length - 5;
        if (remaining <= 0)
        {
            return "..." + Path.DirectorySeparatorChar + fileName;
        }
        var dir = Path.GetDirectoryName(filePath) ?? "";
        return dir[..Math.Min(remaining, dir.Length)] + "/..." + Path.DirectorySeparatorChar + fileName;
    }
}
