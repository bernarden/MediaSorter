using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class SortedMedia
{
    public List<SuccessfulFileMove> Moved { get; init; } = [];
    public List<DuplicateDetectedFileMove> Duplicates { get; init; } = [];
    public List<ErroredFileMove> Errors { get; init; } = [];
}
