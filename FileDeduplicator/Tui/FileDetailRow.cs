using Spectre.Console;
using Spectre.Tui;
using FileDeduplicator.Common;
using Text = Spectre.Tui.Text;

namespace FileDeduplicator.Tui;

public sealed class FileDetailRow : ITableRow
{
    public FileDetails File { get; }

    public FileDetailRow(FileDetails file)
    {
        File = file;
    }

    public Text[] CreateCells(bool isSelected)
    {
        var nameStyle = isSelected ? new Style(Color.DarkOrange) : (Style?)null;
        var sizeStyle = isSelected ? new Style(Color.DarkOrange) : new Style(Color.Blue);
        var pathStyle = isSelected ? new Style(Color.DarkOrange) : new Style(Color.Grey);

        var name = new Text();
        name.Append(Path.GetFileName(File.FilePath), nameStyle);

        var size = new Text();
        size.Append(FormatFileSize(File.FileSize), sizeStyle);

        var dir = new Text();
        dir.Append(Path.GetDirectoryName(File.FilePath) ?? string.Empty, pathStyle);

        return [name, size, dir];
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
