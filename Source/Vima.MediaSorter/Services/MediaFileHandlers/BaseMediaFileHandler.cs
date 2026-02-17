using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services.MediaFileHandlers;

public interface IMediaFileHandler
{
    public IReadOnlySet<string> SupportedExtensions { get; }

    bool CanHandle(string extension);

    MediaFile CreateMediaFile(string filePath);

    CreatedOn? GetCreatedOnDateFromMetadata(string filePath);
}

public abstract partial class BaseMediaFileHandler(params string[] extensions) : IMediaFileHandler
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

    public bool CanHandle(string ext) => SupportedExtensions.Contains(ext);

    public MediaFile CreateMediaFile(string filePath)
    {
        var createdOn = TryGetDateFromFileName(filePath);
        createdOn ??= GetCreatedOnDateFromMetadata(filePath);
        MediaFile mediaFile = createdOn is null
            ? new MediaFile(filePath)
            : new MediaFileWithDate(filePath, createdOn);
        AfterHandleEffect(mediaFile);
        return mediaFile;
    }

    protected virtual void AfterHandleEffect(MediaFile filePath) { }

    public abstract CreatedOn? GetCreatedOnDateFromMetadata(string filePath);

    protected static CreatedOn? TryGetDateFromFileName(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        Match match = FileNameRegex().Match(fileName);

        if (match.Success)
        {
            DateTime date = new(
                int.Parse(match.Groups["year"].Value),
                int.Parse(match.Groups["month"].Value),
                int.Parse(match.Groups["day"].Value),
                int.Parse(match.Groups["hour"].Value),
                int.Parse(match.Groups["minute"].Value),
                int.Parse(match.Groups["second"].Value)
            );
            return new CreatedOn(date, CreatedOnSource.FileName);
        }
        return null;
    }

    [GeneratedRegex(
        @"(?<year>[12]\d{3})(?<month>0[1-9]|1[0-2])(?<day>[012]\d|3[01])[^\d](?<hour>[012]\d)(?<minute>[0-5]\d)(?<second>[0-5]\d)"
    )]
    private static partial Regex FileNameRegex();
}