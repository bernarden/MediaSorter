using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter.Processors.IdentifyAndSortNewMedia;

public interface IIdentifyAndSortNewMediaReporter
{
    void ReportConfiguration();
    void ReportIdentification(
        DirectoryStructure directoryStructure,
        IdentifiedMedia identified,
        AssociatedMedia associated
    );
    void ReportSortingResults(SortedMedia result);
    void ReportDuplicateFileDeletionResults(
        IEnumerable<string> deletedFiles,
        IEnumerable<PathError> deletionErrors
    );
    void ReportEmptyFolderDeletionResults(
        IEnumerable<string> deletedFolders,
        IEnumerable<PathError> deletionErrors
    );
}

public class IdentifyAndSortNewMediaReporter(
    IOutputService outputService,
    IFileSystem fileSystem,
    IEnumerable<IMediaFileHandler> handlers,
    IOptions<MediaSorterOptions> options
) : IIdentifyAndSortNewMediaReporter
{
    public void ReportConfiguration()
    {
        var allExtensions = handlers
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
            ],
            OutputLevel.Info
        );
    }

    public void ReportIdentification(
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
            ],
            OutputLevel.Info
        );

        if (alerts.Count > 0)
        {
            outputService.List("Alerts:", alerts, OutputLevel.Warn, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Warn);
        }

        if (identificationErrorCount > 0)
        {
            var errors = identified
                .ErroredFiles.OrderByPath(x => x.Path)
                .Select(error =>
                    $"{fileSystem.GetRelativePath(error.Path)}: {error.Exception.Message}"
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

    public void ReportSortingResults(SortedMedia result)
    {
        if (result.Errors.Count > 0)
        {
            IEnumerable<string> sortingErrors = result
                .Errors.OrderByPath(x => x.SourcePath)
                .Select(e => $"{fileSystem.GetRelativePath(e.SourcePath)}: {e.Exception.Message}");
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

    public void ReportDuplicateFileDeletionResults(
        IEnumerable<string> deletedFiles,
        IEnumerable<PathError> deletionErrors
    )
    {
        outputService.WriteLine(
            $"  Result: Deleted {deletedFiles.Count()} file(s).",
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);

        if (deletedFiles.Any())
        {
            var files = deletedFiles.OrderBy(f => f).Select(f => fileSystem.GetRelativePath(f));
            outputService.Subsection("Deleted", OutputLevel.Debug);
            outputService.List(string.Empty, files, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (deletionErrors.Any())
        {
            IEnumerable<string> errors = deletionErrors
                .OrderByPath(x => x.Path)
                .Select(error =>
                    $"{fileSystem.GetRelativePath(error.Path)}: {error.Exception.Message}"
                );
            outputService.List("Deletion failures:", errors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }

    public void ReportEmptyFolderDeletionResults(
        IEnumerable<string> deletedFolders,
        IEnumerable<PathError> deletionErrors
    )
    {
        outputService.WriteLine(
            $"  Result: Deleted {deletedFolders.Count()} folder(s).",
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);

        if (deletedFolders.Any())
        {
            var items = deletedFolders
                .OrderByPath(f => f)
                .Select(f => fileSystem.GetRelativePath(f));
            outputService.Subsection("Deleted", OutputLevel.Debug);
            outputService.List(string.Empty, items, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (deletionErrors.Any())
        {
            var errors = deletionErrors
                .OrderByPath(e => e.Path)
                .Select(e => $"{fileSystem.GetRelativePath(e.Path)}: {e.Exception.Message}");
            outputService.List("Deletion failures:", errors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }
}
