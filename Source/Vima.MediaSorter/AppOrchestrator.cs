using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter;

public interface IAppOrchestrator
{
    int Run(ProcessorOptions preselectedOption);
}

public class AppOrchestrator(
    IEnumerable<IProcessor> processors,
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IOutputService outputService,
    IOptions<MediaSorterOptions> options
) : IAppOrchestrator
{
    private const int ErrorExitCode = 1;
    private const int SuccessExitCode = 0;
    private readonly MediaSorterOptions _options = options.Value;

    public int Run(ProcessorOptions preselectedOption)
    {
        try
        {
            outputService.Initialize();

            OutputHeader(mediaFileHandlers);

            // CLI execution hides the menu and runs only the selected processor.
            if (preselectedOption != ProcessorOptions.None)
            {
                outputService.WriteLine(
                    $"Mode: Automatic selection via command-line argument (-p {preselectedOption})."
                );
                return ExecuteByOption(preselectedOption);
            }

            while (true)
            {
                ProcessorOptions selectedOption = GetProcessorOption();
                if (selectedOption == ProcessorOptions.Exit)
                    break;

                ExecuteByOption(selectedOption);
            }

            return SuccessExitCode;
        }
        catch (Exception ex)
        {
            outputService.WriteLine();
            outputService.Section("A critical error occurred");
            outputService.WriteLine(ex.Message);
            outputService.WriteLine(ex.StackTrace);
            return ErrorExitCode;
        }
        finally
        {
            outputService.WriteLine("Press enter to finish...");
            outputService.ReadLine();
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
        outputService.Header($"                      Vima MediaSorter {versionInfo}");
        outputService.WriteLine();
    }

    private int ExecuteByOption(ProcessorOptions option)
    {
        var processor = processors.FirstOrDefault(p => p.Option == option);
        if (processor == null)
        {
            outputService.WriteLine($"Error: No processor found for option {option}");
            return ErrorExitCode;
        }

        processor.Process();
        return SuccessExitCode;
    }

    private ProcessorOptions GetProcessorOption()
    {
        outputService.WriteLine("Available actions:");
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.IdentifyAndSortNewMedia}] Identify and sort new media"
        );
        outputService.WriteLine($"  [{(int)ProcessorOptions.FindDuplicates}] Find duplicates");
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.RenameSortedMedia}] Rename media in sorted folders"
        );
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.CleanupRawMedia}] Cleanup orphaned RAW files"
        );
        outputService.WriteLine($"  [{(int)ProcessorOptions.Exit}] Exit (default)");
        outputService.WriteLine();

        ProcessorOptions result = outputService.PromptForEnum(
            "Enter choice",
            ProcessorOptions.Exit
        );
        return result;
    }
}
