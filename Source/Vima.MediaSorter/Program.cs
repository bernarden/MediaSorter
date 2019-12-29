using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ImageMagick;

namespace Vima.MediaSorter
{
    public class Program
    {
        public static readonly List<string> ImageExtensions = new List<string> {".JPG", ".JPE", ".BMP", ".GIF", ".PNG"};

        public static void Main(string[] args)
        {
            int count = 0;
            string mainFolderPath = Directory.GetCurrentDirectory();
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

                DateTime time = DateTime.ParseExact(dateTimeCreated.Value.ToString(), "yyyy:MM:dd HH:mm:ss",
                    CultureInfo.InvariantCulture);
                return time;
            }
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