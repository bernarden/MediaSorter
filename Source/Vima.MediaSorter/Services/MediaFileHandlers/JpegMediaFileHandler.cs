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
    public override MediaFile Handle(string filePath)
    {
        var createdOn = TryGetDateFromFileName(filePath);
        createdOn ??= GetImageCreatedOnFromExif(filePath);
        MediaFile mediaFile = createdOn is null
            ? new MediaFile(filePath)
            : new MediaFileWithDate(filePath, createdOn);
        return mediaFile;
    }

    public static CreatedOn? GetImageCreatedOnFromExif(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        var directories = JpegMetadataReader.ReadMetadata(fs, [new ExifReader()]);
        var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfdDirectory == null) return null;

        if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime result))
            return new(result, CreatedOnSource.MetadataLocal);

        return null;
    }
}