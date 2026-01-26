using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Processors;

public class IdentifyAndSortNewMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IMediaSortingService mediaSortingService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.IdentifyAndSortNewMedia;

    public void Process()
    {
        Console.Write("Identifying directory structure... ");
        DirectoryStructure directoryStructure = ExecuteWithProgress(directoryIdentifingService.IdentifyDirectoryStructure);
        Console.WriteLine("Done.");

        ReportDirectoryConflicts(directoryStructure);


        Console.Write("Identifying your media... ");
        IEnumerable<string> directoriesToScan =
        [
            options.Value.Directory,
            .. directoryStructure.UnsortedFolders,
        ];
        var identified = ExecuteWithProgress(p => mediaIdentifyingService.Identify(directoriesToScan, p));
        Console.WriteLine("Done.");

        if (identified.MediaFilesWithDates.Count == 0)
        {
            Console.WriteLine("No media files found.");
            return;
        }

        var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
            identified.MediaFilesWithDates, identified.UnsupportedFiles);
        string associatedInfo = associated.AssociatedFiles.Count > 0 ? $" (+{associated.AssociatedFiles.Count} associated)" : "";
        Console.WriteLine($"  Identified: {identified.MediaFilesWithDates.Count}{associatedInfo} | Ignored: {associated.RemainingIgnoredFiles.Count}");

        ConsoleKey proceed = ConsoleHelper.AskYesNoQuestion("Proceed to sort these files?", ConsoleKey.N);
        if (proceed != ConsoleKey.Y) return;

        timeZoneAdjusterService.ApplyOffsetsIfNeeded(identified.MediaFilesWithDates);

        List<DuplicateFile> duplicates = mediaSortingService.Sort(
            identified.MediaFilesWithDates,
            directoryStructure.DateToExistingDirectoryMapping
        );

        if (duplicates.Count > 0)
        {
            Console.WriteLine($"  Detected {duplicates.Count} duplicate file(s).");
            ConsoleKey del = ConsoleHelper.AskYesNoQuestion("Would you like to delete duplicates?", ConsoleKey.N);
            if (del == ConsoleKey.Y)
            {
                Console.Write("Deleting duplicated files... ");
                using ProgressBar progress = new();
                for (int index = 0; index < duplicates.Count; index++)
                {
                    DuplicateFile duplicateFile = duplicates[index];
                    File.Delete(duplicateFile.OriginalFilePath);
                    progress.Report((double)index / duplicates.Count);
                }

                progress.Dispose();
                Console.WriteLine("Done.");
            }
        }
    }

    private static T ExecuteWithProgress<T>(Func<IProgress<double>, T> serviceCall)
    {
        using var progress = new ProgressBar();
        var progressReporter = new Progress<double>(progress.Report);
        var result = serviceCall(progressReporter);
        return result;
    }

    private static void ReportDirectoryConflicts(DirectoryStructure structure)
    {
        if (structure.DateToIgnoredDirectoriesMapping.Count == 0) return;

        Console.WriteLine("  Warning: Multiple directories mapped to the same dates (Only first discovery used):");
        foreach (var (date, ignoredPaths) in structure.DateToIgnoredDirectoriesMapping.OrderBy(x => x.Key))
        {
            string usedDir = Path.GetFileName(structure.DateToExistingDirectoryMapping[date]);
            Console.WriteLine($"    [{date:yyyy_MM_dd}] Target: '{usedDir}'");

            foreach (var ignored in ignoredPaths)
            {
                Console.WriteLine($"               Ignore: '{Path.GetFileName(ignored)}'");
            }
        }
    }
}