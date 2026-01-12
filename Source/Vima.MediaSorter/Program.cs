using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;

namespace Vima.MediaSorter;

public static class Program
{
    private const string Separator = "===============================================================================";
    private const int ErrorExitCode = 1;
    private const int SuccessExitCode = 0;

    public static int Main(string[] args)
    {
        Option<string> directoryOption = new("--directory", "-d")
        {
            Description = "The directory to process. Defaults to the executable's launch directory."
        };

        Option<ProcessorOption> processorOption = new("--processor", "-p")
        {
            Description = "Execute a specific processor directly, bypassing the interactive menu.",
            DefaultValueFactory = _ => ProcessorOption.None
        };

        RootCommand rootCommand = new("Sorts media files into organised folders.");
        rootCommand.Add(directoryOption);
        rootCommand.Add(processorOption);
        rootCommand.SetAction(parseResult =>
            RunMediaSorter(
                parseResult.GetValue(directoryOption),
                parseResult.GetValue(processorOption)
            )
        );

        ParseResult parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    private static int RunMediaSorter(string? directory, ProcessorOption preselectedProcessorOption)
    {
        string directoryPath = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        MediaSorterSettings settings = new() { Directory = directoryPath };

        try
        {
            OutputHeader(settings);

            // CLI execution hides the menu and runs only the selected processor.
            IProcessor? preselectedProcessor = MapOptionToProcessor(preselectedProcessorOption, settings);
            if (preselectedProcessor != null)
            {
                Console.WriteLine($"Mode: Automatic selection via command-line argument (-p {preselectedProcessorOption}).");
                ExecuteProcessor(preselectedProcessor);
                return SuccessExitCode;
            }

            while (true)
            {
                ProcessorOption selectedOption = GetProcessorOption();
                IProcessor? selectedProcessor = MapOptionToProcessor(selectedOption, settings);
                if (selectedProcessor == null) break;
                ExecuteProcessor(selectedProcessor);
            }

            return SuccessExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.Error.WriteLine(Separator);
            Console.Error.WriteLine($"A critical error occurred: {ex.Message}");
            Console.Error.WriteLine(Separator);
            return ErrorExitCode;
        }
        finally
        {
            Console.WriteLine("Press enter to finish...");
            Console.ReadLine();
        }
    }

    private static void OutputHeader(MediaSorterSettings settings)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

        Console.WriteLine(Separator);
        Console.WriteLine($"Vima MediaSorter v{fileVersionInfo.FileVersion}");
        Console.WriteLine($"Processing Directory: {settings.Directory}");
        Console.WriteLine(Separator);

        Console.WriteLine("Configuration Details:");
        Console.WriteLine($"  Folder Name Format: {settings.FolderNameFormat}");
        Console.WriteLine($"  Image Extensions: {string.Join(", ", settings.ImageExtensions)}");
        Console.WriteLine($"  Video Extensions: {string.Join(", ", settings.VideoExtensions)}");

        Console.WriteLine(Separator);
        Console.WriteLine();
    }

    private static IProcessor? MapOptionToProcessor(
    ProcessorOption option, MediaSorterSettings settings)
    {
        return option switch
        {
            ProcessorOption.IdentifyAndSortNewMedia => new IdentifyAndSortNewMediaProcessor(settings),
            _ => null,
        };
    }

    private static void ExecuteProcessor(IProcessor processorToRun)
    {
        Console.WriteLine($"Executing Processor: {processorToRun.GetType().Name}");
        processorToRun.Process();
        Console.WriteLine(Separator);
        Console.WriteLine();
    }

    private static ProcessorOption GetProcessorOption()
    {
        Console.WriteLine("Select an option (Default is exit):");
        Console.WriteLine($"  {(int)ProcessorOption.Exit}. Exit.");
        Console.WriteLine($"  {(int)ProcessorOption.IdentifyAndSortNewMedia}. Identify and sort new media.");
        Console.Write("Enter choice: ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int intChoice) &&
            Enum.IsDefined(typeof(ProcessorOption), intChoice) &&
            intChoice != (int)ProcessorOption.None)
        {
            return (ProcessorOption)intChoice;
        }

        return ProcessorOption.Exit;
    }
}