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

public class RenameSortedMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IOutputService outputService,
    IFileSystem fileSystem,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.RenameSortedMedia;

    public void Process()
    {
        outputService.Start("Rename media in sorted folders");

        try
        {
            OutputConfiguration();

            outputService.Header("[Step 1/2] Identification");

            var directoryStructure = outputService.ExecuteWithProgress(
                "Identifying directories",
                directoryIdentifingService.Identify
            );
            if (directoryStructure.SortedFolders.Count == 0)
            {
                outputService.WriteLine("  No sorted folders found to process.");
                outputService.WriteLine();
                return;
            }

            var foldersToScan = directoryStructure
                .SortedFolders.Concat(directoryStructure.SortedSubFolders)
                .ToList();
            var identified = outputService.ExecuteWithProgress(
                "Identifying media files",
                p => mediaIdentifyingService.Identify(foldersToScan, p)
            );

            var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
                identified.MediaFilesWithDates,
                identified.UnsupportedFiles
            );

            var timeZoneAdjustedFilePaths = timeZoneAdjusterService.ApplyOffsetsIfNeeded(
                identified.MediaFilesWithDates
            );

            var renamePlan = GenerateRenamePlan(identified.MediaFilesWithDates);

            ReportAnalysisResults(
                identified,
                associated,
                renamePlan,
                directoryStructure.SortedFolders.Count,
                timeZoneAdjustedFilePaths.Count
            );

            outputService.Header("[Step 2/2] Renaming");

            if (renamePlan.Count == 0)
            {
                outputService.WriteLine("  All files already match the naming convention.");
                outputService.WriteLine();
                outputService.WriteLine(MediaSorterConstants.Separator);
                outputService.WriteLine();
                return;
            }

            if (!outputService.Confirm($"Action: Rename {renamePlan.Count} file(s)?"))
            {
                outputService.WriteLine("  Operation aborted.");
                outputService.WriteLine();
                outputService.WriteLine(MediaSorterConstants.Separator);
                outputService.WriteLine();
                return;
            }

            ExecuteRenaming(renamePlan);

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
                new("Default format:", MediaSorterConstants.StandardDateFormat),
                new("Burst format:", MediaSorterConstants.PrecisionDateFormat),
            ]
        );
    }

    private void ReportAnalysisResults(
        IdentifiedMedia identified,
        AssociatedMedia associated,
        List<(string Source, string Destination)> plan,
        int folderCount,
        int timeZoneAdjusted
    )
    {
        outputService.Table(
            "Analysis result:",
            [
                new("Sorted folders:", folderCount.ToString()),
                new("Media found:", identified.MediaFilesWithDates.Count.ToString()),
                new(
                    "Sidecars linked:",
                    identified.MediaFilesWithDates.Sum(f => f.RelatedFiles.Count).ToString()
                ),
                new("Unsupported:", associated.RemainingIgnoredFiles.Count.ToString()),
                new("Files to rename:", plan.Count.ToString()),
                new("TZ adjusted:", timeZoneAdjusted.ToString(), timeZoneAdjusted > 0),
                new(
                    "Errors:",
                    identified.ErroredFiles.Count.ToString(),
                    identified.ErroredFiles.Any()
                ),
            ]
        );

        if (identified.ErroredFiles.Any())
        {
            var errors = identified
                .ErroredFiles.OrderBy(e => e.FilePath)
                .Select(e => $"{fileSystem.GetRelativePath(e.FilePath)}: {e.Exception.Message}");
            outputService.List("Errors:", errors, OutputLevel.Error);
            outputService.WriteLine("", OutputLevel.Error);
        }

        if (plan.Count != 0)
        {
            outputService.Header("Proposed rename plan", OutputLevel.Debug);
            var folderGroups = plan.GroupBy(p => Path.GetDirectoryName(p.Source))
                .OrderBy(g => g.Key);
            foreach (var folderGroup in folderGroups)
            {
                outputService.WriteLine(
                    $"Folder: {fileSystem.GetRelativePath(folderGroup.Key)}",
                    OutputLevel.Debug
                );
                foreach (var (Source, Destination) in folderGroup.OrderBy(p => p.Source))
                {
                    outputService.WriteLine(
                        $"  {Path.GetFileName(Source)} -> {Path.GetFileName(Destination)}",
                        OutputLevel.Debug
                    );
                }
                outputService.WriteLine("", OutputLevel.Debug);
            }
        }
    }

    private List<(string Source, string Destination)> GenerateRenamePlan(
        IReadOnlyCollection<MediaFileWithDate> files
    )
    {
        var plan = new List<(string Source, string Destination)>();

        var folderGroups = files
            .GroupBy(f => Path.GetDirectoryName(f.FilePath))
            .OrderBy(g => g.Key);

        foreach (var folderGroup in folderGroups)
        {
            var secondGroups = folderGroup
                .GroupBy(f => f.CreatedOn.Date.ToString(MediaSorterConstants.StandardDateFormat))
                .OrderBy(g => g.Key);

            foreach (var secondGroup in secondGroups)
            {
                var items = secondGroup.ToList();
                bool needsPrecision = items.Count > 1;

                if (needsPrecision)
                    items.ForEach(TryEnhanceMetadata);

                var sorted = items
                    .OrderBy(f => f.CreatedOn.Date.Ticks)
                    .ThenBy(f => Path.GetFileName(f.FilePath))
                    .ToList();

                DateTime lastAssignedTime = DateTime.MinValue;

                foreach (var media in sorted)
                {
                    DateTime targetTime = media.CreatedOn.Date;
                    string format = needsPrecision
                        ? MediaSorterConstants.PrecisionDateFormat
                        : MediaSorterConstants.StandardDateFormat;

                    if (
                        needsPrecision
                        && (
                            targetTime.ToString(format) == lastAssignedTime.ToString(format)
                            || targetTime < lastAssignedTime
                        )
                    )
                    {
                        targetTime = lastAssignedTime.AddMilliseconds(1);
                    }

                    lastAssignedTime = targetTime;
                    string newBaseName = targetTime.ToString(format);

                    var allPaths = new List<string> { media.FilePath }.Concat(media.RelatedFiles);
                    foreach (var oldPath in allPaths)
                    {
                        string ext = Path.GetExtension(oldPath);
                        string newFileName = newBaseName + ext;
                        string newPath = Path.Combine(folderGroup.Key!, newFileName);

                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        plan.Add((oldPath, newPath));
                    }
                }
            }
        }

        return plan;
    }

    private void ExecuteRenaming(List<(string Source, string Destination)> plan)
    {
        var moved = new List<(string Source, string Destination)>();
        var errors = new List<(string Source, string Destination, Exception Exception)>();

        outputService.ExecuteWithProgress(
            "  Renaming files",
            p =>
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    var op = plan[i];
                    try
                    {
                        fileSystem.Move(op.Source, op.Destination);
                        moved.Add(op);
                    }
                    catch (Exception ex)
                    {
                        errors.Add((op.Source, op.Destination, ex));
                    }
                    p.Report((double)(i + 1) / plan.Count);
                }
                return true;
            }
        );
        outputService.WriteLine($"  Result: {moved.Count} files renamed.");
        outputService.WriteLine();

        if (moved.Count > 0)
        {
            outputService.List(
                "Successfully renamed:",
                moved
                    .OrderByPath(m => m.Source)
                    .Select(m =>
                        $"  {Path.GetFileName(m.Source)} -> {Path.GetFileName(m.Destination)}"
                    ),
                OutputLevel.Debug
            );
            outputService.WriteLine("", OutputLevel.Debug);
        }

        if (errors.Count > 0)
        {
            outputService.List(
                "Failed to rename:",
                errors
                    .OrderByPath(m => m.Source)
                    .Select(e => $"{Path.GetFileName(e.Source)}: {e.Exception.Message}"),
                OutputLevel.Error
            );
            outputService.WriteLine("", OutputLevel.Error);
        }
    }

    private void TryEnhanceMetadata(MediaFileWithDate file)
    {
        if (file.CreatedOn.Source != CreatedOnSource.FileName)
            return;

        var ext = Path.GetExtension(file.FilePath).ToLowerInvariant();
        var handler = mediaFileHandlers.FirstOrDefault(h => h.CanHandle(ext));
        var highPrecisionDate = handler?.GetCreatedOnDateFromMetadata(file.FilePath);
        if (highPrecisionDate != null)
            file.SetCreatedOn(highPrecisionDate);
    }
}
