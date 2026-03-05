using System;
using System.Collections.Generic;
using System.IO;
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

            outputService.Section("[Step 1/2] Identification");

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

            outputService.Section("[Step 2/2] Sorting");

            int totalFilesToMove =
                identified.MediaFilesWithDates.Count + associated.AssociatedFiles.Count;
            var targetFolderCount = identified
                .MediaFilesWithDates.Select(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .Distinct()
                .Count();

            if (totalFilesToMove == 0)
            {
                outputService.Complete("  No new media files found to sort.");
                return;
            }

            if (
                !outputService.Confirm(
                    $"Action: Sort {totalFilesToMove} file(s) into {targetFolderCount} date folder(s)?"
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
                outputService.WriteLine($"  Result: {sortingResult.Moved.Count} files moved.");
                outputService.WriteLine();
            }
            else if (sortingResult.Duplicates.Count > 0)
            {
                outputService.WriteLine("  Result: All files are already organised.");
                outputService.WriteLine();
            }

            LogSorting(sortingResult);

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
            outputService.List("Alerts:", alerts, OutputLevel.Warn, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Warn);
        }

        if (identificationErrorCount > 0)
        {
            var errors = identified
                .ErroredFiles.OrderByPath(x => x.FilePath)
                .Select(error =>
                    $"{fileSystem.GetRelativePath(error.FilePath)}: {error.Exception.Message}"
                );
            outputService.List("Errors:", errors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }

        if (identified.MediaFilesWithDates.Any())
        {
            outputService.Subsection("Ready to move", OutputLevel.Debug);
            var groups = identified
                .MediaFilesWithDates.GroupBy(f => f.CreatedOn.Date.ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var files = group
                    .OrderByPath(f => f.FilePath)
                    .SelectMany(file => (string[])[file.FilePath, .. file.RelatedFiles])
                    .Select(path => fileSystem.GetRelativePath(path));
                outputService.List($"Folder {group.Key}:", files, OutputLevel.Debug);
                outputService.WriteLine(string.Empty, OutputLevel.Debug);
            }
        }

        if (identified.MediaFilesWithoutDates.Any())
        {
            var files = identified
                .MediaFilesWithoutDates.OrderByPath(f => f.FilePath)
                .Select(f => fileSystem.GetRelativePath(f.FilePath));
            outputService.Subsection("Missing metadata", OutputLevel.Debug);
            outputService.List(string.Empty, files, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (associated.RemainingIgnoredFiles.Any())
        {
            var files = associated
                .RemainingIgnoredFiles.OrderByPath(p => p)
                .Select(p => fileSystem.GetRelativePath(p));
            outputService.Subsection("Unsupported files", OutputLevel.Debug);
            outputService.List(string.Empty, files, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
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
            var files = deletedFiles.OrderBy(f => f).Select(f => fileSystem.GetRelativePath(f));
            outputService.Subsection("Deleted", OutputLevel.Debug);
            outputService.List(string.Empty, files, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (deletionErrors.Count > 0)
        {
            IEnumerable<string> errors = deletionErrors
                .OrderByPath(x => x.FilePath)
                .Select(error =>
                    $"{fileSystem.GetRelativePath(error.FilePath)}: {error.ExceptionMessage}"
                );
            outputService.List("Deletion failures:", errors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }

    private void CleanUpEmptyFolders(IEnumerable<string> affectedFolders)
    {
        string rootDir =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.Directory))
            + Path.DirectorySeparatorChar;

        var processingQueue = new PriorityQueue<string, int>();

        foreach (var path in affectedFolders)
        {
            string fullPath = Path.GetFullPath(path);
            if (
                fullPath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase)
                && fullPath.Length > rootDir.Length
            )
            {
                processingQueue.Enqueue(fullPath, -fullPath.Length);
            }
        }

        if (processingQueue.Count == 0)
            return;

        var foldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (processingQueue.TryDequeue(out var current, out _))
        {
            if (!fileSystem.DirectoryExists(current))
                continue;

            bool hasFiles = fileSystem
                .EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly)
                .Any();
            if (hasFiles)
                continue;

            bool hasRemainingSubDirs = fileSystem
                .EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly)
                .Any(sub => !foldersToDelete.Contains(sub));

            if (!hasRemainingSubDirs)
            {
                foldersToDelete.Add(current);

                string? parent = Path.GetDirectoryName(current);
                if (
                    parent != null
                    && parent.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase)
                    && parent.Length >= rootDir.Length
                    && seen.Add(parent)
                )
                {
                    processingQueue.Enqueue(parent, -parent.Length);
                }
            }
        }

        if (foldersToDelete.Count == 0)
            return;

        var foldersToDeleteList = foldersToDelete.OrderByDescending(f => f.Length).ToList();

        outputService.Subsection("Planned deletions", OutputLevel.Debug);
        outputService.List(
            string.Empty,
            foldersToDeleteList.Select(f => fileSystem.GetRelativePath(f)),
            OutputLevel.Debug
        );
        outputService.WriteLine(string.Empty, OutputLevel.Debug);

        if (!outputService.Confirm($"Action: Delete {foldersToDeleteList.Count} empty folder(s)?"))
        {
            outputService.WriteLine("  Folder cleanup aborted.");
            return;
        }

        var deletedFolders = new List<string>();
        var deletionErrors = new List<(string Path, string ExceptionMessage)>();

        outputService.ExecuteWithProgress(
            "  Deleting folders",
            p =>
            {
                for (int i = 0; i < foldersToDeleteList.Count; i++)
                {
                    var folder = foldersToDeleteList[i];
                    try
                    {
                        if (fileSystem.DirectoryExists(folder))
                        {
                            fileSystem.DeleteDirectory(folder);
                            deletedFolders.Add(folder);
                        }
                    }
                    catch (Exception ex)
                    {
                        deletionErrors.Add((folder, ex.Message));
                    }
                    p.Report((double)(i + 1) / foldersToDeleteList.Count);
                }
                return true;
            }
        );

        outputService.WriteLine($"  Result: Deleted {deletedFolders.Count} folder(s).");
        outputService.WriteLine();

        if (deletedFolders.Count > 0)
        {
            var items = deletedFolders
                .OrderByPath(f => f)
                .Select(f => fileSystem.GetRelativePath(f));
            outputService.Subsection("Deleted", OutputLevel.Debug);
            outputService.List(string.Empty, items, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (deletionErrors.Count > 0)
        {
            var errors = deletionErrors
                .OrderByPath(e => e.Path)
                .Select(e => $"{fileSystem.GetRelativePath(e.Path)}: {e.ExceptionMessage}");
            outputService.List("Deletion failures:", errors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }

    private void LogSorting(SortedMedia result)
    {
        if (result.Errors.Count > 0)
        {
            IEnumerable<string> sortingErrors = result
                .Errors.OrderByPath(x => x.SourcePath)
                .Select(e =>
                    $"{fileSystem.GetRelativePath(e.SourcePath)}: {e.Exception.Message}"
                );
            outputService.List("Sorting Errors:", sortingErrors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }

        if (result.Moved.Any())
        {
            var items = result
                .Moved.OrderByPath(m => m.SourcePath)
                .Select(m =>
                    $"{fileSystem.GetRelativePath(m.SourcePath)} -> {fileSystem.GetRelativePath(m.DestinationPath)}"
                );
            outputService.List("Successfully moved:", items, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (result.Duplicates.Any())
        {
            var items = result
                .Duplicates.OrderByPath(d => d.SourcePath)
                .Select(d =>
                    $"{fileSystem.GetRelativePath(d.SourcePath)} == {fileSystem.GetRelativePath(d.DestinationPath)}"
                );
            outputService.List("Duplicates detected:", items, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }
    }
}
