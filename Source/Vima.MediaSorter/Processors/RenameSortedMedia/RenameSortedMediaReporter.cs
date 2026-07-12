using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter.Processors.RenameSortedMedia;

public interface IRenameSortedMediaReporter
{
    void ReportConfiguration();

    void ReportAnalysisResults(
        IdentifiedMedia identified,
        AssociatedMedia associated,
        List<FileMove> plan,
        int folderCount,
        int timeZoneAdjusted);
}

public class RenameSortedMediaReporter(
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IOutputService outputService,
    IFileSystem fileSystem,
    IOptions<MediaSorterOptions> options) : IRenameSortedMediaReporter
{
    public void ReportConfiguration()
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
                new("Default format:", MediaSorterConstants.StandardDateFormat),
                new("Burst format:", MediaSorterConstants.PrecisionDateFormat),
            ],
            OutputLevel.Info
        );
    }

    public void ReportAnalysisResults(
        IdentifiedMedia identified,
        AssociatedMedia associated,
        List<FileMove> plan,
        int folderCount,
        int timeZoneAdjusted)
    {
        outputService.Table(
            "Analysis result:",
            [
                new("Sorted folders:", folderCount.ToString()),
                new("Media found:", identified.MediaFilesWithDates.Count.ToString()),
                new("Sidecars linked:", identified.MediaFilesWithDates.Sum(f => f.RelatedFiles.Count).ToString()),
                new("Unsupported:", associated.RemainingIgnoredFiles.Count.ToString()),
                new("Files to rename:", plan.Count.ToString()),
                new("TZ adjusted:", timeZoneAdjusted.ToString(), timeZoneAdjusted > 0),
                new("Errors:", identified.ErroredFiles.Count.ToString(), identified.ErroredFiles.Any()),
            ],
            OutputLevel.Info
        );

        if (identified.ErroredFiles.Any())
        {
            var errors = identified
                .ErroredFiles.OrderBy(e => e.Path)
                .Select(e => $"{fileSystem.GetRelativePath(e.Path)}: {e.Exception.Message}");
            outputService.List("Errors:", errors, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }

        if (plan.Count != 0)
        {
            outputService.Subsection("Proposed rename plan", OutputLevel.Debug);
            var folderGroups = plan.GroupBy(p => Path.GetDirectoryName(p.SourcePath))
                .OrderBy(g => g.Key);
            foreach (var folderGroup in folderGroups)
            {
                var files = folderGroup
                    .OrderBy(p => p.SourcePath)
                    .Select(p => $"{Path.GetFileName(p.SourcePath)} -> {Path.GetFileName(p.DestinationPath)}");
                outputService.List(
                    $"Folder {fileSystem.GetRelativePath(folderGroup.Key)}:",
                    files,
                    OutputLevel.Debug
                );
                outputService.WriteLine(string.Empty, OutputLevel.Debug);
            }
        }
    }
}