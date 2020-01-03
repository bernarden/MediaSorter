using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
            int numberOfProcessedFiles = 0;
            string mainFolderPath = Directory.GetCurrentDirectory();
            string[] files = Directory.GetFiles(mainFolderPath);

            foreach (string file in files)
            {
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

                numberOfProcessedFiles++;
                DrawTextProgressBar(numberOfProcessedFiles, files.Length);
            }

            if (DuplicateFiles.Any())
            {
                Console.Write($"Detected {DuplicateFiles.Count} duplicate file(s). ");
                ConsoleKey response = ConsoleKey.Enter;
                while (response != ConsoleKey.Y && response != ConsoleKey.N)
                {
                    Console.Write("Would you like to delete them? [y/n] ");
                    response = Console.ReadKey(false).Key;
                    if (response != ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                    }
                }

                if (response == ConsoleKey.Y)
                {
                    foreach (var duplicateFile in DuplicateFiles)
                    {
                        File.Delete(duplicateFile);
                    }
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

        private static void DrawTextProgressBar(int progress, int total)
        {
            const int numberOfSlots = 50;
            var numberOfHashesPerFile = (float)numberOfSlots / total;
            var numberOfHashesCompleted = (int)Math.Floor(progress * numberOfHashesPerFile);
            var numberOfMissingHashes = numberOfSlots - numberOfHashesCompleted;

            StringBuilder loadingBarBuilder = new StringBuilder();
            loadingBarBuilder.Append('[');
            loadingBarBuilder.Append(new string('#', numberOfHashesCompleted));
            loadingBarBuilder.Append(new string(' ', numberOfMissingHashes));
            loadingBarBuilder.Append(']');
            Console.SetCursorPosition(0, 0);
            Console.Write(loadingBarBuilder.ToString());

            Console.WriteLine();
            Console.WriteLine(progress + " of " + total);
        }
    }
}