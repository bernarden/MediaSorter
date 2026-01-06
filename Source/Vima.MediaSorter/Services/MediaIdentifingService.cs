using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter.Services;

public class MediaIdentifingService
{
    private readonly MediaSorterSettings _settings;

    public MediaIdentifingService(MediaSorterSettings settings)
    {
        _settings = settings;
    }

    public MediaIdentificationResult Identify()
    {
        Console.Write("Identifying your media... ");
        using ProgressBar progress = new();
        ConcurrentBag<string> stepLogs = new();

        var dateToExistingDirectoryMapping = new Dictionary<DateTime, string>();
        string[] directoryPaths = Directory.GetDirectories(_settings.Directory);
        foreach (string directoryPath in directoryPaths)
        {
            string directoryName = new DirectoryInfo(directoryPath).Name;
            DateTime? directoryDate = GetDirectoryDateFromPath(directoryName);
            if (directoryDate == null) continue;

            if (dateToExistingDirectoryMapping.TryGetValue(directoryDate.Value.Date, out string? existingDirectoryName))
            {
                stepLogs.Add(
                    $"\tWarning: Multiple directories mapped to date: '{directoryDate.Value.ToShortDateString()}'. Only '{existingDirectoryName}' is going to be used.");
                continue;
            }

            dateToExistingDirectoryMapping.Add(directoryDate.Value.Date, directoryName);
        }

        // Get all file paths from source and children directories.
        IEnumerable<string> directoriesToScan = new List<string> { _settings.Directory }.Concat(directoryPaths);
        List<string> filePaths = new();
        foreach (string directoryToScan in directoriesToScan)
        {
            filePaths.AddRange(Directory.GetFiles(directoryToScan));
        }

        ConcurrentBag<MediaFile> mediaFiles = new();
        var ignoredFiles = new List<string>();
        int processedFileCounter = 0;
        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (MediaFileProcessor.ImageExtensions.Contains(ext))
            {
                MediaFile mediaFile = new(filePath, MediaFileType.Image);
                MediaMetadataHelper.SetCreatedDateTime(mediaFile);
                mediaFiles.Add(mediaFile);
            }
            else if (MediaFileProcessor.VideoExtensions.Contains(ext))
            {
                MediaFile mediaFile = new(filePath, MediaFileType.Video);
                IEnumerable<string> relatedFiles = RelatedFilesHelper.FindAll(filePath);
                mediaFile.RelatedFiles.AddRange(relatedFiles);
                MediaMetadataHelper.SetCreatedDateTime(mediaFile);
                mediaFiles.Add(mediaFile);
            }
            else
            {
                ignoredFiles.Add(filePath);
            }

            Interlocked.Increment(ref processedFileCounter);
            progress.Report((double)processedFileCounter / filePaths.Count);
        });

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (string stepLog in stepLogs)
            Console.WriteLine(stepLog);

        return new MediaIdentificationResult
        {
            MediaFiles = [.. mediaFiles],
            ExistingDirectoryMapping = dateToExistingDirectoryMapping,
            IgnoredFiles = ignoredFiles
        };
    }

    private static DateTime? GetDirectoryDateFromPath(string directoryName)
    {
        if (directoryName.Length < MediaFileProcessor.FolderNameFormat.Length) return null;

        string directoryNameBeginning = directoryName[..MediaFileProcessor.FolderNameFormat.Length];
        return DateTime.TryParseExact(directoryNameBeginning, MediaFileProcessor.FolderNameFormat,
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime result)
            ? result
            : null;
    }
}
