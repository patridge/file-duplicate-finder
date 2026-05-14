using System.ComponentModel;
using FileDeduplicator.Common;
using FileDeduplicator.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator.Commands;

public class CacheSettings : CommandSettings
{
}

[Description("View and manage cached file hashes in an interactive tree viewer.")]
public sealed class CacheViewCommand : Command<CacheViewCommand.Settings>
{
    public sealed class Settings : CacheSettings
    {
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var cache = new FileHashCache();

        if (cache.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Cache is empty.[/]");
            AnsiConsole.MarkupLine($"Run [blue]find-duplicates --use-cache[/] to populate it.");
            return 0;
        }

        CacheViewer.Show(cache);

        return 0;
    }
}

[Description("Clear cached file hashes. Clears all entries, or entries under a specific path.")]
public sealed class CacheClearCommand : Command<CacheClearCommand.Settings>
{
    public sealed class Settings : CacheSettings
    {
        [CommandOption("-p|--path <PATH>")]
        [Description("Clear only entries under this path. If not specified, clears all entries.")]
        public string? FilterPath { get; set; }

        [CommandOption("--stale")]
        [Description("Clear only stale entries (files that have changed since caching).")]
        [DefaultValue(false)]
        public bool StaleOnly { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var cache = new FileHashCache();

        if (cache.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Cache is already empty.[/]");
            return 0;
        }

        int removed;
        if (settings.StaleOnly)
        {
            removed = cache.RemoveStaleEntries();
            AnsiConsole.MarkupLine($"[green]Removed {removed} stale entry/entries.[/]");
        }
        else if (!string.IsNullOrWhiteSpace(settings.FilterPath))
        {
            removed = cache.RemoveEntriesUnderPath(settings.FilterPath);
            AnsiConsole.MarkupLine($"[green]Removed {removed} entry/entries under {Markup.Escape(Path.GetFullPath(settings.FilterPath))}.[/]");
        }
        else
        {
            var totalBefore = cache.Count;
            cache.Clear();
            removed = totalBefore;
            AnsiConsole.MarkupLine($"[green]Cleared all {removed} entry/entries from cache.[/]");
        }

        cache.Save();
        return 0;
    }
}

[Description("Refresh stale cached file hashes by re-computing hashes for files that have changed.")]
public sealed class CacheRefreshCommand : Command<CacheRefreshCommand.Settings>
{
    public sealed class Settings : CacheSettings
    {
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var cache = new FileHashCache();

        if (cache.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Cache is empty. Nothing to refresh.[/]");
            return 0;
        }

        int refreshed = cache.RefreshStaleEntries(filePath =>
        {
            AnsiConsole.MarkupLine($"[dim]Refreshing: {Markup.Escape(filePath)}[/]");
        });

        if (refreshed == 0)
        {
            AnsiConsole.MarkupLine("[green]All cached entries are up to date.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Refreshed {refreshed} entry/entries.[/]");
        }

        cache.Save();
        return 0;
    }
}
