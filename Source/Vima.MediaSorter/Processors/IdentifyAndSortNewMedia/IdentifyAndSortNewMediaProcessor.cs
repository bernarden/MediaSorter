using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors.IdentifyAndSortNewMedia;

public class IdentifyAndSortNewMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IMediaSortingService mediaSortingService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IEmptyFolderCleanupService emptyFolderCleanupService,
    IIdentifyAndSortNewMediaReporter identifyAndSortMediaReporter,
    IFileSystem fileSystem,
    IOutputService outputService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.IdentifyAndSortNewMedia;

    public async Task Process(CancellationToken token = default)
    {
        outputService.Start("Identify and sort new media");

        try
        {
            identifyAndSortMediaReporter.ReportConfiguration();

            outputService.Section("[Step 1/2] Identification", OutputLevel.Info);

            var directoryStructure = outputService.ExecuteWithProgress(
                "Identifying directories",
                directoryIdentifingService.Identify
            );

            List<string> directoriesToScan = [options.Value.Directory];
            if (directoryStructure.UnsortedFolders.Count > 0)
            {
                string question =
                    $"Action: Include {directoryStructure.UnsortedFolders.Count} unsorted sub-folder(s) in scan?";
                if (outputService.Confirm(question, OutputLevel.Info))
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

            identifyAndSortMediaReporter.ReportIdentification(
                directoryStructure,
                identified,
                associated
            );

            outputService.Section("[Step 2/2] Sorting", OutputLevel.Info);

            int totalFilesToMove =
                identified.MediaFilesWithDates.Count + associated.AssociatedFiles.Count;
            var targetFolderCount = identified
                .MediaFilesWithDates.Select(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .Distinct()
                .Count();

            if (totalFilesToMove == 0)
            {
                outputService.Complete("No new media files found to sort.");
                return;
            }

            if (
                !outputService.Confirm(
                    $"Action: Sort {totalFilesToMove} file(s) into {targetFolderCount} date folder(s)?",
                    OutputLevel.Info
                )
            )
            {
                outputService.Complete("  Operation aborted.");
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
                outputService.WriteLine(
                    $"  Result: {sortingResult.Moved.Count} files moved.",
                    OutputLevel.Info
                );
                outputService.WriteLine(string.Empty, OutputLevel.Info);
            }
            else if (sortingResult.Duplicates.Count > 0)
            {
                outputService.WriteLine(
                    "  Result: All files are already organised.",
                    OutputLevel.Info
                );
                outputService.WriteLine(string.Empty, OutputLevel.Info);
            }

            identifyAndSortMediaReporter.ReportSortingResults(sortingResult);

            HandleDuplicates(sortingResult.Duplicates);

            List<string> affectedFolders = sortingResult
                .Moved.Select(m => Path.GetDirectoryName(m.SourcePath))
                .Concat(sortingResult.Duplicates.Select(d => Path.GetDirectoryName(d.SourcePath)))
                .Where(path => path != null && !string.IsNullOrEmpty(path))
                .Cast<string>()
                .Distinct()
                .ToList();

            if (affectedFolders.Count != 0)
            {
                CleanUpEmptyFolders(affectedFolders);
            }

            outputService.Complete();
        }
        catch (Exception ex)
        {
            outputService.Fatal("A critical error occurred during processing.", ex);
        }
    }

    private void HandleDuplicates(IReadOnlyList<DuplicateDetectedFileMove> duplicates)
    {
        if (duplicates.Count == 0)
            return;

        string question = $"Action: Delete {duplicates.Count} exact duplicates?";
        if (!outputService.Confirm(question, OutputLevel.Info))
        {
            outputService.WriteLine("  Duplicate cleanup aborted.", OutputLevel.Info);
            outputService.WriteLine(string.Empty, OutputLevel.Info);
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
            }
        );

        identifyAndSortMediaReporter.ReportDuplicateFileDeletionResults(
            deletedFiles,
            deletionErrors
        );
    }

    private void CleanUpEmptyFolders(IEnumerable<string> affectedFolders)
    {
        var foldersToDelete = emptyFolderCleanupService.GetFoldersForDeletion(affectedFolders);
        if (foldersToDelete.Count == 0)
            return;

        outputService.Subsection("Planned deletions", OutputLevel.Debug);
        outputService.List(
            string.Empty,
            foldersToDelete.Select(f => fileSystem.GetRelativePath(f)),
            OutputLevel.Debug
        );
        outputService.WriteLine(string.Empty, OutputLevel.Debug);

        string question = $"Action: Delete {foldersToDelete.Count} empty folder(s)?";
        if (!outputService.Confirm(question, OutputLevel.Info))
        {
            outputService.WriteLine("  Folder cleanup aborted.", OutputLevel.Info);
            return;
        }

        var result = outputService.ExecuteWithProgress(
            "  Deleting folders",
            p => emptyFolderCleanupService.DeleteFolders(foldersToDelete, p)
        );
        identifyAndSortMediaReporter.ReportEmptyFolderDeletionResults(result);
    }
}
