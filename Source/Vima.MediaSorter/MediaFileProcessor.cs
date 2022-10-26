using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter;

public class MediaFileProcessor
{
    public static readonly List<string> ImageExtensions = new() { ".JPG" };
    public static readonly List<string> VideoExtensions = new() { ".MP4" };
    private readonly IList<DuplicateFile> _duplicateFiles;
    private readonly string _sourceDirectory;

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
        string[] filePaths = Directory.GetFiles(_sourceDirectory);
        List<MediaFile> mediaFiles = new();
        for (int index = 0; index < filePaths.Length; index++)
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

            progress.Report((double)index / filePaths.Length);
        }

        progress.Dispose();
        Console.WriteLine("Done.");
        return mediaFiles;
    }

    private void SortMedia(IReadOnlyList<MediaFile> files)
    {
        if (!files.Any())
        {
            Console.WriteLine("No media files found.");
            return;
        }

        Console.Write("Sorting your media... ");
        using ProgressBar progress = new();
        for (int index = 0; index < files.Count; index++)
        {
            MediaFile file = files[index];
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
            }
            catch (Exception)
            {
                // ignored
            }

            progress.Report((double)index / files.Count);
        }

        progress.Dispose();
        Console.WriteLine("Done.");
    }

    private void MoveFile(string filePath, DateTime createdDateTime)
    {
        (FileMovingHelper.MoveStatus status, string? destinationPath) =
            FileMovingHelper.MoveFile(_sourceDirectory, filePath, createdDateTime);
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