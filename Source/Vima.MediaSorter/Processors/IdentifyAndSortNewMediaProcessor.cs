using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Processors;

public class IdentifyAndSortNewMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IMediaSortingService mediaSortingService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IAuditLogService auditLogService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.IdentifyAndSortNewMedia;

    public void Process()
    {
        string logPath = auditLogService.Initialise();

        try
        {
            OutputConfiguration();

            Console.WriteLine("[Step 1/2] Identification");
            Console.WriteLine(ConsoleHelper.TaskSeparator);

            var directoryStructure = ConsoleHelper.ExecuteWithProgress("Identifying directories",
                directoryIdentifingService.Identify);

            List<string> directoriesToScan = [options.Value.Directory];
            if (directoryStructure.UnsortedFolders.Count > 0)
            {
                Console.WriteLine($"  Found {directoryStructure.UnsortedFolders.Count} unsorted sub-folder(s).");
                var response = ConsoleHelper.AskYesNoQuestion("  Include sub-folders in media scan?", ConsoleKey.N);
                if (response == ConsoleKey.Y)
                {
                    directoriesToScan.AddRange(directoryStructure.UnsortedFolders);
                }
            }

            var identified = ConsoleHelper.ExecuteWithProgress("Identifying media files",
                p => mediaIdentifyingService.Identify(directoriesToScan, p));

            var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
                identified.MediaFilesWithDates, identified.UnsupportedFiles);

            LogIdentification(identified, associated);

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
                .Select(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .Distinct()
                .Count();

            if (totalFilesToMove == 0)
            {
                Console.WriteLine("No new media files found to sort.");
                return;
            }

            Console.WriteLine($"Full plan available in: {logPath}");
            if (ConsoleHelper.AskYesNoQuestion($"Action: Sort {totalFilesToMove} file(s) into {targetFolderCount} date folder(s)?", ConsoleKey.N) != ConsoleKey.Y)
            {
                Console.WriteLine("Result: Operation aborted.");
                return;
            }

            timeZoneAdjusterService.ApplyOffsetsIfNeeded(identified.MediaFilesWithDates);

            var sortingResult = ConsoleHelper.ExecuteWithProgress("Status: Sorting",
                p => mediaSortingService.Sort(identified.MediaFilesWithDates, directoryStructure.DateToExistingDirectoryMapping, p));
            LogSorting(sortingResult);

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
            Console.WriteLine($"Processing complete. Audit log: {logPath}");
        }
        catch (Exception ex)
        {
            LogError("A critical error occurred during processing.", ex);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.WriteLine($"Details logged to: {logPath}");
            Console.ResetColor();
        }
    }

    private void OutputConfiguration()
    {
        var allExtensions = mediaFileHandlers
            .SelectMany(h => h.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e);

        Console.WriteLine($"Configuration:");
        Console.WriteLine($"  Directory:      {options.Value.Directory}");
        Console.WriteLine($"  Extensions:     {string.Join(", ", allExtensions)}");
        Console.WriteLine($"  Folder format:  {options.Value.FolderNameFormat}");
        Console.WriteLine();
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

    private void HandleDuplicates(IReadOnlyList<DuplicateDetectedFileMove> duplicates)
    {
        if (duplicates.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Duplicate Management:");
        if (ConsoleHelper.AskYesNoQuestion($"  Action: Delete {duplicates.Count} exact duplicates?", ConsoleKey.N) != ConsoleKey.Y)
        {
            Console.WriteLine("  Result: Operation aborted.");
            return;
        }

        var deletedFiles = new List<string>();
        var deletionErrors = new List<(string FilePath, Exception Exception)>();

        ConsoleHelper.ExecuteWithProgress("  Status: Cleaning up", p =>
        {
            for (int i = 0; i < duplicates.Count; i++)
            {
                var duplicateFile = duplicates[i];
                try
                {
                    var fileInfo = new FileInfo(duplicateFile.SourcePath);
                    if (fileInfo.Exists)
                    {
                        fileInfo.Delete();
                        deletedFiles.Add(duplicateFile.SourcePath);
                    }
                }
                catch (Exception ex)
                {
                    deletionErrors.Add((duplicateFile.SourcePath, ex));
                }
                p.Report((double)(i + 1) / duplicates.Count);
            }
            return true;
        });

        LogDeletedDuplicates(deletedFiles, deletionErrors);
        Console.WriteLine($"  Result: Deleted {deletedFiles.Count} files.");

        if (deletionErrors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  (!) Failed to delete {deletionErrors.Count} file(s).");
            foreach (var error in deletionErrors.Take(5))
            {
                Console.WriteLine($"      - {Path.GetFileName(error.FilePath)}: {error.Exception.Message}");
            }

            if (deletionErrors.Count > 5)
                Console.WriteLine("      - ... (see logs for more)");
            Console.ResetColor();
        }
    }

    private void LogIdentification(IdentifiedMedia identified, AssociatedMedia associated)
    {
        auditLogService.LogHeader("STEP 1: IDENTIFICATION RESULTS");

        if (identified.MediaFilesWithDates.Any())
        {
            auditLogService.LogLine("\n[Ready to Move - Grouped by Target Date]");
            var groups = identified.MediaFilesWithDates
                .GroupBy(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                auditLogService.LogLine($"\nFolder: {group.Key}");
                foreach (var file in group)
                {
                    auditLogService.LogLine($"  - [Media]   {file.FilePath}");
                    foreach (var sidecar in file.RelatedFiles)
                    {
                        auditLogService.LogLine($"  - [Sidecar] {sidecar}");
                    }
                }
            }
        }

        if (identified.MediaFilesWithoutDates.Any())
        {
            auditLogService.LogLine("\n[Missing Metadata - Will be Skipped]");
            auditLogService.LogBulletPoints(
                identified.MediaFilesWithoutDates.Select(f => $"SKIP:  {f.FilePath}"));
        }

        if (associated.RemainingIgnoredFiles.Any())
        {
            auditLogService.LogLine("\n[Ignored / Unsupported Files]");
            auditLogService.LogBulletPoints(
                associated.RemainingIgnoredFiles.Select(f => $"IGNORE: {f}"));
        }

        if (identified.ErroredFiles.Any())
        {
            auditLogService.LogLine("\n[Technical Errors]");
            auditLogService.LogBulletPoints(
                identified.ErroredFiles.Select(err => $"ERROR: {err.FilePath} (Exception: {err.Exception.Message})"));
        }

        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
        auditLogService.Flush();
    }

    private void LogSorting(SortedMedia result)
    {
        auditLogService.LogHeader("STEP 2: SORTING RESULTS");

        if (result.Moved.Any())
        {
            auditLogService.LogLine("\n[Successfully Moved]");
            auditLogService.LogBulletPoints(
                result.Moved.Select(m => $"MOVED: {m.SourcePath} -> {m.DestinationPath}"));
        }

        if (result.Duplicates.Any())
        {
            auditLogService.LogLine("\n[Duplicates Detected - Already exist at destination]");
            auditLogService.LogBulletPoints(
                result.Duplicates.Select(d => $"SKIP:  {d.SourcePath} == {d.DestinationPath}"));
        }

        if (result.Errors.Any())
        {
            auditLogService.LogLine("\n[Errors - Failed to Move]");
            auditLogService.LogBulletPoints(
                result.Errors.Select(err => $"FAIL:  {err.SourcePath} -> Reason: {err.Exception.Message}"));
        }

        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
        auditLogService.Flush();
    }

    private void LogDeletedDuplicates(
        IReadOnlyList<string> deletedFilePaths,
        IReadOnlyList<(string FilePath, Exception Exception)> errors)
    {
        auditLogService.LogHeader("CLEANUP RESULTS");

        if (deletedFilePaths.Any())
        {
            auditLogService.LogLine("\n[Deleted Successfully]");
            auditLogService.LogBulletPoints(
                deletedFilePaths.Select(path => $"DELETED: {path}"));
        }

        if (errors.Any())
        {
            auditLogService.LogLine("\n[Deletion Failures]");
            auditLogService.LogBulletPoints(
                errors.Select(err => $"ERROR:   {err.FilePath} -> Reason: {err.Exception.Message}"));
        }

        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
        auditLogService.Flush();
    }

    private void LogError(string message, Exception ex)
    {
        auditLogService.LogError(message, ex);
        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
        auditLogService.Flush();
    }
}