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

namespace Vima.MediaSorter;

public class MediaFileProcessor(string sourceDirectory)
{
    public static readonly string FolderNameFormat = "yyyy_MM_dd -";
    public static readonly List<string> ImageExtensions = [".JPG", ".JPEG"];
    public static readonly List<string> VideoExtensions = [".MP4"];
    private readonly Dictionary<DateTime, string> _dateToExistingDirectoryMapping = [];
    private readonly List<DuplicateFile> _duplicateFiles = [];
    private readonly List<string> _ignoredFiles = [];

    public void Process()
    {
        Console.WriteLine("Vima MediaSorter v0.2.0");

        List<MediaFile> mediaFiles = IdentifyMediaFiles();
        SortMedia(mediaFiles);
        HandleDuplicatesIfExist();

        Console.WriteLine("Press enter to finish...");
        Console.ReadLine();
    }

    private List<MediaFile> IdentifyMediaFiles()
    {
        Console.Write("Identifying your media... ");
        using ProgressBar progress = new();
        ConcurrentBag<string> stepLogs = [];

        // Find previously used media directories.
        string[] directoryPaths = Directory.GetDirectories(sourceDirectory);
        foreach (string directoryPath in directoryPaths)
        {
            string directoryName = new DirectoryInfo(directoryPath).Name;
            if (directoryName.Length < FolderNameFormat.Length)
            {
                continue;
            }

            string directoryNameBeginning = directoryName[..FolderNameFormat.Length];
            if (!DateTime.TryParseExact(directoryNameBeginning, FolderNameFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
            {
                continue;
            }

            if (_dateToExistingDirectoryMapping.TryGetValue(result.Date, out string? existingDirectoryName))
            {
                stepLogs.Add(
                    $"\tWarning: Multiple directories mapped to date: '{result.ToShortDateString()}'. Only '{existingDirectoryName}' is going to be used.");
                continue;
            }

            _dateToExistingDirectoryMapping.Add(result.Date, directoryName);
        }

        // Get all file paths from source and children directories.
        IEnumerable<string> directoriesToScan = new List<string> { sourceDirectory }.Concat(directoryPaths);
        List<string> filePaths = [];
        foreach (string directoryToScan in directoriesToScan)
        {
            filePaths.AddRange(Directory.GetFiles(directoryToScan));
        }

        ConcurrentBag<MediaFile> mediaFiles = [];
        int processedFileCounter = 0;
        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
        {
            if (ImageExtensions.Contains(Path.GetExtension(filePath).ToUpperInvariant()))
            {
                MediaFile mediaFile = new(filePath, MediaFileType.Image);
                MediaMetadataHelper.SetCreatedDateTime(mediaFile);
                mediaFiles.Add(mediaFile);
            }
            else if (VideoExtensions.Contains(Path.GetExtension(filePath).ToUpperInvariant()))
            {
                MediaFile mediaFile = new(filePath, MediaFileType.Video);
                IEnumerable<string> relatedFiles = RelatedFilesHelper.FindAll(filePath);
                mediaFile.RelatedFiles.AddRange(relatedFiles);
                MediaMetadataHelper.SetCreatedDateTime(mediaFile);
                mediaFiles.Add(mediaFile);
            }
            else
            {
                _ignoredFiles.Add(filePath);
            }

            Interlocked.Increment(ref processedFileCounter);
            // ReSharper disable once AccessToDisposedClosure
            progress.Report((double)processedFileCounter / filePaths.Count);
        });

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (string stepLog in stepLogs)
        {
            Console.WriteLine(stepLog);
        }

        return [.. mediaFiles];
    }

    private void SortMedia(IReadOnlyCollection<MediaFile> files)
    {
        if (files.Count == 0)
        {
            Console.WriteLine("No media files found.");
            return;
        }

        IEnumerable<MediaFile> mediaFilesToApplyOffset = files.Where(x =>
            x.CreatedOnSource == CreatedOnSource.MetadataUtc).ToList();
        if (mediaFilesToApplyOffset.Any())
        {
            TimeSpan utcOffset = ConsoleHelper.GetVideoUtcOffsetFromUser();
            foreach (MediaFile mediaFileToApplyOffset in mediaFilesToApplyOffset)
            {
                mediaFileToApplyOffset.SetCreatedOn(
                    mediaFileToApplyOffset.CreatedOn + utcOffset,
                    CreatedOnSource.MetadataLocal);
            }
        }

        Console.Write("Sorting your media... ");
        using ProgressBar progress = new();
        ConcurrentBag<string> stepLogs = [];
        int processedFileCounter = 0;
        Parallel.ForEach(files, new() { MaxDegreeOfParallelism = 25 }, file =>
        {
            try
            {
                if (file.CreatedOn == null)
                {
                    stepLogs.Add($"\tWarning: No creation date detected: {file.FilePath}");
                    return;
                }

                MoveFile(file.FilePath, file.CreatedOn.Value);
                foreach (string filePath in file.RelatedFiles)
                {
                    MoveFile(filePath, file.CreatedOn.Value);
                }
            }
            catch (Exception)
            {
                // ignored
                stepLogs.Add($"\tWarning: Failed to sort: {file.FilePath}");
            }

            Interlocked.Increment(ref processedFileCounter);
            // ReSharper disable once AccessToDisposedClosure
            progress.Report((double)processedFileCounter / files.Count);
        });

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (string stepLog in stepLogs)
        {
            Console.WriteLine(stepLog);
        }
    }

    private void MoveFile(string filePath, DateTime createdDateTime)
    {
        if (!_dateToExistingDirectoryMapping.TryGetValue(createdDateTime.Date, out string? directoryName))
        {
            directoryName = createdDateTime.ToString("yyyy_MM_dd -");
        }

        string destinationFolderPath = Path.Combine(sourceDirectory, directoryName);
        if (filePath.StartsWith(destinationFolderPath)) return;

        (FileMovingHelper.MoveStatus status, string? destinationPath) =
            FileMovingHelper.MoveFile(filePath, destinationFolderPath);
        if (status == FileMovingHelper.MoveStatus.Duplicate)
        {
            DuplicateFile duplicateFile = new(filePath, destinationPath ?? string.Empty);
            _duplicateFiles.Add(duplicateFile);
        }
    }

    private void HandleDuplicatesIfExist()
    {
        if (_duplicateFiles.Count == 0) return;

        Console.Write($"Detected {_duplicateFiles.Count} duplicate file(s). ");
        ConsoleKey response = ConsoleHelper.AskYesNoQuestion("Would you like to delete them?", ConsoleKey.N);
        if (response != ConsoleKey.Y) return;

        Console.Write("Deleting duplicated files... ");
        using ProgressBar progress = new();
        for (int index = 0; index < _duplicateFiles.Count; index++)
        {
            DuplicateFile duplicateFile = _duplicateFiles[index];
            File.Delete(duplicateFile.OriginalFilePath);
            progress.Report((double)index / _duplicateFiles.Count);
        }

        progress.Dispose();
        Console.WriteLine("Done.");
    }
}