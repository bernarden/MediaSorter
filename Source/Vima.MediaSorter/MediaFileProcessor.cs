using System;
using System.Collections.Generic;
using System.IO;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter;

public class MediaFileProcessor
{
    public static readonly string FolderNameFormat = "yyyy_MM_dd -";
    public static readonly List<string> ImageExtensions = new() { ".jpg", ".jpeg" };
    public static readonly List<string> VideoExtensions = new() { ".mp4" };

    private readonly MediaSorterSettings _settings;

    public MediaFileProcessor(MediaSorterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Process()
    {
        var identifier = new MediaIdentifingService(_settings);
        var result = identifier.Identify();
        if (result.MediaFiles.Count == 0)
        {
            Console.WriteLine("No media files found. Exiting.");
            return;
        }

        Console.WriteLine($"Identified {result.MediaFiles.Count} media file(s). Ignored: {result.IgnoredFiles.Count}");
        ConsoleKey proceed = ConsoleHelper.AskYesNoQuestion("Proceed to sort these files?", ConsoleKey.N);
        if (proceed != ConsoleKey.Y) return;

        var sortingService = new MediaSortingService(_settings, result.ExistingDirectoryMapping);
        List<DuplicateFile> duplicates = sortingService.Sort(result.MediaFiles);

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

        Console.WriteLine("Press enter to finish...");
        Console.ReadLine();
    }
}