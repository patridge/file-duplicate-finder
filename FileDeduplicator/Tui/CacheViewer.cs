using System.Text;
using Spectre.Tui;
using FileDeduplicator.Common;
using Color = Spectre.Console.Color;
using Layout = Spectre.Tui.Layout;
using Paragraph = Spectre.Tui.Paragraph;
using Style = Spectre.Console.Style;

namespace FileDeduplicator.Tui;

public static class CacheViewer
{
    public static void Show(FileHashCache cache)
    {
        Console.OutputEncoding = Encoding.Unicode;
        using var terminal = Terminal.Create();
        var renderer = new Renderer(terminal);
        renderer.SetTargetFps(30);

        var running = true;
        string? statusMessage = null;
        DateTime statusExpiry = DateTime.MinValue;

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            running = false;
        };

        var treeList = new ListWidget<CacheTreeItem>();
        treeList.HighlightStyle = new Style(Color.DarkOrange);
        treeList.WrapAround = true;

        var allItems = BuildTreeItems(cache);
        RebuildVisibleList(treeList, allItems);

        var layout = new Layout("Root").SplitRows(
            new Layout("Title").Size(1),
            new Layout("Summary").Size(1),
            new Layout("Content"),
            new Layout("Status").Size(1));

        while (running)
        {
            renderer.Draw((ctx, info) =>
            {
                if (statusMessage != null && DateTime.UtcNow > statusExpiry)
                {
                    statusMessage = null;
                }

                var titleArea = layout.GetArea(ctx, "Title");
                var summaryArea = layout.GetArea(ctx, "Summary");
                var contentArea = layout.GetArea(ctx, "Content");
                var statusArea = layout.GetArea(ctx, "Status");

                // Title
                ctx.Render(
                    Paragraph.FromMarkup("[bold blue]Hash Cache Viewer[/]", null).Centered(),
                    titleArea);

                // Summary
                var totalEntries = allItems.Count(i => !i.IsDirectory);
                var staleEntries = allItems.Count(i => !i.IsDirectory && i.IsStale);
                var selectedCount = allItems.Count(i => i.IsSelected);
                var selectedInfo = selectedCount > 0 ? $" | [green]{selectedCount} selected[/]" : "";
                var staleInfo = staleEntries > 0 ? $" | [red]{staleEntries} stale[/]" : "";
                var summaryText = $"[green]{totalEntries} cached file(s){staleInfo}{selectedInfo}[/]";
                ctx.Render(
                    Paragraph.FromMarkup(summaryText, null).Centered(),
                    summaryArea);

                // Content
                if (treeList.Items.Count == 0)
                {
                    ctx.Render(
                        new BoxWidget()
                            .Border(Border.Rounded)
                            .MarkupTitle("[bold darkorange]Cache[/]")
                            .Inner(Paragraph.FromMarkup("[grey]Cache is empty.[/]", null).Centered().AlignedMiddle()),
                        contentArea);
                }
                else
                {
                    ctx.Render(
                        new BoxWidget()
                            .Border(Border.Rounded)
                            .MarkupTitle("[bold darkorange]Cache[/]")
                            .Inner(treeList),
                        contentArea);
                }

                // Status bar
                var help = "[bold]\u2191\u2193[/] Navigate  [bold]Enter[/] Expand/Collapse  " +
                           "[bold]Space[/] Select  [bold]D[/] Clear Selected  " +
                           "[bold]R[/] Refresh Selected  [bold]A[/] Select All  " +
                           "[bold]N[/] Select None  [bold]T[/] Select Stale  [bold]Q[/] Quit";
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
                    treeList.MoveUp();
                    break;
                case ConsoleKey.DownArrow:
                    treeList.MoveDown();
                    break;
                case ConsoleKey.Enter:
                    if (treeList.SelectedItem is { IsDirectory: true } dirItem)
                    {
                        dirItem.IsExpanded = !dirItem.IsExpanded;
                        RebuildVisibleList(treeList, allItems);
                    }
                    break;
                case ConsoleKey.Spacebar:
                    if (treeList.SelectedItem != null)
                    {
                        ToggleSelection(treeList.SelectedItem, allItems);
                    }
                    break;
                case ConsoleKey.D:
                {
                    var selected = allItems.Where(i => i.IsSelected && !i.IsDirectory).ToList();
                    if (selected.Count == 0)
                    {
                        // If nothing selected, clear the highlighted item
                        if (treeList.SelectedItem is { IsDirectory: false } fileItem)
                        {
                            cache.Remove(fileItem.FullPath);
                            allItems = BuildTreeItems(cache);
                            RebuildVisibleList(treeList, allItems);
                            SetStatus(ref statusMessage, ref statusExpiry, "Cleared 1 entry.");
                            cache.Save();
                        }
                        else if (treeList.SelectedItem is { IsDirectory: true } dirToClear)
                        {
                            var removed = cache.RemoveEntriesUnderPath(dirToClear.FullPath);
                            allItems = BuildTreeItems(cache);
                            RebuildVisibleList(treeList, allItems);
                            SetStatus(ref statusMessage, ref statusExpiry, $"Cleared {removed} entry/entries under {Path.GetFileName(dirToClear.FullPath.TrimEnd(Path.DirectorySeparatorChar))}.");
                            cache.Save();
                        }
                    }
                    else
                    {
                        foreach (var item in selected)
                        {
                            cache.Remove(item.FullPath);
                        }
                        allItems = BuildTreeItems(cache);
                        RebuildVisibleList(treeList, allItems);
                        SetStatus(ref statusMessage, ref statusExpiry, $"Cleared {selected.Count} entry/entries.");
                        cache.Save();
                    }

                    if (allItems.Count == 0)
                    {
                        SetStatus(ref statusMessage, ref statusExpiry, "Cache is now empty.");
                    }
                    break;
                }
                case ConsoleKey.R:
                {
                    var selected = allItems.Where(i => i.IsSelected && !i.IsDirectory).ToList();
                    if (selected.Count == 0)
                    {
                        // Refresh highlighted item
                        if (treeList.SelectedItem is { IsDirectory: false } fileItem && fileItem.IsStale)
                        {
                            cache.GetOrComputeHash(fileItem.FullPath);
                            allItems = BuildTreeItems(cache);
                            RebuildVisibleList(treeList, allItems);
                            SetStatus(ref statusMessage, ref statusExpiry, "Refreshed 1 entry.");
                            cache.Save();
                        }
                        else if (treeList.SelectedItem is { IsDirectory: true } dirToRefresh)
                        {
                            int refreshed = 0;
                            foreach (var item in allItems.Where(i => !i.IsDirectory && i.IsStale &&
                                i.FullPath.StartsWith(dirToRefresh.FullPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                try
                                {
                                    if (File.Exists(item.FullPath))
                                    {
                                        cache.GetOrComputeHash(item.FullPath);
                                    }
                                    else
                                    {
                                        cache.Remove(item.FullPath);
                                    }
                                    refreshed++;
                                }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                {
                                    cache.Remove(item.FullPath);
                                    refreshed++;
                                }
                            }
                            allItems = BuildTreeItems(cache);
                            RebuildVisibleList(treeList, allItems);
                            SetStatus(ref statusMessage, ref statusExpiry, $"Refreshed {refreshed} entry/entries.");
                            cache.Save();
                        }
                        else
                        {
                            SetStatus(ref statusMessage, ref statusExpiry, "Nothing to refresh (entry is current).");
                        }
                    }
                    else
                    {
                        int refreshed = 0;
                        foreach (var item in selected.Where(i => i.IsStale))
                        {
                            try
                            {
                                if (File.Exists(item.FullPath))
                                {
                                    cache.GetOrComputeHash(item.FullPath);
                                }
                                else
                                {
                                    cache.Remove(item.FullPath);
                                }
                                refreshed++;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                cache.Remove(item.FullPath);
                                refreshed++;
                            }
                        }
                        allItems = BuildTreeItems(cache);
                        RebuildVisibleList(treeList, allItems);
                        SetStatus(ref statusMessage, ref statusExpiry, $"Refreshed {refreshed} entry/entries.");
                        cache.Save();
                    }
                    break;
                }
                case ConsoleKey.A:
                    foreach (var item in allItems)
                    {
                        item.IsSelected = !item.IsDirectory;
                    }
                    SetStatus(ref statusMessage, ref statusExpiry, $"Selected all {allItems.Count(i => i.IsSelected)} entries.");
                    break;
                case ConsoleKey.N:
                    foreach (var item in allItems)
                    {
                        item.IsSelected = false;
                    }
                    SetStatus(ref statusMessage, ref statusExpiry, "Selection cleared.");
                    break;
                case ConsoleKey.T:
                    foreach (var item in allItems)
                    {
                        item.IsSelected = !item.IsDirectory && item.IsStale;
                    }
                    var staleSelected = allItems.Count(i => i.IsSelected);
                    SetStatus(ref statusMessage, ref statusExpiry, $"Selected {staleSelected} stale entry/entries.");
                    break;
            }
        }
    }

    private static void ToggleSelection(CacheTreeItem item, List<CacheTreeItem> allItems)
    {
        if (item.IsDirectory)
        {
            // Toggle all files under this directory
            var dirPath = item.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var children = allItems.Where(i => !i.IsDirectory &&
                i.FullPath.StartsWith(dirPath, StringComparison.OrdinalIgnoreCase)).ToList();
            var newState = !children.All(c => c.IsSelected);
            foreach (var child in children)
            {
                child.IsSelected = newState;
            }
        }
        else
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private static List<CacheTreeItem> BuildTreeItems(FileHashCache cache)
    {
        var entries = cache.GetAllEntries()
            .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.Count == 0)
        {
            return [];
        }

        // Find common root prefix
        var commonRoot = FindCommonRoot(entries.Select(e => Path.GetDirectoryName(e.FilePath) ?? "").ToList());

        // Build directory -> entries mapping
        var dirEntries = new Dictionary<string, List<FileHashCacheEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var dir = Path.GetDirectoryName(entry.FilePath) ?? "";
            if (!dirEntries.TryGetValue(dir, out var list))
            {
                list = [];
                dirEntries[dir] = list;
            }
            list.Add(entry);
        }

        // Collect all unique directory paths and build hierarchy
        var allDirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirEntries.Keys)
        {
            var current = dir;
            while (!string.IsNullOrEmpty(current) && current.Length >= commonRoot.Length)
            {
                allDirs.Add(current);
                current = Path.GetDirectoryName(current) ?? "";
            }
        }

        // Calculate depth relative to common root
        int rootDepth = commonRoot.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;

        var items = new List<CacheTreeItem>();
        var dirItemMap = new Dictionary<string, CacheTreeItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in allDirs)
        {
            int dirDepth = dir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length - rootDepth;
            var dirItem = new CacheTreeItem(dir + Path.DirectorySeparatorChar, isDirectory: true, depth: dirDepth)
            {
                IsExpanded = true,
            };

            // Calculate aggregated stats for this directory
            var filesUnder = entries.Where(e =>
                (Path.GetDirectoryName(e.FilePath) ?? "").StartsWith(dir, StringComparison.OrdinalIgnoreCase)).ToList();
            dirItem.FileCount = filesUnder.Count;
            dirItem.TotalSize = filesUnder.Sum(e => e.FileSize);
            dirItem.StaleCount = filesUnder.Count(e => cache.IsStale(e.FilePath));

            dirItemMap[dir] = dirItem;
            items.Add(dirItem);

            // Add file entries for this directory
            if (dirEntries.TryGetValue(dir, out var fileEntries))
            {
                foreach (var entry in fileEntries)
                {
                    var fileItem = new CacheTreeItem(entry.FilePath, isDirectory: false, depth: dirDepth + 1, entry)
                    {
                        IsStale = cache.IsStale(entry.FilePath),
                    };
                    items.Add(fileItem);
                }
            }
        }

        return items;
    }

    private static void RebuildVisibleList(ListWidget<CacheTreeItem> list, List<CacheTreeItem> allItems)
    {
        var currentPath = list.SelectedItem?.FullPath;

        list.Items.Clear();

        // Only show items whose parent directories are all expanded
        var collapsedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allItems)
        {
            if (item.IsDirectory && !item.IsExpanded)
            {
                collapsedDirs.Add(item.FullPath.TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        foreach (var item in allItems)
        {
            // Check if any ancestor is collapsed
            var parentDir = item.IsDirectory
                ? Path.GetDirectoryName(item.FullPath.TrimEnd(Path.DirectorySeparatorChar)) ?? ""
                : Path.GetDirectoryName(item.FullPath) ?? "";

            bool hidden = false;
            var check = parentDir;
            while (!string.IsNullOrEmpty(check))
            {
                if (collapsedDirs.Contains(check))
                {
                    hidden = true;
                    break;
                }
                check = Path.GetDirectoryName(check) ?? "";
            }

            if (!hidden)
            {
                list.Items.Add(item);
            }
        }

        // Restore selection
        if (currentPath != null)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (string.Equals(list.Items[i].FullPath, currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    list.SelectedIndex = i;
                    return;
                }
            }
        }

        if (list.Items.Count > 0)
        {
            list.SelectedIndex = 0;
        }
    }

    private static string FindCommonRoot(List<string> paths)
    {
        if (paths.Count == 0)
        {
            return "";
        }
        if (paths.Count == 1)
        {
            return paths[0];
        }

        var first = paths[0];
        var commonLength = first.Length;

        foreach (var path in paths.Skip(1))
        {
            var minLen = Math.Min(commonLength, path.Length);
            int i = 0;
            while (i < minLen && char.ToUpperInvariant(first[i]) == char.ToUpperInvariant(path[i]))
            {
                i++;
            }
            commonLength = i;
        }

        var common = first[..commonLength];
        // Truncate to last separator
        var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);
        if (lastSep >= 0)
        {
            common = common[..(lastSep + 1)];
        }

        return common;
    }

    private static void SetStatus(ref string? statusMessage, ref DateTime statusExpiry, string message)
    {
        statusMessage = message;
        statusExpiry = DateTime.UtcNow.AddSeconds(3);
    }

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
