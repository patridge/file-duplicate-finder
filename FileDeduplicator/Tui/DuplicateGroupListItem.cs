using Spectre.Console;
using Spectre.Tui;
using FileDeduplicator.Common;
using Text = Spectre.Tui.Text;

namespace FileDeduplicator.Tui;

public sealed class DuplicateGroupListItem : IListWidgetItem
{
    public string Key { get; }
    public List<FileDetails> Files { get; }
    public string DisplayName => GetDisplayName();

    public DuplicateGroupListItem(string key, List<FileDetails> files)
    {
        Key = key;
        Files = files;
    }

    public Text CreateText(bool isSelected)
    {
        var text = new Text();
        text.Append(GetDisplayName(), isSelected ? new Style(Color.DarkOrange) : (Style?)null);
        text.Append($"  ({FormatFileSize(Files[0].FileSize)}, {Files.Count} files)", new Style(Color.Grey));
        text.Append($"  [{ShortenHash(Key)}]", new Style(Color.Grey));
        return text;
    }

    private string GetDisplayName()
    {
        var fileNames = Files.Select(f => Path.GetFileName(f.FilePath)).Distinct().ToList();
        if (fileNames.Count == 1)
        {
            return fileNames[0];
        }

        var extensions = Files.Select(f => Path.GetExtension(f.FilePath).ToLowerInvariant()).Distinct().ToList();
        if (extensions.Count == 1 && !string.IsNullOrWhiteSpace(extensions[0]))
        {
            return $"({extensions[0]} files)";
        }

        return "(multiple files)";
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

    private static string ShortenHash(string hash)
    {
        if (hash.Length <= 15)
        {
            return hash;
        }
        return $"{hash[..6]}\u2026{hash[^6..]}";
    }
}
