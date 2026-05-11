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
        string[] excludeFileNames,
        Func<List<(string Key, List<FileDetails> Files)>, List<(string Path, string Error)>, string>? onExport = null)
    {
        var duplicateGroups = new List<(string Key, List<FileDetails> Files)>();
        var skippedFiles = new List<(string Path, string Error)>();
        var groupsLock = new object();

        // Shared scan state (written by background thread, read by render loop)
        var scanProgress = 0d;
        var scanStatusText = "Discovering files...";
        var scanComplete = false;

        var scanThread = new Thread(() =>
        {
            scanner.ScanDirectoriesForDuplicateGroups(
                paths,
                minSizeBytes,
                comparers,
                excludePaths: excludePaths,
                excludeExtensions: excludeExtensions,
                excludeFileNames: excludeFileNames,
                onStatus: message =>
                {
                    scanStatusText = TruncateMiddle(message, Console.WindowWidth);
                },
                onProgress: (pct, message) =>
                {
                    if (pct < 0)
                    {
                        scanProgress = -1;
                        scanStatusText = TruncateMiddle(message, Console.WindowWidth);
                    }
                    else
                    {
                        scanProgress = pct;
                        scanStatusText = TruncateMiddle(message, Console.WindowWidth);
                    }
                },
                onFileSkipped: (path, error) =>
                {
                    skippedFiles.Add((path, error));
                },
                onDuplicateGroupFound: group =>
                {
                    group.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
                    var entry = (Key: group[0].Sha256Hash.ToHexString(), Files: group);
                    lock (groupsLock)
                    {
                        duplicateGroups.Add(entry);
                    }
                }
            );

            scanComplete = true;
        })
        {
            IsBackground = true,
        };

        scanThread.Start();
        RunViewer(duplicateGroups, skippedFiles, scanComplete: false,
            getScanComplete: () => scanComplete,
            getScanProgress: () => scanProgress,
            getScanStatus: () => scanStatusText,
            groupsLock: groupsLock,
            onExport: onExport);
    }

    private static void RunViewer(
        List<(string Key, List<FileDetails> Files)> duplicateGroups,
        List<(string Path, string Error)> skippedFiles,
        bool scanComplete,
        Func<bool>? getScanComplete = null,
        Func<double>? getScanProgress = null,
        Func<string>? getScanStatus = null,
        object? groupsLock = null,
        Func<List<(string Key, List<FileDetails> Files)>, List<(string Path, string Error)>, string>? onExport = null)
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
        int lastKnownGroupCount = 0;
        var refreshing = false;
        var refreshComplete = false;
        var refreshRemovedCount = 0;

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
        groupList.HighlightStyle = new Style(Color.DarkOrange);
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
            // Sync new groups from background thread into the list widget
            if (mode == ViewMode.Scanning || mode == ViewMode.GroupList)
            {
                int currentCount;
                if (groupsLock != null)
                {
                    lock (groupsLock)
                    {
                        currentCount = duplicateGroups.Count;
                    }
                }
                else
                {
                    currentCount = duplicateGroups.Count;
                }

                if (currentCount > lastKnownGroupCount)
                {
                    List<(string Key, List<FileDetails> Files)> newGroups;
                    if (groupsLock != null)
                    {
                        lock (groupsLock)
                        {
                            newGroups = duplicateGroups.Skip(lastKnownGroupCount).ToList();
                        }
                    }
                    else
                    {
                        newGroups = duplicateGroups.Skip(lastKnownGroupCount).ToList();
                    }

                    foreach (var g in newGroups)
                    {
                        var item = new DuplicateGroupListItem(g.Key, g.Files);
                        groupItems.Add(item);
                        groupList.Items.Add(item);
                    }

                    if (groupList.Items.Count > 0 && lastKnownGroupCount == 0)
                    {
                        groupList.SelectedIndex = 0;
                    }

                    lastKnownGroupCount = currentCount;
                }
            }

            // Check if scan just completed
            if (mode == ViewMode.Scanning && (getScanComplete?.Invoke() ?? true))
            {
                mode = ViewMode.GroupList;

                if (duplicateGroups.Count == 0)
                {
                    SetStatus(ref statusMessage, ref statusExpiry, "No duplicate files found.");
                }
            }

            // Check if refresh just completed
            if (refreshComplete)
            {
                refreshComplete = false;
                refreshing = false;

                if (selectedGroup != null && selectedGroup.Files.Count < 2)
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
                    if (selectedGroup != null)
                    {
                        fileTable = CreateFileTable(selectedGroup.Files);
                    }
                    SetStatus(ref statusMessage, ref statusExpiry, $"Refreshed \u2014 removed {refreshRemovedCount} file(s)");
                }

                if (duplicateGroups.Count == 0)
                {
                    running = false;
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
                        Paragraph.FromMarkup($"[darkorange]{EscapeMarkup(scanStatus)}[/]", null).Centered(),
                        summaryArea);
                }
                else
                {
                    var sortLabel = sortOrder == SortOrder.Size ? "size" : "path";
                    var skippedSuffix = skippedFiles.Count > 0 ? $" | {skippedFiles.Count} skipped" : "";
                    var totalWastedBytes = duplicateGroups.Sum(g => g.Files[0].FileSize * (g.Files.Count - 1));
                    var summary = $"[green]{duplicateGroups.Count} group(s) | Potential Savings: {FileHelpers.FormatFileSize(totalWastedBytes)} | Sort: {sortLabel}{skippedSuffix}[/]";
                    ctx.Render(
                        Paragraph.FromMarkup(summary, null).Centered(),
                        summaryArea);
                }

                // Content
                switch (mode)
                {
                    case ViewMode.Scanning:
                        var scanTitle = $"[bold darkorange]Duplicate Groups[/]  [dim]({groupList.Items.Count} found so far \u2014 scanning...)[/]";
                        if (groupList.Items.Count > 0)
                        {
                            ctx.Render(
                                new BoxWidget()
                                    .Border(Border.Rounded)
                                    .MarkupTitle(scanTitle)
                                    .Inner(groupList),
                                contentArea);
                        }
                        else
                        {
                            ctx.Render(
                                new BoxWidget()
                                    .Border(Border.Rounded)
                                    .MarkupTitle(scanTitle)
                                    .Inner(Paragraph.FromMarkup("[grey]Scanning for duplicates...[/]", null).Centered().AlignedMiddle()),
                                contentArea);
                        }
                        break;
                    case ViewMode.GroupList:
                        ctx.Render(
                            new BoxWidget()
                                .Border(Border.Rounded)
                                .MarkupTitle("[bold darkorange]Duplicate Groups[/]")
                                .Inner(groupList),
                            contentArea);
                        break;
                    case ViewMode.FileDetail when fileTable != null && selectedGroup != null:
                        var detailTitle = $"[bold darkorange]{EscapeMarkup(selectedGroup.DisplayName)} \u2014 {selectedGroup.Files.Count} files[/]";
                        ctx.Render(
                            new BoxWidget()
                                .Border(Border.Rounded)
                                .MarkupTitle(detailTitle)
                                .Inner(fileTable),
                            contentArea);
                        break;
                }

                // Progress bar (visible during scanning or refreshing)
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
                else if (refreshing)
                {
                    ctx.Render(spinner, progressArea);
                }

                // Status bar
                var help = mode switch
                {
                    ViewMode.Scanning => "[bold]\u2191\u2193[/] Navigate  [bold]Enter[/] View Files  [bold]Q[/] Quit",
                    ViewMode.GroupList => "[bold]\u2191\u2193[/] Navigate  [bold]Enter[/] View Files  [bold]S[/] Sort  [bold]E[/] Export  [bold]Q[/] Quit",
                    ViewMode.FileDetail => "[bold]\u2191\u2193[/] Navigate  [bold]Enter[/] Open Location  [bold]R[/] Refresh  [bold]E[/] Export  [bold]Esc[/] Back  [bold]Q[/] Quit",
                    _ => "",
                };
                if (statusMessage != null)
                {
                    help = $"[darkorange]{EscapeMarkup(statusMessage)}[/]  |  {help}";
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
                    if (mode == ViewMode.GroupList || mode == ViewMode.Scanning)
                    {
                        groupList.MoveUp();
                    }
                    else
                    {
                        fileTable?.MoveUp();
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (mode == ViewMode.GroupList || mode == ViewMode.Scanning)
                    {
                        groupList.MoveDown();
                    }
                    else
                    {
                        fileTable?.MoveDown();
                    }
                    break;
                case ConsoleKey.Enter:
                    if ((mode == ViewMode.GroupList || mode == ViewMode.Scanning) && groupList.SelectedItem != null)
                    {
                        selectedGroup = groupList.SelectedItem;
                        fileTable = CreateFileTable(selectedGroup.Files);
                        mode = ViewMode.FileDetail;
                    }
                    else if (mode == ViewMode.FileDetail && fileTable?.SelectedItem != null)
                    {
                        var filePath = fileTable.SelectedItem.File.FilePath;
                        if (File.Exists(filePath))
                        {
                            OpenFileLocation(filePath);
                        }
                        else
                        {
                            SetStatus(ref statusMessage, ref statusExpiry, "File no longer exists");
                        }
                    }
                    break;
                case ConsoleKey.Escape:
                case ConsoleKey.Backspace:
                    if (mode == ViewMode.FileDetail)
                    {
                        mode = (getScanComplete?.Invoke() ?? true) ? ViewMode.GroupList : ViewMode.Scanning;
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
                case ConsoleKey.E:
                    if ((mode == ViewMode.GroupList || mode == ViewMode.FileDetail) && onExport != null && duplicateGroups.Count > 0)
                    {
                        try
                        {
                            var exportedPath = onExport(duplicateGroups, skippedFiles);
                            SetStatus(ref statusMessage, ref statusExpiry, $"Exported to {exportedPath}");
                        }
                        catch (Exception ex)
                        {
                            SetStatus(ref statusMessage, ref statusExpiry, $"Export failed: {ex.Message}");
                        }
                    }
                    break;
                case ConsoleKey.R:
                    if (mode == ViewMode.FileDetail && selectedGroup != null && !refreshing)
                    {
                        refreshing = true;
                        var groupToRefresh = selectedGroup;
                        new Thread(() =>
                        {
                            refreshRemovedCount = RefreshGroup(groupToRefresh);
                            refreshComplete = true;
                        })
                        {
                            IsBackground = true,
                        }.Start();
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
            new TableColumn(new TextLine { Spans = [new TextSpan("Modified", null)] })
            {
                Width = ColumnWidth.Fixed(21),
            },
            new TableColumn(new TextLine { Spans = [new TextSpan("Path", null)] })
            {
                Width = ColumnWidth.Star(3),
            },
        ]);
        table.Rows.AddRange(rows);
        table.HighlightStyle = new Style(Color.DarkOrange);
        table.WrapAround = true;
        table.ShowHeader = true;
        table.SelectedIndex = 0;
        return table;
    }

    private static int RefreshGroup(DuplicateGroupListItem group)
    {
        var removedCount = 0;
        var originalHash = group.Key;

        // Phase 1: Remove files that no longer exist
        for (int i = group.Files.Count - 1; i >= 0; i--)
        {
            if (!File.Exists(group.Files[i].FilePath))
            {
                group.Files.RemoveAt(i);
                removedCount++;
            }
        }

        // If fewer than 2 files remain, no point in re-hashing
        if (group.Files.Count < 2)
        {
            return removedCount;
        }

        // Phase 2: Re-hash remaining files and remove any that no longer match
        for (int i = group.Files.Count - 1; i >= 0; i--)
        {
            try
            {
                var currentHash = FileHelpers.GetFileSha256(group.Files[i].FilePath).ToHexString();
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

    private static string TruncateMiddle(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        const string ellipsis = "...";
        int available = maxLength - ellipsis.Length;
        int keepStart = (available + 1) / 2;
        int keepEnd = available / 2;
        return text[..keepStart] + ellipsis + text[^keepEnd..];
    }
}
