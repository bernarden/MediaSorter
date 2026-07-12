using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter.Processors.RenameSortedMedia;

public interface IRenameSortedMediaPlanService
{
    List<FileMove> GenerateRenamePlan(IReadOnlyCollection<MediaFileWithDate> files);

    void Rename(List<FileMove> plan);
}

public class RenameSortedMediaPlanService(
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IFileSystem fileSystem,
    IOutputService outputService) : IRenameSortedMediaPlanService
{
    public List<FileMove> GenerateRenamePlan(IReadOnlyCollection<MediaFileWithDate> files)
    {
        var plan = new List<FileMove>();
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
                var needsPrecision = items.Count > 1;

                if (needsPrecision)
                {
                    items.ForEach(TryEnhanceMetadata);
                }

                var sorted = items
                    .OrderBy(f => f.CreatedOn.Date.Ticks)
                    .ThenBy(f => Path.GetFileName(f.FilePath))
                    .ToList();

                var lastAssignedTime = DateTime.MinValue;

                foreach (var media in sorted)
                {
                    var targetTime = media.CreatedOn.Date;
                    var format = needsPrecision
                        ? MediaSorterConstants.PrecisionDateFormat
                        : MediaSorterConstants.StandardDateFormat;

                    if (needsPrecision && (targetTime.ToString(format) == lastAssignedTime.ToString(format) || targetTime < lastAssignedTime))
                    {
                        targetTime = lastAssignedTime.AddMilliseconds(1);
                    }

                    lastAssignedTime = targetTime;
                    var newBaseName = targetTime.ToString(format);

                    var allPaths = new List<string> { media.FilePath }.Concat(media.RelatedFiles);
                    foreach (var oldPath in allPaths)
                    {
                        var ext = Path.GetExtension(oldPath);
                        var newFileName = newBaseName + ext;
                        var newPath = Path.Combine(folderGroup.Key!, newFileName);

                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        plan.Add(new FileMove(oldPath, newPath));
                    }
                }
            }
        }

        return plan;
    }

    public void Rename(List<FileMove> plan)
    {
        var moved = new List<SuccessfulFileMove>();
        var errors = new List<ErroredFileMove>();

        outputService.ExecuteWithProgress("  Renaming files", p =>
        {
            for (var i = 0; i < plan.Count; i++)
            {
                var op = plan[i];
                try
                {
                    fileSystem.MoveFile(op.SourcePath, op.DestinationPath);
                    moved.Add(new SuccessfulFileMove(op.SourcePath, op.DestinationPath));
                }
                catch (Exception ex)
                {
                    errors.Add(new ErroredFileMove(op.SourcePath, op.DestinationPath, ex));
                }
                p.Report((double)(i + 1) / plan.Count);
            }
        });

        outputService.WriteLine($"  Result: {moved.Count} files renamed.", OutputLevel.Info);
        outputService.WriteLine(string.Empty, OutputLevel.Info);

        if (moved.Count > 0)
        {
            var renamed = moved
                .OrderByPath(m => m.SourcePath)
                .Select(m => $"{Path.GetFileName(m.SourcePath)} -> {Path.GetFileName(m.DestinationPath)}");
            outputService.Subsection("Successfully renamed", OutputLevel.Debug);
            outputService.List(string.Empty, renamed, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (errors.Count > 0)
        {
            var failedToRename = errors
                .OrderByPath(m => m.SourcePath)
                .Select(e => $"{Path.GetFileName(e.SourcePath)}: {e.Exception.Message}");
            outputService.List("Failed to rename:", failedToRename, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }

    private void TryEnhanceMetadata(MediaFileWithDate file)
    {
        if (file.CreatedOn.Source != CreatedOnSource.FileName)
        {
            return;
        }

        var ext = Path.GetExtension(file.FilePath).ToLowerInvariant();
        var handler = mediaFileHandlers.FirstOrDefault(h => h.CanHandle(ext));
        var highPrecisionDate = handler?.GetCreatedOnDateFromMetadata(file.FilePath);

        if (highPrecisionDate != null)
        {
            file.SetCreatedOn(highPrecisionDate);
        }
    }
}
