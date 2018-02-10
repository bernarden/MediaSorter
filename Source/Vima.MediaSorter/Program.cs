using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ImageMagick;

namespace Vima.MediaSorter
{
    public class Program
    {
        public static readonly List<string> ImageExtensions = new List<string> { ".JPG", ".JPE", ".BMP", ".GIF", ".PNG" };

        public static void Main(string[] args)
        {
            int count = 0;
            const string mainFolderPath = "G:\\Pictures";
            string[] files = Directory.GetFiles(mainFolderPath);
            foreach (string file in files)
            {
                if (ImageExtensions.Contains(Path.GetExtension(file).ToUpperInvariant()))
                {
                    DateTime? value = GetImageDatetimeCreatedFromMetadata(file);
                    if (value == null)
                        continue;

                    string newFolderName = value.Value.ToString("yyyy_MM_dd -");
                    string newFolderPath = Path.Combine(mainFolderPath, newFolderName);
                    Directory.CreateDirectory(newFolderPath);

                    string filePathInNewFolder = Path.Combine(newFolderPath, Path.GetFileName(file));
                    File.Move(file, filePathInNewFolder);

                }
                count++;
                DrawTextProgressBar(count, files.Length);
            }
            Console.Out.WriteLine("Press enter to finish...");
            Console.ReadLine();
        }

        private static DateTime? GetImageDatetimeCreatedFromMetadata(string file)
        {
            using (var image = new MagickImage(file))
            {
                ExifProfile profile = image.GetExifProfile();
                if (profile == null)
                {
                    return null;
                }

                var dateTimeCreated = profile.GetValue(ExifTag.DateTimeOriginal);
                if (string.IsNullOrEmpty(dateTimeCreated?.Value.ToString()))
                    return null;

                DateTime time = DateTime.ParseExact(dateTimeCreated.Value.ToString(), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                return time;
            }
        }

        private static void DrawTextProgressBar(int progress, int total)
        {
            Console.SetCursorPosition(0, 0);

            int slots = total < 20 ? total : 20;

            string loadingBar = "[";

            for (int i = 1; i <= slots; i++)
            {
                loadingBar += ' ';
            }

            loadingBar += ']';

            Console.Write(loadingBar);

            int count = 1;
            for (int i = total / slots; i <= total; i += total / slots)
            {
                if (progress < i)
                {
                    break;
                }
                Console.SetCursorPosition(count, 0);
                Console.Write('#');
                count++;
            }

            Console.WriteLine();
            Console.WriteLine(progress + " of " + total);
        }
    }
}