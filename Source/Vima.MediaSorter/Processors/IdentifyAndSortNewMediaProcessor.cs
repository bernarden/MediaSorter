using System;
using System.Collections.Generic;
using System.IO;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors;

public class IdentifyAndSortNewMediaProcessor(
    IDirectoryIdentifingService directoryIdentifingService,
    IMediaIdentifyingService mediaIdentifyingService,
    IMediaSortingService mediaSortingService,
    IRelatedFileDiscoveryService relatedFileDiscoveryService,
    MediaSorterSettings settings
) : IProcessor
{
    public ProcessorOption Option => ProcessorOption.IdentifyAndSortNewMedia;

    public void Process()
    {
        var directoryStructure = directoryIdentifingService.IdentifyDirectoryStructure();

        IEnumerable<string> directoriesToScan =
        [
            settings.Directory,
            .. directoryStructure.UnsortedFolders,
        ];
        var identified = mediaIdentifyingService.Identify(directoriesToScan);
        if (identified.MediaFiles.Count == 0)
        {
            Console.WriteLine("No media files found.");
            return;
        }

        var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
            identified.MediaFiles, identified.IgnoredFiles);
        string associatedInfo = associated.AssociatedFiles.Count > 0 ? $" (+{associated.AssociatedFiles.Count} associated)" : "";
        Console.WriteLine($"  Identified: {identified.MediaFiles.Count}{associatedInfo} | Ignored: {associated.RemainingIgnoredFiles.Count}");

        ConsoleKey proceed = ConsoleHelper.AskYesNoQuestion("Proceed to sort these files?", ConsoleKey.N);
        if (proceed != ConsoleKey.Y) return;

        List<DuplicateFile> duplicates = mediaSortingService.Sort(
            identified.MediaFiles,
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
}