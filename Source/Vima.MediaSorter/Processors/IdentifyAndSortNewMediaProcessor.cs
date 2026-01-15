using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors;

public class IdentifyAndSortNewMediaProcessor(MediaSorterSettings settings) : IProcessor
{
    public void Process()
    {
        var directoryIdentifier = new DirectoryIdentifingService(settings);
        var directoryStructure = directoryIdentifier.IdentifyDirectoryStructure();

        var identifier = new MediaIdentifyingService(settings);
        IEnumerable<string> directoriesToScan = new List<string> { settings.Directory }.Concat(directoryStructure.UnsortedFolders);
        var result = identifier.Identify(directoriesToScan);
        if (result.MediaFiles.Count == 0)
        {
            Console.WriteLine("No media files found.");
            return;
        }

        Console.WriteLine($"Identified {result.MediaFiles.Count} media file(s). Ignored: {result.IgnoredFiles.Count}");
        ConsoleKey proceed = ConsoleHelper.AskYesNoQuestion("Proceed to sort these files?", ConsoleKey.N);
        if (proceed != ConsoleKey.Y) return;

        var sortingService = new MediaSortingService(settings);
        List<DuplicateFile> duplicates = sortingService.Sort(result.MediaFiles, directoryStructure.DateToExistingDirectoryMapping);

        if (duplicates.Count > 0)
        {
            Console.WriteLine($"Detected {duplicates.Count} duplicate file(s).");
            ConsoleKey del = ConsoleHelper.AskYesNoQuestion("Would you like to delete them?", ConsoleKey.N);
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