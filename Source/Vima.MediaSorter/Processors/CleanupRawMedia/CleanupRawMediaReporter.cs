using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors.CleanupRawMedia;

public interface ICleanupRawMediaReporter
{
    void ReportAnalysisResults(
        DirectoryStructure structure,
        IDictionary<string, List<string>> plan
    );
    void ReportConfiguration(IEnumerable<string> rawExtensions);
    void ReportDeletionResults(
        IEnumerable<string> deletedFiles,
        IEnumerable<PathError> deletionErrors
    );
}

public class CleanupRawMediaReporter(
    IOutputService outputService,
    IFileSystem fileSystem,
    IOptions<MediaSorterOptions> options
) : ICleanupRawMediaReporter
{
    public void ReportConfiguration(IEnumerable<string> rawExtensions)
    {
        outputService.Table(
            "Configuration:",
            [
                new("Directory:", options.Value.Directory),
                new("Log file:", outputService.LogFileName),
                new("Raw folder:", MediaSorterConstants.RawFolderName),
                new("Raw extensions:", string.Join(", ", rawExtensions)),
            ],
            OutputLevel.Info
        );
    }

    public void ReportAnalysisResults(
        DirectoryStructure structure,
        IDictionary<string, List<string>> plan
    )
    {
        int orphanedCount = plan.Sum(p => p.Value.Count);
        outputService.Table(
            "Analysis result:",
            [
                new("Folders checked:", structure.SortedFolders.Count.ToString()),
                new("Orphaned RAWs:", orphanedCount.ToString()),
            ],
            OutputLevel.Info
        );

        if (plan.Count != 0)
        {
            outputService.Subsection("Proposed deletion plan", OutputLevel.Debug);
            foreach (var entry in plan.OrderBy(e => e.Key))
            {
                var files = entry
                    .Value.OrderByPath(x => x)
                    .Select(x => fileSystem.GetRelativePath(x, entry.Key));
                outputService.List(
                    $"Folder {fileSystem.GetRelativePath(entry.Key)}:",
                    files,
                    OutputLevel.Debug
                );
                outputService.WriteLine(string.Empty, OutputLevel.Debug);
            }
        }
    }

    public void ReportDeletionResults(
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
            var items = deletedFiles.OrderByPath(f => f).Select(f => fileSystem.GetRelativePath(f));
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
