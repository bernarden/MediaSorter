using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ImageMagick;

namespace Vima.MediaSorter
{
    public class Program
    {
        public static readonly List<string> ImageExtensions = new List<string> { ".JPG", ".JPE", ".BMP", ".GIF", ".PNG" };
        public static readonly List<string> VideoExtensions = new List<string> { ".MP4" };

        public static void Main(string[] args)
        {
            int count = 0;
            string mainFolderPath = Directory.GetCurrentDirectory();
            string[] files = Directory.GetFiles(mainFolderPath);
            
            foreach (string file in files)
            {
                if (ImageExtensions.Contains(Path.GetExtension(file).ToUpperInvariant()))
                {
                    DateTime? imageCreatedDate = GetImageDatetimeCreatedFromMetadata(file);
                    if (imageCreatedDate != null)
                    {
                        MoveFile(mainFolderPath, file, imageCreatedDate.Value);
                    }
                }
                else if (VideoExtensions.Contains(Path.GetExtension(file).ToUpperInvariant()))
                {
                    DateTime? videoCreatedDate = GetVideoCreatedDateTimeFromName(file);
                    if (videoCreatedDate != null)
                    {
                        MoveFile(mainFolderPath, file, videoCreatedDate.Value);
                    }
                }

                count++;
                DrawTextProgressBar(count, files.Length);
            }

            Console.Out.WriteLine("Press enter to finish...");
            Console.ReadLine();
        }

        private static void MoveFile(string mainFolderPath, string file, DateTime createdDate)
        {
            string newFolderName = createdDate.ToString("yyyy_MM_dd -");
            string newFolderPath = Path.Combine(mainFolderPath, newFolderName);
            Directory.CreateDirectory(newFolderPath);

            string filePathInNewFolder = Path.Combine(newFolderPath, Path.GetFileName(file));
            File.Move(file, filePathInNewFolder);
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
        private static DateTime? GetVideoCreatedDateTimeFromName(string file)
        {
            Regex rgx = new Regex(@"(?<year>[12]\d{3})(?<month>0[1-9]|1[0-2])(?<day>[012]\d|3[01])");
            Match mat = rgx.Match(file);             
            if (!mat.Success)
            {
                return null;
            }

            GroupCollection groups = mat.Groups;            
            int year = int.Parse(groups["year"].Value);
            int month = int.Parse(groups["month"].Value);
            int day = int.Parse(groups["day"].Value);
            return new DateTime(year, month, day);
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