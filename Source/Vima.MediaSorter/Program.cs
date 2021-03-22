using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter
{
    public class Program
    {
        public static readonly List<string> ImageExtensions = new() { ".JPG" };

        public static readonly List<string> VideoExtensions = new() { ".MP4" };

        public static void Main(string[] args)
        {
            string sourceDirectory = Directory.GetCurrentDirectory();
            List<MediaFile> mediaFiles = IdentifyMediaFiles(sourceDirectory);
            SortMedia(mediaFiles, sourceDirectory, out IList<MediaFile> duplicateFiles);
            HandleDuplicatesIfExist(duplicateFiles);

            Console.WriteLine("Press enter to finish...");
            Console.ReadLine();
        }

        private static List<MediaFile> IdentifyMediaFiles(string sourceDirectory)
        {
            Console.Write("Identifying your media... ");
            using ProgressBar progress = new();
            string[] filePaths = Directory.GetFiles(sourceDirectory);
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
                    mediaFiles.Add(new MediaFile(filePath, MediaFile.Type.Video));
                }

                progress.Report((double) index / filePaths.Length);
            }

            Console.WriteLine("Done.");
            return mediaFiles;
        }

        private static void SortMedia(IReadOnlyList<MediaFile> files, string destination,
            out IList<MediaFile> duplicateFiles)
        {
            duplicateFiles = new List<MediaFile>();
            if (!files.Any())
            {
                Console.WriteLine("No media files found.");
                return;
            }

            Console.Write("Sorting your media... ");
            using (ProgressBar progress = new())
            {
                for (int index = 0; index < files.Count; index++)
                {
                    MediaFile file = files[index];
                    DateTime? createdDateTime = MediaMetadataHelper.GetCreatedDateTime(file);
                    if (createdDateTime != null)
                    {
                        FileMovingHelper.MoveStatus result =
                            FileMovingHelper.MoveFile(destination, file.FilePath, createdDateTime.Value);
                        if (result == FileMovingHelper.MoveStatus.Duplicate)
                        {
                            duplicateFiles.Add(file);
                        }
                    }

                    progress.Report((double) index / files.Count);
                }
            }

            Console.WriteLine("Done.");
        }

        private static void HandleDuplicatesIfExist(IList<MediaFile> duplicateFiles)
        {
            if (!duplicateFiles.Any())
            {
                return;
            }

            Console.Write($"Detected {duplicateFiles.Count} duplicate file(s). ");
            ConsoleKey response = ConsoleHelper.AskYesNoQuestion("Would you like to delete them?", ConsoleKey.N);
            if (response != ConsoleKey.Y)
            {
                return;
            }

            Console.Write("Deleting duplicated files... ");
            using ProgressBar progress = new();
            for (int index = 0; index < duplicateFiles.Count; index++)
            {
                MediaFile duplicateFile = duplicateFiles[index];
                File.Delete(duplicateFile.FilePath);
                progress.Report((double) index / duplicateFiles.Count);
            }

            Console.WriteLine("Done.");
        }
    }
}