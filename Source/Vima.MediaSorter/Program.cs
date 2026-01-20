using System;
using System.CommandLine;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MetadataDiscovery;

namespace Vima.MediaSorter;

public static class Program
{
    public static int Main(string[] args)
    {
        Option<string> directoryOption = new("--directory", "-d")
        {
            Description =
                "The directory to process. Defaults to the executable's launch directory.",
        };

        Option<ProcessorOption> processorOption = new("--processor", "-p")
        {
            Description = "Execute a specific processor directly, bypassing the interactive menu.",
            DefaultValueFactory = _ => ProcessorOption.None,
        };

        RootCommand rootCommand = new("Sorts media files into organised folders.");
        rootCommand.Add(directoryOption);
        rootCommand.Add(processorOption);

        rootCommand.SetAction(parseResult =>
        {
            string path = parseResult.GetValue(directoryOption) ?? Directory.GetCurrentDirectory();
            ProcessorOption option = parseResult.GetValue(processorOption);
            IServiceProvider serviceProvider = ConfigureServices(path);
            var app = serviceProvider.GetRequiredService<IAppOrchestrator>();
            return app.Run(option);
        });

        return rootCommand.Parse(args).Invoke();
    }

    public static IServiceProvider ConfigureServices(string directoryPath)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new MediaSorterSettings { Directory = directoryPath });

        services.AddTransient<IAppOrchestrator, AppOrchestrator>();
        services.AddTransient<IProcessor, IdentifyAndSortNewMediaProcessor>();

        services.AddTransient<IDirectoryIdentifingService, DirectoryIdentifingService>();
        services.AddTransient<IMediaIdentifyingService, MediaIdentifyingService>();
        services.AddTransient<IMediaSortingService, MediaSortingService>();

        services.AddTransient<IMediaFileHandler, JpegMediaFileHandler>();
        services.AddTransient<IMediaFileHandler, Mp4MediaFileHandler>();

        return services.BuildServiceProvider();
    }
}
