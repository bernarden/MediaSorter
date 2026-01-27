using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class IdentifiedMedia
{
    public IReadOnlyList<MediaFileWithDate> MediaFilesWithDates { get; init; } = new List<MediaFileWithDate>();
    public IReadOnlyList<MediaFile> MediaFilesWithoutDates { get; init; } = new List<MediaFile>();
    public IReadOnlyList<FileIdentificationError> ErroredFiles { get; init; } = new List<FileIdentificationError>();
    public IReadOnlyList<string> UnsupportedFiles { get; init; } = new List<string>();
}