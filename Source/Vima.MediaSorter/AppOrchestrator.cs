using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter;

public interface IAppOrchestrator
{
    int Run(ProcessorOptions preselectedOption);
}

public class AppOrchestrator(
    IEnumerable<IProcessor> processors,
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IOptions<MediaSorterOptions> options) : IAppOrchestrator
{
    private const string Separator = "===============================================================================";
    private const int ErrorExitCode = 1;
    private const int SuccessExitCode = 0;
    private readonly MediaSorterOptions _options = options.Value;

    public int Run(ProcessorOptions preselectedOption)
    {
        try
        {
            OutputHeader(mediaFileHandlers);

            // CLI execution hides the menu and runs only the selected processor.
            if (preselectedOption != ProcessorOptions.None)
            {
                Console.WriteLine($"Mode: Automatic selection via command-line argument (-p {preselectedOption}).");
                return ExecuteByOption(preselectedOption);
            }

            while (true)
            {
                ProcessorOptions selectedOption = GetProcessorOption();
                if (selectedOption == ProcessorOptions.Exit) break;
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

    private void OutputHeader(IEnumerable<IMediaFileHandler> mediaFileHandlers)
    {
        var assemblyVersion = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        string versionInfo = assemblyVersion?.Split('+') switch
        {
            [var v, var h] => $"v{v} ({h[..Math.Min(7, h.Length)]})",
            [var v] => $"v{v} (unknown)",
            _ => "v0.0.0 (unknown)",
        };

        var allExtensions = mediaFileHandlers
            .SelectMany(h => h.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e);

        Console.WriteLine(Separator);
        Console.WriteLine($"Vima MediaSorter {versionInfo}");
        Console.WriteLine($"Processing Directory: {_options.Directory}");
        Console.WriteLine(Separator);

        Console.WriteLine("Configuration Details:");
        Console.WriteLine($"  Folder Name Format: {_options.FolderNameFormat}");
        Console.WriteLine($"  Supported Extensions: {string.Join(", ", allExtensions)}");

        Console.WriteLine(Separator);
        Console.WriteLine();
    }

    private int ExecuteByOption(ProcessorOptions option)
    {
        var processor = processors.FirstOrDefault(p => p.Option == option);
        if (processor == null)
        {
            Console.Error.WriteLine($"Error: No processor found for option {option}");
            return ErrorExitCode;
        }

        Console.WriteLine($"Executing: {processor.GetType().Name}");
        processor.Process();
        Console.WriteLine(Separator);
        Console.WriteLine();
        return SuccessExitCode;
    }

    private static ProcessorOptions GetProcessorOption()
    {
        Console.WriteLine("Select an option (Default is exit):");
        Console.WriteLine($"  {(int)ProcessorOptions.Exit}. Exit.");
        Console.WriteLine($"  {(int)ProcessorOptions.IdentifyAndSortNewMedia}. Identify and sort new media.");
        Console.Write("Enter choice: ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int intChoice) &&
            Enum.IsDefined(typeof(ProcessorOptions), intChoice) &&
            intChoice != (int)ProcessorOptions.None)
        {
            return (ProcessorOptions)intChoice;
        }

        return ProcessorOptions.Exit;
    }
}
