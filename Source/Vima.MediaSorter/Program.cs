using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter
{
    public class Program
    {
        public static readonly List<string> ImageExtensions = new List<string>
            { ".JPG", ".JPE", ".BMP", ".GIF", ".PNG" };

        public static readonly List<string> VideoExtensions = new List<string> { ".MP4" };
        public static readonly List<string> DuplicateFiles = new List<string>();

        public static void Main(string[] args)
        {
            string mainFolderPath = Directory.GetCurrentDirectory();
            string[] files = Directory.GetFiles(mainFolderPath);

            Console.Write("Sorting your media... ");
            using (ProgressBar progress = new ProgressBar())
            {
                for (var index = 0; index < files.Length; index++)
                {
                    string file = files[index];
                    if (ImageExtensions.Contains(Path.GetExtension(file).ToUpperInvariant()))
                    {
                        DateTime? imageCreatedDate = MediaMetadataHelper.GetImageDatetimeCreatedFromMetadata(file);
                        if (imageCreatedDate != null)
                        {
                            MoveFile(mainFolderPath, file, imageCreatedDate.Value);
                        }
                    }
                    else if (VideoExtensions.Contains(Path.GetExtension(file).ToUpperInvariant()))
                    {
                        DateTime? videoCreatedDate = MediaMetadataHelper.GetVideoCreatedDateTimeFromName(file);
                        if (videoCreatedDate != null)
                        {
                            MoveFile(mainFolderPath, file, videoCreatedDate.Value);
                        }
                    }

                    progress.Report((double)index / files.Length);
                }
            }

            Console.WriteLine("Done.");

            if (DuplicateFiles.Any())
            {
                Console.Write($"Detected {DuplicateFiles.Count} duplicate file(s). ");
                ConsoleKey response = ConsoleHelper.AskYesNoQuestion("Would you like to delete them?", ConsoleKey.N);

                if (response == ConsoleKey.Y)
                {
                    Console.Write("Deleting duplicated files... ");
                    using ProgressBar progress = new ProgressBar();
                    for (var index = 0; index < DuplicateFiles.Count; index++)
                    {
                        var duplicateFile = DuplicateFiles[index];
                        File.Delete(duplicateFile);
                        progress.Report((double)index / DuplicateFiles.Count);
                    }

                    Console.WriteLine("Done.");
                }
            }

            Console.WriteLine("Press enter to finish...");
            Console.ReadLine();
        }

        private static void MoveFile(string mainFolderPath, string file, DateTime createdDate)
        {
            string newFolderName = createdDate.ToString("yyyy_MM_dd -");
            string newFolderPath = Path.Combine(mainFolderPath, newFolderName);
            Directory.CreateDirectory(newFolderPath);

            string filePathInNewFolder = Path.Combine(newFolderPath, Path.GetFileName(file));
            if (File.Exists(filePathInNewFolder))
            {
                if (DuplicationHelper.AreFilesIdentical(file, filePathInNewFolder))
                {
                    DuplicateFiles.Add(file);
                    return;
                }

                int count = 1;
                do
                {
                    filePathInNewFolder = Path.Combine(newFolderPath,
                        $"{Path.GetFileNameWithoutExtension(file)} ({count}){Path.GetExtension(file)}");
                    count++;
                } while (File.Exists(filePathInNewFolder));
            }

            File.Move(file, filePathInNewFolder);
        }
    }
}