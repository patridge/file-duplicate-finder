using FileDeduplicator.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileDeduplicator;

internal class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "FileDeduplicator";
            config.SetApplicationName(executableName);
            config.ValidateExamples();

            config.AddCommand<CompareCommand>("compare")
                .WithExample("compare", "./path/to/file1.txt", "./path/to/file2.txt");

            config.AddCommand<FindDuplicatesCommand>("find-duplicates")
                .WithExample("find-duplicates", "--path", "./path/to/files-to-scan")
                .WithExample("find-duplicates", "--path", "./path/to/files-to-scan", "--min-size", "500MB")
                .WithExample("find-duplicates", "--path", "./path/to/files-to-scan", "--use-cache");
            
            config.AddBranch("cache", cache =>
            {
                cache.SetDescription("Manage the file hash cache.");
                cache.AddCommand<CacheViewCommand>("view")
                    .WithExample("cache", "view");
                cache.AddCommand<CacheClearCommand>("clear")
                    .WithExample("cache", "clear")
                    .WithExample("cache", "clear", "--path", "./path/to/clear/cache")
                    .WithExample("cache", "clear", "--stale");
                cache.AddCommand<CacheRefreshCommand>("refresh")
                    .WithExample("cache", "refresh");
            });
            
            // TODO: Offer `find` command to locate duplicates of a given starting file.
            // config.AddCommand<FindCommand>("find")
            //     .WithExample("find", "--file", "./path/to/file-with-hash-to-match.txt");

            // // Add
            // config.AddBranch<AddSettings>("add", add =>
            // {
            //     add.SetDescription("Add a package or reference to a .NET project");
            //     add.AddCommand<AddPackageCommand>("package");
            //     add.AddCommand<AddReferenceCommand>("reference");
            // });
            //
            // // Serve
            // config.AddCommand<ServeCommand>("serve")
            //     .WithExample("serve", "-o", "firefox")
            //     .WithExample("serve", "--port", "80", "-o", "firefox");
        });

        return app.Run(args);
    }
}
