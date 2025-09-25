using MyApp.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MyApp;

internal class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("deduper");
            config.ValidateExamples();
            config.AddExample("scan");

            config.AddCommand<ScanCommand>("scan")
                .WithExample("scan", "--path", "./path/to/files");
            
            // TODO: Offer `find` command to locate duplicates of a given starting file.

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
