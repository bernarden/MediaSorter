using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface IMediaSortingService
{
    List<DuplicateFile> Sort(
        IReadOnlyCollection<MediaFile> files,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping
    );
}
public class MediaSortingService(
    IFileMover fileMover,
    IDirectoryResolver directoryResolver,
    ITimeZoneAdjustingService timeZoneAdjuster,
    MediaSorterSettings settings) : IMediaSortingService
{
    public List<DuplicateFile> Sort(
        IReadOnlyCollection<MediaFile> files,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping)
    {
        if (files == null || files.Count == 0) return [];

        timeZoneAdjuster.ApplyOffsetsIfNeeded(files);

        Console.Write("Sorting your media... ");
        using ProgressBar progress = new();
        var duplicates = new ConcurrentBag<DuplicateFile>();
        var stepLogs = new ConcurrentBag<string>();
        int processedFileCounter = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 25 }, file =>
        {
            ProcessFile(file, dateToExistingDirectoryMapping, duplicates, stepLogs);
            Interlocked.Increment(ref processedFileCounter);
            progress.Report((double)processedFileCounter / files.Count);
        });

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (var log in stepLogs) Console.WriteLine(log);
        return [.. duplicates];
    }

    private void ProcessFile(
        MediaFile file,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping,
        ConcurrentBag<DuplicateFile> duplicates,
        ConcurrentBag<string> stepLogs)
    {
        if (file.CreatedOn == null)
        {
            stepLogs.Add($"  Warning: No creation date detected: {Path.GetRelativePath(settings.Directory, file.FilePath)}");
            return;
        }

        var targetFolderPath = directoryResolver.GetTargetFolderPath(
            file.CreatedOn.Date, file.TargetSubFolder, dateToExistingDirectoryMapping);

        foreach (var filePath in (string[])[file.FilePath, .. file.RelatedFiles])
        {
            try
            {
                if (IsAlreadySorted(filePath, targetFolderPath)) return;

                var (status, destinationPath) = fileMover.Move(filePath, targetFolderPath, Path.GetFileName(filePath));
                if (status == MoveStatus.Duplicate)
                    duplicates.Add(new DuplicateFile(filePath, destinationPath ?? string.Empty));
            }

            catch (Exception e)
            {
                stepLogs.Add($"  Warning: Failed to sort: {Path.GetRelativePath(settings.Directory, file.FilePath)}. Error: {e.Message}");
            }
        }
    }

    private bool IsAlreadySorted(string filePath, string targetFolderPath)
    {
        var parent = Directory.GetParent(filePath);
        return filePath.StartsWith(targetFolderPath, StringComparison.OrdinalIgnoreCase) ||
            directoryResolver.GetDateFromDirectoryName(parent?.Name ?? "") != null ||
            directoryResolver.GetDateFromDirectoryName(parent?.Parent?.Name ?? "") != null;
    }
}