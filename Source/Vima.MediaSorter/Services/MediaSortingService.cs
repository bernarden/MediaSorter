using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter.Services;

public interface IMediaSortingService
{
    List<DuplicateFile> Sort(
        IReadOnlyCollection<MediaFile> files,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping
    );
}

public class MediaSortingService(MediaSorterSettings settings) : IMediaSortingService
{
    public List<DuplicateFile> Sort(
        IReadOnlyCollection<MediaFile> files,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping
    )
    {
        var duplicates = new ConcurrentBag<DuplicateFile>();
        if (files == null || files.Count == 0) return duplicates.ToList();

        var mediaFilesToApplyOffset = files.Where(x => x.CreatedOn != null && x.CreatedOn.Source == CreatedOnSource.MetadataUtc).ToList();
        if (mediaFilesToApplyOffset.Any())
        {
            TimeSpan utcOffset = ConsoleHelper.GetVideoUtcOffsetFromUser();
            foreach (var mf in mediaFilesToApplyOffset)
            {
                mf.SetCreatedOn(new CreatedOn(mf.CreatedOn!.Date + utcOffset, CreatedOnSource.MetadataLocal));
            }
        }

        Console.Write("Sorting your media... ");
        using ProgressBar progress = new();
        var stepLogs = new ConcurrentBag<string>();
        int processedFileCounter = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 25 }, file =>
        {
            try
            {
                if (file.CreatedOn == null)
                {
                    stepLogs.Add($"\tWarning: No creation date detected: {file.FilePath}");
                    return;
                }

                MoveFile(file.FilePath, file.CreatedOn.Date, file.TargetSubFolder, dateToExistingDirectoryMapping, duplicates);

                foreach (string relatedFilePath in file.RelatedFiles)
                {
                    MoveFile(relatedFilePath, file.CreatedOn.Date, file.TargetSubFolder, dateToExistingDirectoryMapping, duplicates);
                }
            }
            catch (Exception e)
            {
                stepLogs.Add($"\tWarning: Failed to sort: {file.FilePath}. Error: {e.Message}");
            }

            Interlocked.Increment(ref processedFileCounter);
            progress.Report((double)processedFileCounter / files.Count);
        });

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (var log in stepLogs) Console.WriteLine(log);
        return [.. duplicates];
    }

    private void MoveFile(string filePath, DateTime createdDateTime, string? targetSubFolder, IDictionary<DateTime, string> dateToExistingDirectoryMapping, ConcurrentBag<DuplicateFile> duplicates)
    {
        if (!dateToExistingDirectoryMapping.TryGetValue(createdDateTime.Date, out string? directoryName))
        {
            directoryName = createdDateTime.ToString(settings.FolderNameFormat, CultureInfo.InvariantCulture);
        }

        string destinationFileName = Path.GetFileName(filePath);

        string destinationFolderPath = Path.Combine(settings.Directory, directoryName);
        if (!string.IsNullOrEmpty(targetSubFolder))
        {
            destinationFolderPath = Path.Combine(destinationFolderPath, targetSubFolder);
        }

        if (filePath.StartsWith(destinationFolderPath, StringComparison.OrdinalIgnoreCase)) return;

        // Don't move files that have been previously (manually?) put into a dated folder.
        if (IsAlreadySorted(filePath)) return;

        (FileMovingHelper.MoveStatus status, string? destinationPath) =
            FileMovingHelper.MoveFile(filePath, destinationFolderPath, destinationFileName);

        if (status == FileMovingHelper.MoveStatus.Duplicate)
        {
            duplicates.Add(new DuplicateFile(filePath, destinationPath ?? string.Empty));
        }
    }

    private bool IsAlreadySorted(string filePath)
    {
        var parent = Directory.GetParent(filePath);
        return GetDirectoryDateFromPath(parent?.Name ?? "") != null ||
            GetDirectoryDateFromPath(parent?.Parent?.Name ?? "") != null;
    }

    private DateTime? GetDirectoryDateFromPath(string directoryName)
    {
        if (directoryName.Length < settings.FolderNameFormat.Length) return null;

        string directoryNameBeginning = directoryName.Substring(0, settings.FolderNameFormat.Length);
        return DateTime.TryParseExact(directoryNameBeginning, settings.FolderNameFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result)
            ? result
            : null;
    }
}
