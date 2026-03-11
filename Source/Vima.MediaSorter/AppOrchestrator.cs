using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter;

public interface IAppOrchestrator
{
    int Run(ProcessorOptions preselectedOption);
}

public class AppOrchestrator(
    IEnumerable<IProcessor> processors,
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

            OutputHeader();

            // CLI execution hides the menu and runs only the selected processor.
            if (preselectedOption != ProcessorOptions.None)
            {
                outputService.WriteLine(
                    $"Mode: Automatic selection via command-line argument (-p {preselectedOption}).",
                    OutputLevel.Info
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
            outputService.Fatal("A critical error occurred", ex);
            return ErrorExitCode;
        }
        finally
        {
            outputService.WriteLine("Press enter to finish...", OutputLevel.Info);
            outputService.ReadLine();
        }
    }

    private void OutputHeader()
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
        outputService.Header(
            $"                      Vima MediaSorter {versionInfo}",
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);
    }

    private int ExecuteByOption(ProcessorOptions option)
    {
        var processor = processors.FirstOrDefault(p => p.Option == option);
        if (processor == null)
        {
            outputService.WriteLine(
                $"Error: No processor found for option {option}",
                OutputLevel.Info
            );
            return ErrorExitCode;
        }

        processor.Process();
        return SuccessExitCode;
    }

    private ProcessorOptions GetProcessorOption()
    {
        outputService.WriteLine("Available actions:", OutputLevel.Info);
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.IdentifyAndSortNewMedia}] Identify and sort new media",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.FindDuplicates}] Find duplicates",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.RenameSortedMedia}] Rename media in sorted folders",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.CleanupRawMedia}] Cleanup orphaned RAW files",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)ProcessorOptions.Exit}] Exit (default)",
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);

        int result = outputService.PromptForInt(
            "Enter choice",
            (int)ProcessorOptions.Exit,
            0,
            4,
            OutputLevel.Info
        );
        return (ProcessorOptions)result;
    }
}
