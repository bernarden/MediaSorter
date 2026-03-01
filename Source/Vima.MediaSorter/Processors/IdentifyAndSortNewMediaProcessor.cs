using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter.Processors;

public class IdentifyAndSortNewMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IMediaSortingService mediaSortingService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IFileSystem fileSystem,
    IOutputService outputService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.IdentifyAndSortNewMedia;

    public void Process()
    {
        outputService.Start("Identify and sort new media");

        try
        {
            OutputConfiguration();

            outputService.Header("[Step 1/2] Identification");

            var directoryStructure = outputService.ExecuteWithProgress(
                "Identifying directories",
                directoryIdentifingService.Identify
            );

            List<string> directoriesToScan = [options.Value.Directory];
            if (directoryStructure.UnsortedFolders.Count > 0)
            {
                string question =
                    $"Action: Include {directoryStructure.UnsortedFolders.Count} unsorted sub-folder(s) in scan?";
                if (outputService.Confirm(question))
                {
                    directoriesToScan.AddRange(directoryStructure.UnsortedFolders);
                }
            }

            var identified = outputService.ExecuteWithProgress(
                "Identifying media files",
                p => mediaIdentifyingService.Identify(directoriesToScan, p)
            );

            var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
                identified.MediaFilesWithDates,
                identified.UnsupportedFiles
            );

            timeZoneAdjusterService.ApplyOffsetsIfNeeded(identified.MediaFilesWithDates);

            ReportIdentificationResults(directoryStructure, identified, associated);

            outputService.Header("[Step 2/2] Sorting");

            int totalFilesToMove =
                identified.MediaFilesWithDates.Count + associated.AssociatedFiles.Count;
            var targetFolderCount = identified
                .MediaFilesWithDates.Select(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .Distinct()
                .Count();

            if (totalFilesToMove == 0)
            {
                outputService.WriteLine("  No new media files found to sort.");
                outputService.WriteLine();
                outputService.WriteLine(MediaSorterConstants.Separator);
                outputService.WriteLine();
                return;
            }

            if (
                !outputService.Confirm(
                    $"Action: Sort {totalFilesToMove} file(s) into {targetFolderCount} date folder(s)?"
                )
            )
            {
                outputService.WriteLine("  Operation aborted.");
                outputService.WriteLine();
                outputService.WriteLine(MediaSorterConstants.Separator);
                outputService.WriteLine();
                return;
            }

            var sortingResult = outputService.ExecuteWithProgress(
                "  Sorting media",
                p =>
                    mediaSortingService.Sort(
                        identified.MediaFilesWithDates,
                        directoryStructure.DateToExistingDirectoryMapping,
                        p
                    )
            );
            if (sortingResult.Moved.Count > 0)
            {
                outputService.WriteLine($"  Result: {sortingResult.Moved.Count} files moved.");
            }
            else if (sortingResult.Duplicates.Count > 0)
            {
                outputService.WriteLine("  Result: All files are already organised.");
            }

            if (sortingResult.Errors.Count > 0)
            {
                outputService.List(
                    "Sorting Errors:",
                    sortingResult
                        .Errors.OrderByPath(x => x.SourcePath)
                        .Select(e =>
                            $"{fileSystem.GetRelativePath(e.SourcePath)}: {e.Exception.Message}"
                        ),
                    OutputLevel.Error
                );
            }
            outputService.WriteLine();

            LogSorting(sortingResult);

            HandleDuplicates(sortingResult.Duplicates);

            outputService.Complete();
        }
        catch (Exception ex)
        {
            outputService.Fatal("A critical error occurred during processing.", ex);
        }
    }

    private void OutputConfiguration()
    {
        var allExtensions = mediaFileHandlers
            .SelectMany(h => h.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e);

        outputService.Table(
            "Configuration:",
            [
                new("Directory:", options.Value.Directory),
                new("Log file:", outputService.LogFileName),
                new("Extensions:", string.Join(", ", allExtensions)),
                new("Folder format:", options.Value.FolderNameFormat),
            ]
        );
    }

    private void ReportIdentificationResults(
        DirectoryStructure directoryStructure,
        IdentifiedMedia identified,
        AssociatedMedia associated
    )
    {
        var readyCount = identified.MediaFilesWithDates.Count;
        var sidecarCount = associated.AssociatedFiles.Count;
        var missingDateCount = identified.MediaFilesWithoutDates.Count;
        var identificationErrorCount = identified.ErroredFiles.Count;
        var unsupportedCount = associated.RemainingIgnoredFiles.Count;

        var alerts = new List<string>();
        if (directoryStructure.DateToIgnoredDirectoriesMapping.Count > 0)
        {
            string dateWord =
                directoryStructure.DateToIgnoredDirectoriesMapping.Count == 1
                    ? "date has"
                    : "dates have";
            alerts.Add(
                $"{directoryStructure.DateToIgnoredDirectoriesMapping.Count} {dateWord} multiple folders mapped."
            );
        }

        if (missingDateCount > 0)
        {
            alerts.Add(
                $"{missingDateCount} file(s) skipped: Supported file type, but no date metadata found."
            );
        }

        outputService.Table(
            "Analysis result:",
            [
                new("New media:", readyCount.ToString()),
                new("Sidecars:", sidecarCount.ToString()),
                new("Missing date:", missingDateCount.ToString()),
                new("Unsupported:", unsupportedCount.ToString()),
                new("Alerts:", alerts.Count.ToString(), alerts.Count > 0),
                new("Errors:", identificationErrorCount.ToString(), identificationErrorCount > 0),
            ]
        );

        if (alerts.Count > 0)
        {
            outputService.List("Alerts:", alerts, OutputLevel.Warn);
            outputService.WriteLine("", OutputLevel.Warn);
        }

        if (identificationErrorCount > 0)
        {
            var errors = identified
                .ErroredFiles.OrderByPath(x => x.FilePath)
                .Select(error =>
                    $"{fileSystem.GetRelativePath(error.FilePath)}: {error.Exception.Message}"
                );
            outputService.List("Errors:", errors, OutputLevel.Error);
            outputService.WriteLine("", OutputLevel.Error);
        }

        if (identified.MediaFilesWithDates.Any())
        {
            outputService.Header("Ready to move", OutputLevel.Debug);
            var groups = identified
                .MediaFilesWithDates.GroupBy(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                outputService.WriteLine($"Folder: {group.Key}", OutputLevel.Debug);
                foreach (var file in group.OrderByPath(f => f.FilePath))
                {
                    string[] paths = [file.FilePath, .. file.RelatedFiles];
                    foreach (var path in paths.OrderByPath(p => p))
                    {
                        outputService.WriteLine(
                            $"  {fileSystem.GetRelativePath(path)}",
                            OutputLevel.Debug
                        );
                    }
                }
                outputService.WriteLine("", OutputLevel.Debug);
            }
        }

        if (identified.MediaFilesWithoutDates.Any())
        {
            outputService.Header("Missing metadata", OutputLevel.Debug);
            foreach (var file in identified.MediaFilesWithoutDates.OrderByPath(f => f.FilePath))
            {
                outputService.WriteLine(
                    $"  {fileSystem.GetRelativePath(file.FilePath)}",
                    OutputLevel.Debug
                );
            }
            outputService.WriteLine("", OutputLevel.Debug);
        }

        if (associated.RemainingIgnoredFiles.Any())
        {
            outputService.Header("Unsupported files", OutputLevel.Debug);
            foreach (var file in associated.RemainingIgnoredFiles.OrderByPath(p => p))
            {
                outputService.WriteLine($"  {fileSystem.GetRelativePath(file)}", OutputLevel.Debug);
            }
            outputService.WriteLine("", OutputLevel.Debug);
        }
    }

    private void HandleDuplicates(IReadOnlyList<DuplicateDetectedFileMove> duplicates)
    {
        if (duplicates.Count == 0)
            return;

        if (!outputService.Confirm($"Action: Delete {duplicates.Count} exact duplicates?"))
        {
            outputService.WriteLine("  Duplicate cleanup aborted.");
            outputService.WriteLine();
            return;
        }

        var deletedFiles = new List<string>();
        var deletionErrors = new List<(string FilePath, string ExceptionMessage)>();

        outputService.ExecuteWithProgress(
            "  Cleaning up duplicates",
            p =>
            {
                for (int i = 0; i < duplicates.Count; i++)
                {
                    var duplicateFile = duplicates[i];
                    try
                    {
                        if (fileSystem.FileExists(duplicateFile.SourcePath))
                        {
                            fileSystem.DeleteFile(duplicateFile.SourcePath);
                            deletedFiles.Add(duplicateFile.SourcePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        deletionErrors.Add((duplicateFile.SourcePath, ex.Message));
                    }
                    p.Report((double)(i + 1) / duplicates.Count);
                }
                return true;
            }
        );

        outputService.WriteLine($"  Result: Deleted {deletedFiles.Count} file(s).");
        outputService.WriteLine();

        if (deletedFiles.Count != 0)
        {
            outputService.Header("Deleted", OutputLevel.Debug);
            foreach (var file in deletedFiles.OrderBy(f => f))
            {
                outputService.WriteLine($"  {fileSystem.GetRelativePath(file)}", OutputLevel.Debug);
            }
            outputService.WriteLine("", OutputLevel.Debug);
        }

        if (deletionErrors.Count > 0)
        {
            outputService.List(
                "Deletion failures:",
                deletionErrors
                    .OrderByPath(x => x.FilePath)
                    .Select(error =>
                        $"{fileSystem.GetRelativePath(error.FilePath)}: {error.ExceptionMessage}"
                    ),
                OutputLevel.Error
            );
            outputService.WriteLine("", OutputLevel.Error);
        }
    }

    private void LogSorting(SortedMedia result)
    {
        if (result.Moved.Any())
        {
            outputService.Header("Successfully moved", OutputLevel.Debug);
            foreach (var m in result.Moved.OrderByPath(m => m.SourcePath))
            {
                outputService.WriteLine(
                    $"  {fileSystem.GetRelativePath(m.SourcePath)} -> {fileSystem.GetRelativePath(m.DestinationPath)}",
                    OutputLevel.Debug
                );
            }
            outputService.WriteLine("", OutputLevel.Debug);
        }

        if (result.Duplicates.Any())
        {
            outputService.Header("Duplicates detected", OutputLevel.Debug);
            foreach (var d in result.Duplicates.OrderByPath(d => d.SourcePath))
            {
                outputService.WriteLine(
                    $"  {fileSystem.GetRelativePath(d.SourcePath)} == {fileSystem.GetRelativePath(d.DestinationPath)}",
                    OutputLevel.Debug
                );
            }
            outputService.WriteLine("", OutputLevel.Debug);
        }
    }
}
