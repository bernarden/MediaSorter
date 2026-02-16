using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;
using System;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services.MediaFileHandlers;

public class Mp4MediaFileHandler() : BaseMediaFileHandler(".mp4")
{
    public override CreatedOn? GetCreatedOnDateFromMetadata(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        var directories = QuickTimeMetadataReader.ReadMetadata(fs);
        var subIfdDirectory = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (subIfdDirectory == null) return null;

        if (subIfdDirectory.TryGetDateTime(
                QuickTimeMovieHeaderDirectory.TagCreated, out DateTime tagCreatedUtcResult) &&
            !tagCreatedUtcResult.Equals(new(1904, 01, 01, 0, 0, 0)))
            return new(tagCreatedUtcResult, CreatedOnSource.MetadataUtc);

        return null;
    }
}