using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        Console.WriteLine("[Step 1/2] Identification");
        Console.WriteLine(ConsoleHelper.TaskSeparator);

        var directoryStructure = ExecuteWithProgress("Identifying directories",
            directoryIdentifingService.Identify);

        IEnumerable<string> directoriesToScan = [options.Value.Directory, .. directoryStructure.UnsortedFolders];
        var identified = ExecuteWithProgress("Identifying media files",
            p => mediaIdentifyingService.Identify(directoriesToScan, p));

        var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
            identified.MediaFilesWithDates, identified.UnsupportedFiles);

        Console.WriteLine();
        Console.WriteLine("Analysis Result:");
        var readyCount = identified.MediaFilesWithDates.Count;
        var sidecarCount = associated.AssociatedFiles.Count;
        var missingDateCount = identified.MediaFilesWithoutDates.Count;
        var identificationErrorCount = identified.ErroredFiles.Count;
        var unsupportedCount = associated.RemainingIgnoredFiles.Count;
        Console.WriteLine($"  New media:    {readyCount} files");
        Console.WriteLine($"  Sidecars:     {sidecarCount} related files");
        Console.WriteLine($"  Missing date: {missingDateCount} files (no metadata)");
        Console.WriteLine($"  Unsupported:  {unsupportedCount} files (skipped)");

        if (identificationErrorCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Errors:       {identificationErrorCount} files (failed to open/parse)");
            Console.ResetColor();
        }

        ReportDiscoveryAlerts(directoryStructure, missingDateCount, identified.ErroredFiles);

        Console.WriteLine();
        Console.WriteLine(ConsoleHelper.TaskSeparator);
        Console.WriteLine("[Step 2/2] Sorting");
        Console.WriteLine(ConsoleHelper.TaskSeparator);

        int totalFilesToMove = readyCount + sidecarCount;
        var targetFolderCount = identified.MediaFilesWithDates
            .Select(f => f.CreatedOn.Date)
            .Distinct()
            .Count();

        if (totalFilesToMove == 0)
        {
            Console.WriteLine("No new media files found to sort.");
            return;
        }

        if (ConsoleHelper.AskYesNoQuestion($"Action: Sort {totalFilesToMove} file(s) into {targetFolderCount} date folder(s)?", ConsoleKey.N) != ConsoleKey.Y)
        {
            Console.WriteLine("Result: Operation aborted.");
            return;
        }

        timeZoneAdjusterService.ApplyOffsetsIfNeeded(identified.MediaFilesWithDates);

        var sortingResult = ExecuteWithProgress("Status: Sorting",
            p => mediaSortingService.Sort(identified.MediaFilesWithDates, directoryStructure.DateToExistingDirectoryMapping, p));

        if (sortingResult.Moved.Count > 0)
        {
            Console.WriteLine($"Result: {sortingResult.Moved.Count} files moved.");
        }
        else if (sortingResult.Duplicates.Count > 0)
        {
            Console.WriteLine("Result: All files are already organised.");
        }

        if (sortingResult.Errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"(!) {sortingResult.Errors.Count} file(s) failed due to errors:");

            foreach (var error in sortingResult.Errors.Take(5))
                Console.WriteLine($"    - {Path.GetFileName(error.SourcePath)}: {error.Exception.Message}");

            if (sortingResult.Errors.Count > 5)
                Console.WriteLine("    - ... (see logs for more)");

            Console.ResetColor();
        }

        HandleDuplicates(sortingResult.Duplicates);

        Console.WriteLine();
    }

    private static T ExecuteWithProgress<T>(string label, Func<IProgress<double>, T> serviceCall)
    {
        Console.Write($"{label}... ");
        var sw = Stopwatch.StartNew();
        T result;
        using (var progress = new ProgressBar())
        {
            var progressReporter = new Progress<double>(progress.Report);
            result = serviceCall(progressReporter);
        }
        sw.Stop();
        string timeTaken = sw.Elapsed.TotalSeconds > 1 ? $"{sw.Elapsed.TotalSeconds:N1}s" : $"{sw.Elapsed.TotalMilliseconds:N0}ms";
        Console.WriteLine($"Done ({timeTaken}).");
        return result;
    }

    private static void ReportDiscoveryAlerts(
         DirectoryStructure structure,
         int missingDateCount,
         IReadOnlyList<FileIdentificationError> identificationErrors)
    {
        var hasConflicts = structure.DateToIgnoredDirectoriesMapping.Count > 0;
        if (!hasConflicts && missingDateCount == 0 && identificationErrors.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("(!) Discovery Alerts:");

        if (hasConflicts)
        {
            int dateConflictCount = structure.DateToIgnoredDirectoriesMapping.Count;
            string dateWord = dateConflictCount == 1 ? "date has" : "dates have";
            Console.WriteLine($"    - {dateConflictCount} {dateWord} multiple folders mapped.");
        }

        if (missingDateCount > 0)
        {
            Console.WriteLine($"    - {missingDateCount} file(s) skipped: Supported file type, but no date metadata found.");
        }

        if (identificationErrors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    - {identificationErrors.Count} file(s) could not be read due to errors:");

            foreach (var error in identificationErrors.Take(5))
                Console.WriteLine($"      - {Path.GetFileName(error.FilePath)}: {error.Exception.Message}");

            if (identificationErrors.Count > 5)
                Console.WriteLine("      - ... (see logs for more)");

            Console.ResetColor();
        }
    }

    private static void HandleDuplicates(List<DuplicateDetectedFileMove> duplicates)
    {
        if (duplicates.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Duplicate Management:");
        if (ConsoleHelper.AskYesNoQuestion($"  Action: Delete {duplicates.Count} exact duplicates?", ConsoleKey.N) != ConsoleKey.Y)
        {
            Console.WriteLine("  Result: Operation aborted.");
            return;
        }

        ExecuteWithProgress("  Status: Cleaning up", p =>
        {
            for (int i = 0; i < duplicates.Count; i++)
            {
                var fileInfo = new FileInfo(duplicates[i].SourcePath);
                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }
                p.Report((double)(i + 1) / duplicates.Count);
            }
            return true;
        });

        Console.WriteLine($"  Result: Deleted {duplicates.Count} files.");

    }
}