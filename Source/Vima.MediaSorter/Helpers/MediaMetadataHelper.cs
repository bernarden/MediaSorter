using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ImageMagick;
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
            using var image = new MagickImage(filePath);
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