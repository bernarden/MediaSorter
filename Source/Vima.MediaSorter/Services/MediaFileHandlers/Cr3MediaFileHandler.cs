using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using System;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services.MediaFileHandlers;

public class Cr3MediaFileHandler() : BaseMediaFileHandler(".cr3")
{
    public override MediaFile Handle(string filePath)
    {
        var createdOn = TryGetDateFromFileName(filePath);
        createdOn ??= GetCr3CreatedOn(filePath);
        MediaFile mediaFile = createdOn is null
            ? new MediaFile(filePath)
            : new MediaFileWithDate(filePath, createdOn);
        mediaFile.SetTargetSubFolder("raw");
        return mediaFile;
    }

    private static CreatedOn? GetCr3CreatedOn(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        var directories = QuickTimeMetadataReader.ReadMetadata(fs);
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