using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter;

public class MediaFileProcessor
{
    public static readonly string FolderNameFormat = "yyyy_MM_dd -";
    public static readonly List<string> ImageExtensions = new() { ".JPG", ".JPEG" };
    public static readonly List<string> VideoExtensions = new() { ".MP4" };
    private readonly IList<DuplicateFile> _duplicateFiles;
    private readonly string _sourceDirectory;
    private readonly Dictionary<DateTime, string> _dateToExistingDirectoryMapping = new();
    private readonly List<string> _ignoredFiles = new();

    public MediaFileProcessor(string sourceDirectory)
    {
        _sourceDirectory = sourceDirectory;
        _duplicateFiles = new List<DuplicateFile>();
    }

    public void Process()
    {
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
        List<string> stepLogs = new();

        // Find previously used media directories.
        string[] directoryPaths = Directory.GetDirectories(_sourceDirectory);
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
        IEnumerable<string> directoriesToScan = new List<string> { _sourceDirectory }.Concat(directoryPaths);
        List<string> filePaths = new();
        foreach (string directoryToScan in directoriesToScan)
        {
            filePaths.AddRange(Directory.GetFiles(directoryToScan));
        }

        List<MediaFile> mediaFiles = new();
        for (int index = 0; index < filePaths.Count; index++)
        {
            string filePath = filePaths[index];
            if (ImageExtensions.Contains(Path.GetExtension(filePath).ToUpperInvariant()))
            {
                mediaFiles.Add(new MediaFile(filePath, MediaFile.Type.Image));
            }
            else if (VideoExtensions.Contains(Path.GetExtension(filePath).ToUpperInvariant()))
            {
                MediaFile mediaFile = new(filePath, MediaFile.Type.Video);
                IEnumerable<string> relatedFiles = RelatedFilesHelper.FindAll(filePath);
                mediaFile.RelatedFiles.AddRange(relatedFiles);
                mediaFiles.Add(mediaFile);
            }
            else
            {
                _ignoredFiles.Add(filePath);
            }

            progress.Report((double)index / filePaths.Count);
        }

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (string stepLog in stepLogs)
            Console.WriteLine(stepLog);

        return mediaFiles;
    }

    private void SortMedia(IReadOnlyCollection<MediaFile> files)
    {
        if (!files.Any())
        {
            Console.WriteLine("No media files found.");
            return;
        }

        Console.Write("Sorting your media... ");
        using ProgressBar progress = new();
        List<string> stepLogs = new();
        int processedFileCounter = 0;
        Parallel.ForEach(files,
            new ParallelOptions { MaxDegreeOfParallelism = 25 },
            file =>
            {
                try
                {
                    DateTime? createdDateTime = MediaMetadataHelper.GetCreatedDateTime(file);
                    if (createdDateTime != null)
                    {
                        MoveFile(file.FilePath, createdDateTime.Value);
                        foreach (string filePath in file.RelatedFiles)
                        {
                            MoveFile(filePath, createdDateTime.Value);
                        }
                    }
                    else
                    {
                        stepLogs.Add($"\tWarning: No creation date detected: {file.FilePath}");
                    }
                }
                catch (Exception)
                {
                    // ignored
                    stepLogs.Add($"\tWarning: Failed to sort: {file.FilePath}");
                }

                Interlocked.Increment(ref processedFileCounter);
                progress.Report((double)processedFileCounter / files.Count);
            });

        progress.Dispose();
        Console.WriteLine("Done.");

        foreach (string stepLog in stepLogs)
            Console.WriteLine(stepLog);
    }

    private void MoveFile(string filePath, DateTime createdDateTime)
    {
        if (!_dateToExistingDirectoryMapping.TryGetValue(createdDateTime.Date, out string? directoryName))
        {
            directoryName = createdDateTime.ToString("yyyy_MM_dd -");
        }

        string destinationFolderPath = Path.Combine(_sourceDirectory, directoryName);
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
        if (!_duplicateFiles.Any()) return;

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