using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Helpers
{
    public class MediaMetadataHelper
    {
        public static DateTime? GetCreatedDateTime(MediaFile file)
        {
            return file.MediaType switch
            {
                MediaFile.Type.Image => GetImageDatetimeCreatedFromMetadata(file.FilePath),
                MediaFile.Type.Video => GetVideoCreatedDateTimeFromName(file.FilePath),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public static DateTime? GetImageDatetimeCreatedFromMetadata(string filePath)
        {
            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var directories = JpegMetadataReader.ReadMetadata(fs, new[] { new ExifReader() });
            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            return subIfdDirectory?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal);
        }

        public static DateTime? GetVideoCreatedDateTimeFromName(string filePath)
        {
            Regex rgx = new Regex(@"(?<year>[12]\d{3})(?<month>0[1-9]|1[0-2])(?<day>[012]\d|3[01])");
            Match mat = rgx.Match(filePath);
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
    }
}