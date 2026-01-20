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

        bool shouldRenameFiles = false;
        var mediaFilesToApplyOffset = files.Where(x => x.CreatedOn != null && x.CreatedOn.Source == CreatedOnSource.MetadataUtc).ToList();
        if (mediaFilesToApplyOffset.Any())
        {
            TimeSpan utcOffset = ConsoleHelper.GetVideoUtcOffsetFromUser();
            ConsoleKey response = ConsoleHelper.AskYesNoQuestion(
                $"Would you like to rename {mediaFilesToApplyOffset.Count} file(s) to yyyymmdd_hhmmss format?",
                ConsoleKey.N);
            shouldRenameFiles = response == ConsoleKey.Y;
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

                bool shouldRenameThisFile = shouldRenameFiles && file.CreatedOn.Source != CreatedOnSource.FileName;
                MoveFile(file.FilePath, file.CreatedOn.Date, shouldRenameThisFile, dateToExistingDirectoryMapping, duplicates);

                foreach (string related in file.RelatedFiles)
                {
                    MoveFile(related, file.CreatedOn.Date, shouldRenameThisFile, dateToExistingDirectoryMapping, duplicates);
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

    private void MoveFile(string filePath, DateTime createdDateTime, bool shouldRenameFile, IDictionary<DateTime, string> dateToExistingDirectoryMapping, ConcurrentBag<DuplicateFile> duplicates)
    {
        if (!dateToExistingDirectoryMapping.TryGetValue(createdDateTime.Date, out string? directoryName))
        {
            directoryName = createdDateTime.ToString(settings.FolderNameFormat, CultureInfo.InvariantCulture);
        }

        string destinationFileName = shouldRenameFile
            ? createdDateTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(filePath).ToLowerInvariant()
            : Path.GetFileName(filePath);

        string destinationFolderPath = Path.Combine(settings.Directory, directoryName);
        if (filePath.StartsWith(destinationFolderPath, StringComparison.OrdinalIgnoreCase) && !shouldRenameFile) return;

        // Don't move files that have been previously (manually?) put into a dated folder.
        string? currentParentDirectoryName = new DirectoryInfo(filePath).Parent?.Name;
        if (currentParentDirectoryName != null && GetDirectoryDateFromPath(currentParentDirectoryName) != null) return;

        (FileMovingHelper.MoveStatus status, string? destinationPath) =
            FileMovingHelper.MoveFile(filePath, destinationFolderPath, destinationFileName);

        if (status == FileMovingHelper.MoveStatus.Duplicate)
        {
            duplicates.Add(new DuplicateFile(filePath, destinationPath ?? string.Empty));
        }
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
