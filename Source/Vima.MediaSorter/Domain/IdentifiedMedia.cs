using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class IdentifiedMedia
{
    public IReadOnlyList<MediaFile> MediaFiles { get; init; } = new List<MediaFile>();
    public IReadOnlyList<string> IgnoredFiles { get; init; } = new List<string>();
}