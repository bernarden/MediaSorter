using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class IdentifiedMedia
{
    public IReadOnlyList<MediaFileWithDate> MediaFilesWithDates { get; init; } = [];
    public IReadOnlyList<MediaFile> MediaFilesWithoutDates { get; init; } = [];
    public IReadOnlyList<PathError> ErroredFiles { get; init; } = [];
    public IReadOnlyList<string> UnsupportedFiles { get; init; } = [];
}
