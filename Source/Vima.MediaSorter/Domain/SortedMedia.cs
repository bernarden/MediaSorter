using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class SortedMedia
{
    public IReadOnlyList<SuccessfulFileMove> Moved { get; init; } = [];
    public IReadOnlyList<DuplicateDetectedFileMove> Duplicates { get; init; } = [];
    public IReadOnlyList<ErroredFileMove> Errors { get; init; } = [];
}
