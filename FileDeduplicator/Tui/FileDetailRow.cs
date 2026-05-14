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
        var dateStyle = isSelected ? new Style(Color.DarkOrange) : new Style(Color.Grey);
        var pathStyle = isSelected ? new Style(Color.DarkOrange) : new Style(Color.Grey);

        var name = new Text();
        name.Append(Path.GetFileName(File.FilePath), nameStyle);

        var size = new Text();
        size.Append(FileHelpers.FormatFileSize(File.FileSize), sizeStyle);

        var modified = new Text();
        modified.Append(File.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), dateStyle);

        var dir = new Text();
        dir.Append(Path.GetDirectoryName(File.FilePath) ?? string.Empty, pathStyle);

        return [name, size, modified, dir];
    }
}
