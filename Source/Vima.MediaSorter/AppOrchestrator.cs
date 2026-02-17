using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;
using Vima.MediaSorter.Services.MediaFileHandlers;
using Vima.MediaSorter.UI;

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
            Console.Error.WriteLine(ConsoleHelper.Separator);
            Console.Error.WriteLine($"A critical error occurred: {ex.Message}");
            Console.Error.WriteLine(ConsoleHelper.Separator);
            return ErrorExitCode;
        }
        finally
        {
            Console.WriteLine("Press enter to finish...");
            Console.ReadLine();
        }
    }

    private static void OutputHeader(IEnumerable<IMediaFileHandler> mediaFileHandlers)
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
        Console.WriteLine(ConsoleHelper.Separator);
        Console.WriteLine($"                      Vima MediaSorter {versionInfo}");
        Console.WriteLine(ConsoleHelper.Separator);
    }

    private int ExecuteByOption(ProcessorOptions option)
    {
        var processor = processors.FirstOrDefault(p => p.Option == option);
        if (processor == null)
        {
            Console.Error.WriteLine($"Error: No processor found for option {option}");
            return ErrorExitCode;
        }

        Console.WriteLine();
        Console.WriteLine(ConsoleHelper.Separator);
        Console.WriteLine($"Executing: {processor.GetType().Name}");
        Console.WriteLine(ConsoleHelper.Separator);
        processor.Process();
        Console.WriteLine(ConsoleHelper.Separator);
        return SuccessExitCode;
    }

    private static ProcessorOptions GetProcessorOption()
    {
        Console.WriteLine();
        Console.WriteLine("Available actions:");
        Console.WriteLine($"  [{(int)ProcessorOptions.IdentifyAndSortNewMedia}] Identify and sort new media");
        Console.WriteLine($"  [{(int)ProcessorOptions.FindDuplicates}] Find duplicates");
        Console.WriteLine($"  [{(int)ProcessorOptions.RenameSortedMedia}] Rename media in sorted folders");
        Console.WriteLine($"  [{(int)ProcessorOptions.Exit}] Exit (default)");
        Console.WriteLine();
        return ConsoleHelper.PromptForEnum("Enter choice", ProcessorOptions.Exit);
    }
}
