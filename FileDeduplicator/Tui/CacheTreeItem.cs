using Spectre.Console;
using Spectre.Tui;
using FileDeduplicator.Common;
using Text = Spectre.Tui.Text;

namespace FileDeduplicator.Tui;

public sealed class CacheTreeItem : IListWidgetItem
{
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public int Depth { get; }
    public FileHashCacheEntry? Entry { get; }
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public int StaleCount { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsStale { get; set; }
    public bool IsSelected { get; set; }

    public CacheTreeItem(string fullPath, bool isDirectory, int depth, FileHashCacheEntry? entry = null)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Depth = depth;
        Entry = entry;
    }

    public Text CreateText(bool isHighlighted)
    {
        var text = new Text();

        // Indent based on depth
        if (Depth > 0)
        {
            text.Append(new string(' ', Depth * 2), null);
        }

        // Selection marker
        if (IsSelected)
        {
            text.Append("[*] ", new Style(Color.Green));
        }

        if (IsDirectory)
        {
            var arrow = IsExpanded ? "▼ " : "▶ ";
            string dirName;
            if (Depth == 0)
            {
                dirName = FullPath;
            }
            else
            {
                dirName = Path.GetFileName(FullPath.TrimEnd(Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(dirName))
                {
                    dirName = FullPath;
                }
                dirName += Path.DirectorySeparatorChar;
            }

            text.Append(arrow, new Style(Color.Grey));
            text.Append(dirName, isHighlighted ? new Style(Color.DarkOrange) : new Style(Color.Blue));

            var staleInfo = StaleCount > 0 ? $", {StaleCount} stale" : "";
            text.Append($"  ({FileCount} files, {FileHelpers.FormatFileSize(TotalSize)}{staleInfo})", new Style(Color.Grey));
        }
        else if (Entry != null)
        {
            var fileName = Path.GetFileName(FullPath);
            var staleMarker = IsStale ? " (stale)" : "";
            Style? staleStyle = IsStale ? new Style(Color.Red) : (Style?)null;

            text.Append("  ", (Style?)null);
            text.Append(fileName, isHighlighted ? new Style(Color.DarkOrange) : (Style?)null);
            if (IsStale)
            {
                text.Append(staleMarker, staleStyle);
            }

            var hashShort = Entry.Sha256Hash.Length >= 12
                ? Entry.Sha256Hash[..6] + "\u2026" + Entry.Sha256Hash[^6..]
                : Entry.Sha256Hash;
            text.Append($"  {FileHelpers.FormatFileSize(Entry.FileSize)}  {hashShort}  cached {FormatAge(Entry.CachedAt)}", new Style(Color.Grey));
        }

        return text;
    }

    private static string FormatAge(DateTime cachedAtUtc)
    {
        var age = DateTime.UtcNow - cachedAtUtc;
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }
        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes}m ago";
        }
        if (age.TotalDays < 1)
        {
            return $"{(int)age.TotalHours}h ago";
        }
        if (age.TotalDays < 30)
        {
            return $"{(int)age.TotalDays}d ago";
        }
        return cachedAtUtc.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
