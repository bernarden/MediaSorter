using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.QuickTime;
using Vima.MediaSorter.Domain;
using Directory = MetadataExtractor.Directory;

namespace Vima.MediaSorter.Helpers;

public partial class MediaMetadataHelper
{
    public static DateTime? SetCreatedDateTime(MediaFile file)
    {
        (DateTime? createdOn, CreatedOnSource? createdOnSource) = file.MediaMediaFileType switch
        {
            MediaFileType.Image => GetImageCreatedOn(file.FilePath),
            MediaFileType.Video => GetVideoCreatedOn(file.FilePath),
            _ => throw new ArgumentOutOfRangeException(nameof(file))
        };
        file.SetCreatedOn(createdOn, createdOnSource);
        return createdOn;
    }

    public static (DateTime?, CreatedOnSource?) GetImageCreatedOn(string filePath)
    {
        if (TryGetCreatedOnFromFileName(filePath, out DateTime? dateTimeFromName))
            return (dateTimeFromName, CreatedOnSource.FileName);

        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        IReadOnlyList<Directory> directories =
            JpegMetadataReader.ReadMetadata(fs, [new ExifReader()]);
        ExifSubIfdDirectory? subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfdDirectory == null) return (null, null);
        if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime result))
            return (result, CreatedOnSource.MetadataLocal);

        return (null, null);
    }

    public static (DateTime?, CreatedOnSource?) GetVideoCreatedOn(string filePath)
    {
        if (TryGetCreatedOnFromFileName(filePath, out DateTime? dateTimeFromName))
            return (dateTimeFromName, CreatedOnSource.FileName);

        // Try to get creation date from metadata tag.
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        IReadOnlyList<Directory> directories = QuickTimeMetadataReader.ReadMetadata(fs);
        QuickTimeMovieHeaderDirectory? subIfdDirectory = directories
            .OfType<QuickTimeMovieHeaderDirectory>()
            .FirstOrDefault();
        if (subIfdDirectory == null) return (null, null);
        if (subIfdDirectory.TryGetDateTime(
                QuickTimeMovieHeaderDirectory.TagCreated, out DateTime tagCreatedUtcResult) &&
            !tagCreatedUtcResult.Equals(new(1904, 01, 01, 0, 0, 0)))
            return (tagCreatedUtcResult, CreatedOnSource.MetadataUtc);

        return (null, null);
    }

    public static bool TryGetCreatedOnFromFileName(string filePath, out DateTime? createdOn)
    {
        // Try to get creation date from file name.
        string fileName = Path.GetFileName(filePath);
        Regex rgx = GetCreatedOnFileNameRegex();
        Match mat = rgx.Match(fileName);
        if (mat.Success)
        {
            GroupCollection groups = mat.Groups;
            int year = int.Parse(groups["year"].Value);
            int month = int.Parse(groups["month"].Value);
            int day = int.Parse(groups["day"].Value);
            createdOn = new DateTime(year, month, day);
            return true;
        }

        createdOn = null;
        return false;
    }

    [GeneratedRegex(@"(?<year>[12]\d{3})(?<month>0[1-9]|1[0-2])(?<day>[012]\d|3[01])")]
    private static partial Regex GetCreatedOnFileNameRegex();
}