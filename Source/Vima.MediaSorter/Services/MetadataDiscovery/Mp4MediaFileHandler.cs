using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter.Services.MetadataDiscovery;

public class Mp4MediaFileHandler() : BaseMediaFileHandler
{
    private static readonly HashSet<string> SupportedExtensions = new() { ".mp4" };

    public override bool CanHandle(string ext) => SupportedExtensions.Contains(ext.ToLowerInvariant());

    public override MediaFile Handle(string filePath)
    {
        var mediaFile = new MediaFile(filePath);
        mediaFile.RelatedFiles.AddRange(RelatedFilesHelper.FindAll(filePath));
        var createdOn = TryGetDateFromFileName(filePath);
        createdOn ??= GetVideoCreatedOn(filePath);
        mediaFile.SetCreatedOn(createdOn);
        return mediaFile;
    }

    public static CreatedOn? GetVideoCreatedOn(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        IReadOnlyList<MetadataExtractor.Directory> directories =
            QuickTimeMetadataReader.ReadMetadata(fs);
        QuickTimeMovieHeaderDirectory? subIfdDirectory = directories
            .OfType<QuickTimeMovieHeaderDirectory>()
            .FirstOrDefault();

        if (subIfdDirectory == null) return null;

        if (subIfdDirectory.TryGetDateTime(
                QuickTimeMovieHeaderDirectory.TagCreated, out DateTime tagCreatedUtcResult) &&
            !tagCreatedUtcResult.Equals(new(1904, 01, 01, 0, 0, 0)))
            return new(tagCreatedUtcResult, CreatedOnSource.MetadataUtc);

        return null;
    }
}