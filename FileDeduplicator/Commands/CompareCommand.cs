using System.ComponentModel;
using FileDeduplicator.Common;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator.Commands;

[Description("Compare two files for size and hash.")]
public sealed class CompareCommand : Command<CompareCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE1>")]
        [Description("The path to the first file to compare.")]
        public required string File1Path { get; set; }

        [CommandArgument(1, "<FILE2>")]
        [Description("The path to the second file to compare.")]
        public required string File2Path { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Validate file paths
        if (!File.Exists(settings.File1Path))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(settings.File1Path)}");
            return 1;
        }

        if (!File.Exists(settings.File2Path))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(settings.File2Path)}");
            return 1;
        }

        // Get file information
        var file1Info = new FileInfo(settings.File1Path);
        var file2Info = new FileInfo(settings.File2Path);

        var file1Size = file1Info.Length;
        var file2Size = file2Info.Length;

        var file1Hash = FileHelpers.GetFileSha256(settings.File1Path).ToHexString();
        var file2Hash = FileHelpers.GetFileSha256(settings.File2Path).ToHexString();

        // Determine if values are different
        var sizesMatch = file1Size == file2Size;
        var hashesMatch = file1Hash == file2Hash;

        // Create comparison table
        var table = new Table();
        table.Title = new TableTitle("[bold]File Comparison[/]");
        table.AddColumn(new TableColumn("[bold]Criteria[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]File 1[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]File 2[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]Match[/]").Centered());

        // Add file paths row
        table.AddRow(
            "Path",
            Markup.Escape(settings.File1Path),
            Markup.Escape(settings.File2Path),
            "[dim]-[/]"
        );

        // Add size row with highlighting if different
        var sizeStyle = sizesMatch ? "" : "[red]";
        var sizeEndStyle = sizesMatch ? "" : "[/]";
        var sizeMatchIndicator = sizesMatch ? "[green]✓[/]" : "[red]✗[/]";
        table.AddRow(
            "Size (bytes)",
            $"{sizeStyle}{file1Size:N0}{sizeEndStyle}",
            $"{sizeStyle}{file2Size:N0}{sizeEndStyle}",
            sizeMatchIndicator
        );

        // Add hash row with highlighting if different
        var hashStyle = hashesMatch ? "" : "[red]";
        var hashEndStyle = hashesMatch ? "" : "[/]";
        var hashMatchIndicator = hashesMatch ? "[green]✓[/]" : "[red]✗[/]";
        table.AddRow(
            "SHA-256 Hash",
            $"{hashStyle}{file1Hash}{hashEndStyle}",
            $"{hashStyle}{file2Hash}{hashEndStyle}",
            hashMatchIndicator
        );

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Summary
        if (sizesMatch && hashesMatch)
        {
            AnsiConsole.MarkupLine("[green]The files are identical.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]The files are different.[/]");
        }

        return 0;
    }
}
