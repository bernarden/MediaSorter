using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using System;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services.MediaFileHandlers;

public class JpegMediaFileHandler() : BaseMediaFileHandler(".jpg", ".jpeg")
{
    public override CreatedOn? GetCreatedOnDateFromMetadata(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        var directories = JpegMetadataReader.ReadMetadata(fs, [new ExifReader()]);
        var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfdDirectory == null) return null;

        if (!subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime createdOn))
            return null;

        var subSecStr = subIfdDirectory.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal)?.Trim();
        if (!string.IsNullOrEmpty(subSecStr) && int.TryParse(subSecStr, out int subSec))
        {
            createdOn = createdOn.AddMilliseconds(subSec * Math.Pow(10, 3 - subSecStr.Length));
        }

        return new CreatedOn(createdOn, CreatedOnSource.MetadataLocal);
    }
}
