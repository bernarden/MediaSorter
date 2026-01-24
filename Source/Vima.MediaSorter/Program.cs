using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.IO;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Processors;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;

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

        Option<ProcessorOptions> processorOption = new("--processor", "-p")
        {
            Description = "Execute a specific processor directly, bypassing the interactive menu.",
            DefaultValueFactory = _ => ProcessorOptions.None,
        };

        RootCommand rootCommand = new("Sorts media files into organised folders.");
        rootCommand.Add(directoryOption);
        rootCommand.Add(processorOption);

        rootCommand.SetAction(parseResult =>
        {
            string path = parseResult.GetValue(directoryOption) ?? Directory.GetCurrentDirectory();
            ProcessorOptions option = parseResult.GetValue(processorOption);
            IServiceProvider serviceProvider = ConfigureServices(path);
            var app = serviceProvider.GetRequiredService<IAppOrchestrator>();
            return app.Run(option);
        });

        return rootCommand.Parse(args).Invoke();
    }

    public static IServiceProvider ConfigureServices(string directoryPath)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new MediaSorterOptions { Directory = directoryPath });

        services.AddTransient<IAppOrchestrator, AppOrchestrator>();
        services.AddTransient<IProcessor, IdentifyAndSortNewMediaProcessor>();

        services.AddTransient<IDirectoryIdentificationService, DirectoryIdentificationService>();
        services.AddTransient<IMediaIdentificationService, MediaIdentificationService>();
        services.AddTransient<IRelatedFilesDiscoveryService, RelatedFilesDiscoveryService>();
        services.AddTransient<IMediaSortingService, MediaSortingService>();
        services.AddTransient<ITimeZoneAdjustmentService, TimeZoneAdjustmentService>();

        services.AddTransient<IMediaFileHandler, JpegMediaFileHandler>();
        services.AddTransient<IMediaFileHandler, Cr3MediaFileHandler>();
        services.AddTransient<IMediaFileHandler, Mp4MediaFileHandler>();

        services.AddTransient<IDuplicateDetector, DuplicateDetector>();
        services.AddTransient<IFileMover, FileMover>();
        services.AddTransient<IDirectoryResolver, DirectoryResolver>();

        return services.BuildServiceProvider();
    }
}
