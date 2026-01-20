using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Reflection;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;

namespace Vima.MediaSorter;

public class AppOrchestrator(IServiceProvider serviceProvider, MediaSorterSettings settings)
{
    private const string Separator = "===============================================================================";
    private const int ErrorExitCode = 1;
    private const int SuccessExitCode = 0;

    public int Run(ProcessorOption preselectedOption)
    {
        try
        {
            OutputHeader(settings);

            // CLI execution hides the menu and runs only the selected processor.
            if (preselectedOption != ProcessorOption.None)
            {
                Console.WriteLine($"Mode: Automatic selection via command-line argument (-p {preselectedOption}).");
                return ExecuteByOption(preselectedOption);
            }

            while (true)
            {
                ProcessorOption selectedOption = GetProcessorOption();
                if (selectedOption == ProcessorOption.Exit) break;
                ExecuteByOption(selectedOption);
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

    private int ExecuteByOption(ProcessorOption option)
    {
        var processor = option switch
        {
            ProcessorOption.IdentifyAndSortNewMedia =>
                serviceProvider.GetRequiredService<IdentifyAndSortNewMediaProcessor>(),
            _ => null
        };

        if (processor == null) return ErrorExitCode;

        Console.WriteLine($"Executing: {processor.GetType().Name}");
        processor.Process();
        Console.WriteLine(Separator);
        Console.WriteLine();
        return SuccessExitCode;
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
