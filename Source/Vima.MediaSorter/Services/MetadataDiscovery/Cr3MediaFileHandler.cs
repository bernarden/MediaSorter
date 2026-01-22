using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services.MetadataDiscovery;

public class Cr3MediaFileHandler() : BaseMediaFileHandler
{
    private static readonly HashSet<string> SupportedExtensions = new() { ".cr3" };

    public override bool CanHandle(string ext) => SupportedExtensions.Contains(ext.ToLowerInvariant());

    public override MediaFile Handle(string filePath)
    {
        var mediaFile = new MediaFile(filePath);
        var createdOn = TryGetDateFromFileName(filePath);
        createdOn ??= GetCr3CreatedOn(filePath);
        mediaFile.SetCreatedOn(createdOn);
        return mediaFile;
    }

    private static CreatedOn? GetCr3CreatedOn(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        var directories = QuickTimeMetadataReader.ReadMetadata(fs);
        var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfdDirectory == null) return null;

        if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime result))
            return new(result, CreatedOnSource.MetadataLocal);

        return null;
    }
}